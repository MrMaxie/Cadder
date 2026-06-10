use crate::{logs::CaddyLogStore, runtime::ProcessRuntime};
use anyhow::{Context, Result, anyhow};
use cadder_protocol::{
  ConfigApplyStatus, ConfigDiagnostic, ConfigState, EntrypointRegistration, LogAttributionKind,
  LogSeverity, RegisteredDomain,
};
use chrono::Utc;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::{
  collections::{BTreeMap, BTreeSet},
  env,
  path::{Path, PathBuf},
  process::Stdio,
};
use tokio::process::Command;

#[derive(Debug, Clone)]
pub struct RealCaddyResolver {
  configured_command: Option<String>,
}

impl RealCaddyResolver {
  pub fn new(configured_command: Option<String>) -> Self {
    Self { configured_command }
  }

  pub fn resolve(&self) -> Result<PathBuf> {
    if let Some(command) = self
      .configured_command
      .clone()
      .or_else(|| env::var("CADDER_CADDY_REAL_COMMAND").ok())
    {
      return resolve_command(&command, &shim_exclusions())
        .with_context(|| format!("resolve configured real Caddy command `{command}`"));
    }

    resolve_command("caddy-real", &shim_exclusions())
      .or_else(|_| resolve_command("caddy", &shim_exclusions()))
      .context("could not resolve a real Caddy binary")
  }
}

fn shim_exclusions() -> BTreeSet<PathBuf> {
  let mut excluded = BTreeSet::new();
  if let Ok(path) = env::current_exe().and_then(|path| path.canonicalize()) {
    excluded.insert(path);
  }
  if let Some(path) = env::var_os("CADDER_CADDY_SHIM_PATH")
    && let Ok(path) = PathBuf::from(path).canonicalize()
  {
    excluded.insert(path);
  }
  excluded
}

fn resolve_command(command: &str, excluded: &BTreeSet<PathBuf>) -> Result<PathBuf> {
  let path = PathBuf::from(command);
  if path.components().count() > 1 || path.is_absolute() {
    let canonical = path
      .canonicalize()
      .with_context(|| format!("canonicalize {}", path.display()))?;
    if excluded.contains(&canonical) {
      return Err(anyhow!("resolved command points at the Cadder shim"));
    }
    return Ok(canonical);
  }

  let path_var = env::var_os("PATH").ok_or_else(|| anyhow!("PATH is not set"))?;
  for dir in env::split_paths(&path_var) {
    for candidate in executable_candidates(&dir, command) {
      if candidate.is_file() {
        let canonical = candidate.canonicalize().unwrap_or(candidate);
        if !excluded.contains(&canonical) {
          return Ok(canonical);
        }
      }
    }
  }
  Err(anyhow!("command `{command}` not found on PATH"))
}

fn executable_candidates(dir: &Path, command: &str) -> Vec<PathBuf> {
  #[cfg(windows)]
  {
    let pathext = env::var("PATHEXT").unwrap_or_else(|_| ".COM;.EXE;.BAT;.CMD".to_string());
    let mut candidates = vec![dir.join(command)];
    for ext in pathext.split(';').filter(|ext| !ext.is_empty()) {
      candidates.push(dir.join(format!("{command}{ext}")));
    }
    candidates
  }

  #[cfg(not(windows))]
  {
    vec![dir.join(command)]
  }
}

#[derive(Debug, Clone)]
pub struct CaddyConfigAdapter {
  resolver: RealCaddyResolver,
}

#[derive(Debug, Clone)]
pub struct PreparedRegistration {
  pub registration: EntrypointRegistration,
  pub routes: Vec<Value>,
  pub diagnostics: Vec<ConfigDiagnostic>,
}

impl CaddyConfigAdapter {
  pub fn new(resolver: RealCaddyResolver) -> Self {
    Self { resolver }
  }

  pub async fn prepare(&self, registration: EntrypointRegistration) -> PreparedRegistration {
    match self.adapt(&registration).await {
      Ok(adapted) => {
        let hosts = extract_hosts(&adapted);
        let mut prepared = registration;
        if !hosts.is_empty() {
          prepared.registered_domains = hosts.into_iter().map(RegisteredDomain::active).collect();
        }
        let routes = extract_http_routes(&adapted);
        PreparedRegistration {
          registration: prepared,
          routes,
          diagnostics: Vec::new(),
        }
      }
      Err(error) => PreparedRegistration {
        registration,
        routes: Vec::new(),
        diagnostics: vec![ConfigDiagnostic {
          code: "adapt-failed".to_string(),
          message: error.to_string(),
          domain_key: None,
          source_config_paths: Vec::new(),
        }],
      },
    }
  }

