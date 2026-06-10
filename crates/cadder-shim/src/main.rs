use anyhow::{Context, Result, anyhow};
use cadder_daemon::{
  CadderSession, DaemonLaunchOptions, RealCaddyResolver, RuntimePaths,
  ensure_daemon_running_with_options,
};
use cadder_protocol::{
  ActivationState, BasicResponse, EntrypointInstanceIdentity, EntrypointRegistration,
  HeartbeatEntrypointRequest, LogStreamIdentity, OwnerProcessIdentity, RegisterEntrypointRequest,
  RegisterEntrypointResponse, ShimRunMetadata, SourcePath, UnregisterEntrypointRequest,
  message_types, new_request_id,
};
use chrono::Utc;
use clap::Parser;
use std::{
  env,
  path::PathBuf,
  process::{ExitCode, Stdio},
  time::Duration,
};
use tokio::{process::Command, sync::Mutex, time::interval};

#[derive(Debug, Parser)]
#[command(name = "caddy", version, about = "Cadder PATH-facing Caddy shim")]
struct ShimArgs {
  #[arg(long = "cadder-runtime-dir", hide = true)]
  runtime_dir: Option<PathBuf>,

  #[arg(long = "cadder-daemon-path", hide = true)]
  daemon_path: Option<PathBuf>,

  #[arg(long = "cadder-real-caddy-command", hide = true)]
  real_caddy_command: Option<String>,

  #[arg(trailing_var_arg = true, allow_hyphen_values = true)]
  caddy_args: Vec<String>,
}

#[tokio::main]
async fn main() -> Result<ExitCode> {
  let args = ShimArgs::parse();
  if args
    .caddy_args
    .iter()
    .any(|arg| arg == "--cadder-shim-info")
  {
    println!(
      "{}",
      serde_json::json!({
          "role": "caddy-shim",
          "version": env!("CARGO_PKG_VERSION"),
          "executable": env::current_exe().ok().map(|path| path.display().to_string())
      })
    );
    return Ok(ExitCode::SUCCESS);
  }

  if args.caddy_args.first().is_some_and(|arg| arg == "run") {
    run_managed(args).await
  } else {
    delegate_to_real_caddy(args.real_caddy_command, &args.caddy_args).await
  }
}

async fn run_managed(args: ShimArgs) -> Result<ExitCode> {
  let paths = RuntimePaths::resolve(args.runtime_dir)?;
  ensure_daemon_running_with_options(
    &paths,
    DaemonLaunchOptions {
      explicit_daemon: args.daemon_path,
      real_caddy_command: args.real_caddy_command,
      shim_path: env::current_exe().ok(),
    },
  )
  .await?;
  let session = std::sync::Arc::new(Mutex::new(CadderSession::connect(&paths).await?));
  let registration = build_registration(&args.caddy_args)?;
  let registration_id = registration.registration_id.clone();
  let shim_session_nonce = registration.entrypoint_instance.shim_session_nonce.clone();

  let response: RegisterEntrypointResponse = session
    .lock()
    .await
    .request(
      message_types::REGISTER_ENTRYPOINT_REQUEST,
      message_types::REGISTER_ENTRYPOINT_RESPONSE,
      &RegisterEntrypointRequest {
        request_id: new_request_id("shim-register"),
        registration,
      },
    )
    .await?;
  if !response.accepted {
    return Err(anyhow!(response.message));
  }

  let heartbeat_session = session.clone();
  let heartbeat_registration = registration_id.clone();
  let heartbeat_nonce = shim_session_nonce.clone();
  let heartbeat = tokio::spawn(async move {
    let mut interval = interval(Duration::from_secs(5));
    loop {
      interval.tick().await;
      let _response: Result<BasicResponse> = heartbeat_session
        .lock()
        .await
        .request(
          message_types::HEARTBEAT_ENTRYPOINT_REQUEST,
          message_types::HEARTBEAT_ENTRYPOINT_RESPONSE,
          &HeartbeatEntrypointRequest {
            request_id: new_request_id("shim-heartbeat"),
            registration_id: heartbeat_registration.clone(),
            shim_session_nonce: heartbeat_nonce.clone(),
          },
        )
        .await;
    }
  });

  tokio::signal::ctrl_c()
    .await
    .context("wait for Ctrl+C while registered with Cadder")?;
  heartbeat.abort();

  let _response: BasicResponse = session
    .lock()
    .await
    .request(
      message_types::UNREGISTER_ENTRYPOINT_REQUEST,
      message_types::UNREGISTER_ENTRYPOINT_RESPONSE,
      &UnregisterEntrypointRequest {
        request_id: new_request_id("shim-unregister"),
        registration_id,
        shim_session_nonce,
      },
    )
    .await?;

  Ok(ExitCode::SUCCESS)
}

