use crate::{logs::CaddyLogStore, paths::RuntimePaths};
use anyhow::{Context, Result};
use cadder_protocol::{
  LogAttributionKind, LogSeverity, LogStreamIdentity, RuntimeState, RuntimeStatus,
};
use std::{path::PathBuf, process::Stdio, sync::Arc, time::Duration};
use tokio::{
  fs,
  io::{AsyncBufReadExt, BufReader},
  process::{Child, Command},
  sync::Mutex,
  time::timeout,
};

use crate::caddy::RealCaddyResolver;

#[derive(Debug, Clone)]
pub struct ProcessRuntime {
  resolver: RealCaddyResolver,
  paths: RuntimePaths,
  child: Arc<Mutex<Option<Child>>>,
}

impl ProcessRuntime {
  pub fn new(resolver: RealCaddyResolver, paths: RuntimePaths) -> Self {
    Self {
      resolver,
      paths,
      child: Arc::new(Mutex::new(None)),
    }
  }

  pub async fn inspect(&self) -> RuntimeState {
    let child = self.child.lock().await;
    if let Some(child) = child.as_ref() {
      RuntimeState {
        status: RuntimeStatus::Running,
        binary_path: self
          .resolver
          .resolve()
          .ok()
          .map(|path| path.display().to_string()),
        version: None,
        process_id: child.id(),
        admin_endpoint: Some("localhost:2019".to_string()),
        diagnostics: Vec::new(),
      }
    } else {
      RuntimeState::idle()
    }
  }

  pub async fn apply_config(&self, rendered: &[u8], logs: &CaddyLogStore) -> Result<()> {
    let config_path = self.paths.effective_config_path();
    fs::write(&config_path, rendered)
      .await
      .with_context(|| format!("write effective config {}", config_path.display()))?;

    if self.child.lock().await.is_none() {
      self.start(&config_path, logs).await?;
    } else {
      self.reload(&config_path, logs).await?;
    }
    Ok(())
  }

  async fn start(&self, config_path: &PathBuf, logs: &CaddyLogStore) -> Result<()> {
    let binary = self.resolver.resolve()?;
    let mut child = Command::new(binary)
      .arg("run")
      .arg("--config")
      .arg(config_path)
      .stdout(Stdio::piped())
      .stderr(Stdio::piped())
      .spawn()
      .context("start real Caddy runtime")?;

    if let Some(stdout) = child.stdout.take() {
      spawn_log_reader(stdout, logs.clone(), "stdout");
    }
    if let Some(stderr) = child.stderr.take() {
      spawn_log_reader(stderr, logs.clone(), "stderr");
    }

    *self.child.lock().await = Some(child);
    logs.append(
      LogStreamIdentity::runtime_control(),
      LogSeverity::Info,
      "real Caddy runtime started",
      LogAttributionKind::RuntimeControl,
      Some("start".to_string()),
    );
    Ok(())
  }

  async fn reload(&self, config_path: &PathBuf, logs: &CaddyLogStore) -> Result<()> {
    let binary = self.resolver.resolve()?;
    let output = Command::new(binary)
      .arg("reload")
      .arg("--config")
      .arg(config_path)
      .stdout(Stdio::piped())
      .stderr(Stdio::piped())
      .output()
      .await
      .context("reload real Caddy runtime")?;
    if output.status.success() {
      logs.append(
        LogStreamIdentity::runtime_control(),
        LogSeverity::Info,
        "real Caddy runtime reloaded",
        LogAttributionKind::RuntimeControl,
        Some("reload".to_string()),
      );
      Ok(())
    } else {
      let message = String::from_utf8_lossy(&output.stderr).to_string();
      logs.append(
        LogStreamIdentity::runtime_control(),
        LogSeverity::Error,
        &message,
        LogAttributionKind::RuntimeControl,
        Some("reload".to_string()),
      );
      anyhow::bail!("caddy reload failed: {message}");
    }
  }

  pub async fn stop(&self) -> Result<()> {
    let mut child = self.child.lock().await;
    if let Some(mut child) = child.take() {
      if let Ok(binary) = self.resolver.resolve() {
        let _ = request_graceful_stop(binary).await;
      }
      let _ = child.kill().await;
      let _ = child.wait().await;
    }
    Ok(())
  }
}

async fn request_graceful_stop(binary: PathBuf) -> Result<()> {
  let mut command = Command::new(binary);
  command
    .arg("stop")
    .arg("--address")
    .arg("localhost:2019")
    .stdout(Stdio::null())
    .stderr(Stdio::null())
    .kill_on_drop(true);
  let mut child = command.spawn().context("start caddy stop")?;
  if timeout(Duration::from_secs(5), child.wait()).await.is_err() {
    let _ = child.kill().await;
    let _ = child.wait().await;
  }
  Ok(())
}

fn spawn_log_reader<R>(reader: R, logs: CaddyLogStore, channel: &'static str)
where
  R: tokio::io::AsyncRead + Unpin + Send + 'static,
{
  tokio::spawn(async move {
    let mut reader = BufReader::new(reader).lines();
    while let Ok(Some(line)) = reader.next_line().await {
      let severity = if channel == "stderr" {
        LogSeverity::Error
      } else {
        LogSeverity::Info
      };
      logs.append(
        LogStreamIdentity {
          stream_id: "runtime".to_string(),
          domain_key: None,
          channel: channel.to_string(),
        },
        severity,
        line,
        LogAttributionKind::Runtime,
        None,
      );
    }
  });
}