  async fn adapt(&self, registration: &EntrypointRegistration) -> Result<Value> {
    let binary = self.resolver.resolve()?;
    let config_path = registration
      .source_config_path
      .canonical
      .as_deref()
      .unwrap_or(&registration.source_config_path.raw);
    let adapter = registration
      .shim_run
      .as_ref()
      .and_then(|run| run.adapter.as_deref())
      .unwrap_or("caddyfile");

    let output = Command::new(binary)
      .arg("adapt")
      .arg("--config")
      .arg(config_path)
      .arg("--adapter")
      .arg(adapter)
      .stdout(Stdio::piped())
      .stderr(Stdio::piped())
      .output()
      .await
      .context("run caddy adapt")?;

    if !output.status.success() {
      return Err(anyhow!(
        "caddy adapt failed: {}",
        String::from_utf8_lossy(&output.stderr)
      ));
    }

    serde_json::from_slice(&output.stdout).context("parse adapted Caddy JSON")
  }
}

#[derive(Debug)]
pub struct CaddyConfigCoordinator {
  adapter: CaddyConfigAdapter,
  runtime: ProcessRuntime,
  routes: BTreeMap<String, Vec<Value>>,
  current: ConfigState,
}

impl CaddyConfigCoordinator {
  pub fn new(adapter: CaddyConfigAdapter, runtime: ProcessRuntime) -> Self {
    Self {
      adapter,
      runtime,
      routes: BTreeMap::new(),
      current: ConfigState::idle(),
    }
  }

  pub fn current_state(&self) -> ConfigState {
    self.current.clone()
  }

  pub async fn runtime_state(&self) -> cadder_protocol::RuntimeState {
    self.runtime.inspect().await
  }

  pub async fn prepare_registration(
    &mut self,
    registration: EntrypointRegistration,
  ) -> EntrypointRegistration {
    let source_path = registration.source_config_path.raw.clone();
    let prepared = self.adapter.prepare(registration).await;
    if prepared.diagnostics.is_empty() {
      self.routes.insert(
        prepared.registration.registration_id.clone(),
        prepared.routes,
      );
    } else {
      self.current = ConfigState {
        status: ConfigApplyStatus::Failed,
        last_attempted_at_utc: Some(Utc::now()),
        last_successful_reload_at_utc: self.current.last_successful_reload_at_utc,
        effective_config_hash: self.current.effective_config_hash.clone(),
        diagnostics: prepared
          .diagnostics
          .into_iter()
          .map(|mut diagnostic| {
            if diagnostic.source_config_paths.is_empty() {
              diagnostic.source_config_paths = vec![source_path.clone()];
            }
            diagnostic
          })
          .collect(),
      };
    }
    prepared.registration
  }

  pub async fn apply(
    &mut self,
    registrations: &[EntrypointRegistration],
    logs: &CaddyLogStore,
  ) -> ConfigState {
    let diagnostics = detect_conflicts(registrations);
    let attempted = Utc::now();
    if !diagnostics.is_empty() {
      self.current = ConfigState {
        status: ConfigApplyStatus::Failed,
        last_attempted_at_utc: Some(attempted),
        last_successful_reload_at_utc: self.current.last_successful_reload_at_utc,
        effective_config_hash: self.current.effective_config_hash.clone(),
        diagnostics,
      };
      return self.current.clone();
    }

    let active: Vec<_> = registrations
      .iter()
      .filter(|registration| registration.activation_state.is_enabled())
      .cloned()
      .collect();
    if active
      .iter()
      .all(|registration| active_domains(registration).is_empty())
    {
      if let Err(error) = self.runtime.stop().await {
        logs.append(
          cadder_protocol::LogStreamIdentity::runtime_control(),
          LogSeverity::Error,
          error.to_string(),
          LogAttributionKind::RuntimeControl,
          Some("idle-stop".to_string()),
        );
      }
      self.current = ConfigState {
        status: ConfigApplyStatus::Idle,
        last_attempted_at_utc: Some(attempted),
        last_successful_reload_at_utc: self.current.last_successful_reload_at_utc,
        effective_config_hash: None,
        diagnostics: Vec::new(),
      };
      return self.current.clone();
    }

    let config = compose_config(&active, &self.routes);
    let rendered = serde_json::to_vec_pretty(&config).expect("config serialization");
    let hash = hex::encode(Sha256::digest(&rendered));

    match self.runtime.apply_config(&rendered, logs).await {
      Ok(()) => {
        self.current = ConfigState {
          status: ConfigApplyStatus::Applied,
          last_attempted_at_utc: Some(attempted),
          last_successful_reload_at_utc: Some(Utc::now()),
          effective_config_hash: Some(hash),
          diagnostics: Vec::new(),
        };
      }
      Err(error) => {
        self.current = ConfigState {
          status: ConfigApplyStatus::Failed,
          last_attempted_at_utc: Some(attempted),
          last_successful_reload_at_utc: self.current.last_successful_reload_at_utc,
          effective_config_hash: self.current.effective_config_hash.clone(),
          diagnostics: vec![ConfigDiagnostic {
            code: "runtime-apply-failed".to_string(),
            message: error.to_string(),
            domain_key: None,
            source_config_paths: active
              .iter()
              .map(|registration| registration.source_config_path.raw.clone())
              .collect(),
          }],
        };
      }
    }
    self.current.clone()
  }