fn build_registration(args: &[String]) -> Result<EntrypointRegistration> {
  let now = Utc::now();
  let cwd = env::current_dir().context("resolve current directory")?;
  let (config_path, adapter) = parse_run_args(args, &cwd);
  let canonical_cwd = cwd.canonicalize().ok();
  let canonical_config = config_path.canonicalize().ok();
  let identity = EntrypointInstanceIdentity::new(now);
  let executable_path = env::current_exe().ok();

  Ok(EntrypointRegistration {
    registration_id: identity.instance_id.clone(),
    source_working_directory: SourcePath::new(
      cwd.display().to_string(),
      canonical_cwd.map(|path| path.display().to_string()),
    ),
    source_config_path: SourcePath::new(
      config_path.display().to_string(),
      canonical_config.map(|path| path.display().to_string()),
    ),
    registered_domains: Vec::new(),
    activation_state: ActivationState::Active,
    owner_process: OwnerProcessIdentity {
      process_id: std::process::id(),
      process_start_time_utc: now,
      shim_session_nonce: identity.shim_session_nonce.clone(),
      executable_path: executable_path.map(|path| path.display().to_string()),
    },
    log_stream: LogStreamIdentity::entrypoint(&identity.instance_id),
    shim_run: Some(ShimRunMetadata {
      adapter,
      raw_arguments: args.to_vec(),
      command_line: args.join(" "),
    }),
    created_at_utc: now,
    last_heartbeat_utc: now,
    entrypoint_instance: identity,
  })
}

fn parse_run_args(args: &[String], cwd: &std::path::Path) -> (PathBuf, Option<String>) {
  let mut config = None;
  let mut adapter = None;
  let mut iter = args.iter().skip(1);
  while let Some(arg) = iter.next() {
    match arg.as_str() {
      "--config" | "-c" => config = iter.next().map(PathBuf::from),
      "--adapter" | "-a" => adapter = iter.next().cloned(),
      _ => {}
    }
  }
  (config.unwrap_or_else(|| cwd.join("Caddyfile")), adapter)
}

async fn delegate_to_real_caddy(
  real_caddy_command: Option<String>,
  args: &[String],
) -> Result<ExitCode> {
  let resolver = RealCaddyResolver::new(real_caddy_command);
  let binary = match resolver.resolve() {
    Ok(binary) => binary,
    Err(error) => {
      eprintln!("{}", RealCaddyResolver::resolution_help(&error));
      return Ok(ExitCode::FAILURE);
    }
  };
  let status = Command::new(binary)
    .args(args)
    .stdin(Stdio::inherit())
    .stdout(Stdio::inherit())
    .stderr(Stdio::inherit())
    .status()
    .await
    .context("delegate command to real Caddy")?;
  Ok(ExitCode::from(status.code().unwrap_or(1) as u8))
}

#[cfg(test)]
mod tests {
  use super::*;

  #[test]
  fn parses_config_and_adapter_flags() {
    let cwd = PathBuf::from("/project");
    let args = vec![
      "run".to_string(),
      "--config".to_string(),
      "Proxy.Caddyfile".to_string(),
      "--adapter".to_string(),
      "caddyfile".to_string(),
    ];

    let (config, adapter) = parse_run_args(&args, &cwd);

    assert_eq!(config, PathBuf::from("Proxy.Caddyfile"));
    assert_eq!(adapter.as_deref(), Some("caddyfile"));
  }
}