  pub async fn shutdown(&mut self) -> Result<()> {
    self.runtime.stop().await
  }
}

fn compose_config(
  registrations: &[EntrypointRegistration],
  routes_by_registration: &BTreeMap<String, Vec<Value>>,
) -> Value {
  let mut routes = Vec::new();
  for registration in registrations {
    let enabled_hosts = active_domains(registration);
    if enabled_hosts.is_empty() {
      continue;
    }

    if let Some(source_routes) = routes_by_registration.get(&registration.registration_id) {
      for route in source_routes {
        if let Some(filtered) = filter_route_hosts(route.clone(), &enabled_hosts) {
          routes.push(filtered);
        }
      }
    } else {
      for host in enabled_hosts {
        routes.push(json!({
            "match": [{ "host": [host] }],
            "handle": [{ "handler": "static_response", "body": "Cadder route placeholder" }],
            "terminal": true
        }));
      }
    }
  }

  json!({
      "admin": { "listen": "localhost:2019" },
      "apps": {
          "http": {
              "servers": {
                  "cadder": {
                      "listen": [":80", ":443"],
                      "routes": routes
                  }
              }
          }
      }
  })
}

fn active_domains(registration: &EntrypointRegistration) -> BTreeSet<String> {
  registration
    .registered_domains
    .iter()
    .filter(|domain| domain.activation_state.is_enabled())
    .map(|domain| domain.name.canonical.clone())
    .collect()
}

fn filter_route_hosts(mut route: Value, enabled_hosts: &BTreeSet<String>) -> Option<Value> {
  let mut retained_any = false;
  filter_hosts_recursive(&mut route, enabled_hosts, &mut retained_any);
  retained_any.then_some(route)
}

fn filter_hosts_recursive(
  value: &mut Value,
  enabled_hosts: &BTreeSet<String>,
  retained_any: &mut bool,
) {
  match value {
    Value::Object(map) => {
      if let Some(Value::Array(hosts)) = map.get_mut("host") {
        hosts.retain(|host| {
          let keep = host
            .as_str()
            .map(cadder_protocol::canonicalize_domain)
            .is_some_and(|host| enabled_hosts.contains(&host));
          if keep {
            *retained_any = true;
          }
          keep
        });
      }
      for child in map.values_mut() {
        filter_hosts_recursive(child, enabled_hosts, retained_any);
      }
    }
    Value::Array(items) => {
      for item in items {
        filter_hosts_recursive(item, enabled_hosts, retained_any);
      }
    }
    _ => {}
  }
}

fn extract_hosts(value: &Value) -> BTreeSet<String> {
  let mut hosts = BTreeSet::new();
  collect_hosts(value, &mut hosts);
  hosts
}

fn collect_hosts(value: &Value, hosts: &mut BTreeSet<String>) {
  match value {
    Value::Object(map) => {
      if let Some(Value::Array(values)) = map.get("host") {
        for value in values {
          if let Some(host) = value.as_str() {
            hosts.insert(cadder_protocol::canonicalize_domain(host));
          }
        }
      }
      for child in map.values() {
        collect_hosts(child, hosts);
      }
    }
    Value::Array(items) => {
      for item in items {
        collect_hosts(item, hosts);
      }
    }
    _ => {}
  }
}

fn extract_http_routes(value: &Value) -> Vec<Value> {
  value
    .pointer("/apps/http/servers")
    .and_then(Value::as_object)
    .map(|servers| {
      servers
        .values()
        .filter_map(|server| server.get("routes"))
        .filter_map(Value::as_array)
        .flat_map(|routes| routes.iter().cloned())
        .collect()
    })
    .unwrap_or_default()
}

fn detect_conflicts(registrations: &[EntrypointRegistration]) -> Vec<ConfigDiagnostic> {
  let mut owners: BTreeMap<String, Vec<&EntrypointRegistration>> = BTreeMap::new();
  for registration in registrations
    .iter()
    .filter(|registration| registration.activation_state.is_enabled())
  {
    for domain in registration
      .registered_domains
      .iter()
      .filter(|domain| domain.activation_state.is_enabled())
    {
      owners
        .entry(domain.name.canonical.clone())
        .or_default()
        .push(registration);
    }
  }

  owners
    .into_iter()
    .filter_map(|(domain, registrations)| {
      (registrations.len() > 1).then(|| ConfigDiagnostic {
        code: "domain-conflict".to_string(),
        message: format!("domain `{domain}` is registered by multiple entrypoints"),
        domain_key: Some(domain),
        source_config_paths: registrations
          .into_iter()
          .map(|registration| registration.source_config_path.raw.clone())
          .collect(),
      })
    })
    .collect()
}

#[cfg(test)]
mod tests {
  use super::*;
  use cadder_protocol::{
    ActivationState, EntrypointInstanceIdentity, LogStreamIdentity, OwnerProcessIdentity,
    SourcePath,
  };
  use chrono::Utc;

  fn registration(id: &str, hosts: &[&str]) -> EntrypointRegistration {
    let now = Utc::now();
    let identity = EntrypointInstanceIdentity {
      instance_id: id.to_string(),
      started_at_utc: now,
      shim_session_nonce: format!("{id}-nonce"),
    };
    EntrypointRegistration {
      registration_id: id.to_string(),
      entrypoint_instance: identity.clone(),
      source_working_directory: SourcePath::new(".", None),
      source_config_path: SourcePath::new(format!("{id}.Caddyfile"), None),
      registered_domains: hosts
        .iter()
        .map(|host| RegisteredDomain::active(*host))
        .collect(),
      activation_state: ActivationState::Active,
      owner_process: OwnerProcessIdentity {
        process_id: 1,
        process_start_time_utc: now,
        shim_session_nonce: identity.shim_session_nonce,
        executable_path: None,
      },
      log_stream: LogStreamIdentity::entrypoint(id),
      shim_run: None,
      created_at_utc: now,
      last_heartbeat_utc: now,
    }
  }

  #[test]
  fn extracts_hosts_from_adapted_json() {
    let adapted = json!({
        "apps": {
            "http": {
                "servers": {
                    "srv0": {
                        "routes": [
                            { "match": [{ "host": ["App.Localhost", "api.localhost"] }] }
                        ]
                    }
                }
            }
        }
    });

    let hosts = extract_hosts(&adapted);
    assert!(hosts.contains("app.localhost"));
    assert!(hosts.contains("api.localhost"));
  }

  #[test]
  fn detects_active_domain_conflicts() {
    let left = registration("left", &["app.localhost"]);
    let right = registration("right", &["APP.localhost."]);

    let diagnostics = detect_conflicts(&[left, right]);

    assert_eq!(diagnostics.len(), 1);
    assert_eq!(diagnostics[0].domain_key.as_deref(), Some("app.localhost"));
  }

  #[test]
  fn filters_routes_to_enabled_hosts() {
    let route = json!({
        "match": [{ "host": ["app.localhost", "api.localhost"] }],
        "handle": [{ "handler": "reverse_proxy" }]
    });
    let hosts = BTreeSet::from(["api.localhost".to_string()]);

    let filtered = filter_route_hosts(route, &hosts).unwrap();

    assert_eq!(
      filtered.pointer("/match/0/host/0").and_then(Value::as_str),
      Some("api.localhost")
    );
    assert_eq!(
      filtered
        .pointer("/match/0/host")
        .and_then(Value::as_array)
        .unwrap()
        .len(),
      1
    );
  }
}
