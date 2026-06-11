use anyhow::{Context, Result, anyhow};
use cadder_protocol::{
  IisBinding, IisBindingIdentity, IisHandoffState, IisIssue, IisIssueKind,
  IisRestoreMetadataSummary, canonicalize_domain,
};
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, path::PathBuf, sync::Arc};
use tokio::sync::Mutex;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct IisBindingRecord {
  pub site_name: String,
  pub protocol: String,
  pub binding_information: String,
  pub ip_address: String,
  pub port: u16,
  pub host_header: String,
  #[serde(default)]
  pub tls_certificate: Option<IisTlsCertificate>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct IisTlsCertificate {
  pub thumbprint: String,
  pub store_name: String,
  #[serde(default)]
  pub ssl_flags: Option<i32>,
}

impl IisBindingRecord {
  pub fn from_binding_information(
    site_name: impl Into<String>,
    protocol: impl Into<String>,
    binding_information: impl Into<String>,
  ) -> Result<Self> {
    let site_name = site_name.into();
    let protocol = protocol.into();
    let binding_information = binding_information.into();
    let mut parts = binding_information.rsplitn(3, ':');
    let host_header = parts
      .next()
      .ok_or_else(|| anyhow!("IIS binding `{binding_information}` is missing a host segment"))?;
    let port = parts
      .next()
      .ok_or_else(|| anyhow!("IIS binding `{binding_information}` is missing a port segment"))?
      .parse::<u16>()
      .with_context(|| format!("parse IIS binding port in `{binding_information}`"))?;
    let ip_address = parts
      .next()
      .ok_or_else(|| anyhow!("IIS binding `{binding_information}` is missing an IP segment"))?;
    let ip_address = ip_address.to_string();
    let host_header = host_header.to_string();

    Ok(Self {
      site_name,
      protocol,
      binding_information,
      ip_address,
      port,
      host_header,
      tls_certificate: None,
    })
  }

  pub fn binding_id(&self) -> String {
    format!(
      "{}|{}|{}",
      self.site_name, self.protocol, self.binding_information
    )
  }

  pub fn identity(&self) -> IisBindingIdentity {
    IisBindingIdentity {
      binding_id: self.binding_id(),
      site_name: self.site_name.clone(),
      protocol: self.protocol.clone(),
      binding_information: self.binding_information.clone(),
    }
  }

  pub fn restore_summary(&self) -> IisRestoreMetadataSummary {
    IisRestoreMetadataSummary {
      site_name: self.site_name.clone(),
      protocol: self.protocol.clone(),
      ip_address: self.ip_address.clone(),
      port: self.port,
      host_header: self.host_header.clone(),
      binding_information: self.binding_information.clone(),
    }
  }

  pub fn backend_http_binding(&self, port: u16, route_host: &str) -> Self {
    let ip_address = "127.0.0.1".to_string();
    let protocol = "http".to_string();
    let original_host = self.host_header.trim();
    let host_header = if original_host.is_empty() || original_host == "*" {
      canonicalize_domain(route_host)
    } else {
      self.host_header.clone()
    };
    let binding_information = format!("{ip_address}:{port}:{host_header}");
    Self {
      site_name: self.site_name.clone(),
      protocol,
      binding_information,
      ip_address,
      port,
      host_header,
      tls_certificate: None,
    }
  }

  #[cfg(windows)]
  fn with_tls_certificate(mut self, tls_certificate: Option<IisTlsCertificate>) -> Self {
    self.tls_certificate = tls_certificate;
    self
  }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct IisRestoreRecord {
  pub binding: IisBindingRecord,
  pub domain_key: String,
  #[serde(default)]
  pub registration_id: Option<String>,
  #[serde(default)]
  pub backend_binding: Option<IisBindingRecord>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct IisMetadata {
  pub handoffs: BTreeMap<String, IisRestoreRecord>,
}

#[derive(Debug, Clone)]
pub struct IisMetadataStore {
  path: Option<PathBuf>,
  metadata: Arc<Mutex<IisMetadata>>,
}

impl IisMetadataStore {
  pub fn memory() -> Self {
    Self {
      path: None,
      metadata: Arc::new(Mutex::new(IisMetadata::default())),
    }
  }

  pub async fn load(path: PathBuf) -> Result<Self> {
    let metadata = if path.is_file() {
      let raw = tokio::fs::read_to_string(&path)
        .await
        .with_context(|| format!("read daemon metadata {}", path.display()))?;
      serde_json::from_str(&raw)
        .with_context(|| format!("parse daemon metadata {}", path.display()))?
    } else {
      IisMetadata::default()
    };
    Ok(Self {
      path: Some(path),
      metadata: Arc::new(Mutex::new(metadata)),
    })
  }

  pub async fn snapshot(&self) -> BTreeMap<String, IisRestoreRecord> {
    self.metadata.lock().await.handoffs.clone()
  }

  pub async fn insert(&self, binding_id: String, record: IisRestoreRecord) -> Result<()> {
    let mut metadata = self.metadata.lock().await;
    metadata.handoffs.insert(binding_id, record);
    self.save_locked(&metadata).await
  }

  pub async fn remove(&self, binding_id: &str) -> Result<Option<IisRestoreRecord>> {
    let mut metadata = self.metadata.lock().await;
    let removed = metadata.handoffs.remove(binding_id);
    self.save_locked(&metadata).await?;
    Ok(removed)
  }

  async fn save_locked(&self, metadata: &IisMetadata) -> Result<()> {
    let Some(path) = &self.path else {
      return Ok(());
    };
    if let Some(parent) = path.parent() {
      tokio::fs::create_dir_all(parent)
        .await
        .with_context(|| format!("create metadata directory {}", parent.display()))?;
    }
    let rendered = serde_json::to_string_pretty(metadata)?;
    tokio::fs::write(path, rendered)
      .await
      .with_context(|| format!("write daemon metadata {}", path.display()))
  }
}

#[derive(Debug, Clone)]
pub struct IisProvider {
  inner: IisProviderInner,
}

#[derive(Debug, Clone)]
enum IisProviderInner {
  System,
  #[cfg(test)]
  Fake(Arc<Mutex<FakeIisProviderState>>),
}

impl Default for IisProvider {
  fn default() -> Self {
    Self::system()
  }
}

impl IisProvider {
  pub fn system() -> Self {
    Self {
      inner: IisProviderInner::System,
    }
  }

  #[cfg(test)]
  pub fn fake(bindings: Vec<IisBindingRecord>) -> Self {
    Self {
      inner: IisProviderInner::Fake(Arc::new(Mutex::new(FakeIisProviderState {
        bindings,
        fail_discovery: None,
        fail_remove: None,
        fail_restore: None,
      }))),
    }
  }

  pub async fn discover(&self) -> Result<Vec<IisBindingRecord>, IisIssue> {
    match &self.inner {
      IisProviderInner::System => system_discover().await,
      #[cfg(test)]
      IisProviderInner::Fake(state) => state.lock().await.discover(),
    }
  }

  pub async fn remove_binding(&self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    match &self.inner {
      IisProviderInner::System => system_remove_binding(binding).await,
      #[cfg(test)]
      IisProviderInner::Fake(state) => state.lock().await.remove_binding(binding),
    }
  }

  pub async fn add_binding(&self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    match &self.inner {
      IisProviderInner::System => system_add_binding(binding).await,
      #[cfg(test)]
      IisProviderInner::Fake(state) => state.lock().await.add_binding(binding),
    }
  }

  pub async fn restore_binding(&self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    match &self.inner {
      IisProviderInner::System => system_restore_binding(binding).await,
      #[cfg(test)]
      IisProviderInner::Fake(state) => state.lock().await.restore_binding(binding),
    }
  }

  #[cfg(test)]
  pub async fn set_fail_remove(&self, issue: IisIssue) {
    if let IisProviderInner::Fake(state) = &self.inner {
      state.lock().await.fail_remove = Some(issue);
    }
  }

  #[cfg(test)]
  pub async fn set_fail_restore(&self, issue: IisIssue) {
    if let IisProviderInner::Fake(state) = &self.inner {
      state.lock().await.fail_restore = Some(issue);
    }
  }
}

#[cfg(test)]
#[derive(Debug)]
struct FakeIisProviderState {
  bindings: Vec<IisBindingRecord>,
  fail_discovery: Option<IisIssue>,
  fail_remove: Option<IisIssue>,
  fail_restore: Option<IisIssue>,
}

#[cfg(test)]
impl FakeIisProviderState {
  fn discover(&self) -> Result<Vec<IisBindingRecord>, IisIssue> {
    if let Some(issue) = &self.fail_discovery {
      return Err(issue.clone());
    }
    Ok(self.bindings.clone())
  }

  fn remove_binding(&mut self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    if let Some(issue) = &self.fail_remove {
      return Err(issue.clone());
    }
    let before = self.bindings.len();
    self
      .bindings
      .retain(|candidate| candidate.binding_id() != binding.binding_id());
    if self.bindings.len() == before {
      return Err(IisIssue::new(
        IisIssueKind::MissingBinding,
        "IIS binding was not found.",
      ));
    }
    Ok(())
  }

  fn restore_binding(&mut self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    if let Some(issue) = &self.fail_restore {
      return Err(issue.clone());
    }
    self.add_binding(binding)
  }

  fn add_binding(&mut self, binding: &IisBindingRecord) -> Result<(), IisIssue> {
    if !self
      .bindings
      .iter()
      .any(|candidate| candidate.binding_id() == binding.binding_id())
    {
      self.bindings.push(binding.clone());
    }
    Ok(())
  }
}

#[cfg(windows)]
async fn system_discover() -> Result<Vec<IisBindingRecord>, IisIssue> {
  use tokio::process::Command;

  let script = r#"
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration -ErrorAction Stop
Get-WebBinding | ForEach-Object {
  $certificateHash = $null
  if ($_.certificateHash) {
    $certificateHash = ($_.certificateHash | ForEach-Object { $_.ToString('x2') }) -join ''
  }
  [PSCustomObject]@{
    siteName = ($_.ItemXPath -replace "^.*name='([^']+)'.*$", '$1')
    protocol = $_.protocol
    bindingInformation = $_.bindingInformation
    certificateHash = $certificateHash
    certificateStoreName = $_.certificateStoreName
    sslFlags = $_.sslFlags
  }
} | ConvertTo-Json -Depth 4
"#;
  let output = Command::new("powershell")
    .arg("-NoProfile")
    .arg("-NonInteractive")
    .arg("-Command")
    .arg(script)
    .output()
    .await
    .map_err(provider_error)?;
  if !output.status.success() {
    return Err(classify_powershell_error(&output.stderr));
  }
  parse_powershell_bindings(&output.stdout)
}

#[cfg(not(windows))]
async fn system_discover() -> Result<Vec<IisBindingRecord>, IisIssue> {
  Err(IisIssue::new(
    IisIssueKind::IisUnavailable,
    "IIS handoff is only available on Windows.",
  ))
}

#[cfg(windows)]
async fn system_remove_binding(binding: &IisBindingRecord) -> Result<(), IisIssue> {
  let script = format!(
    "$ErrorActionPreference = 'Stop'; Import-Module WebAdministration -ErrorAction Stop; Remove-WebBinding -Name '{}' -Protocol '{}' -BindingInformation '{}'",
    ps_escape(&binding.site_name),
    ps_escape(&binding.protocol),
    ps_escape(&binding.binding_information)
  );
  run_powershell_mutation(script).await
}

#[cfg(not(windows))]
async fn system_remove_binding(_binding: &IisBindingRecord) -> Result<(), IisIssue> {
  Err(IisIssue::new(
    IisIssueKind::IisUnavailable,
    "IIS handoff is only available on Windows.",
  ))
}

#[cfg(windows)]
async fn system_add_binding(binding: &IisBindingRecord) -> Result<(), IisIssue> {
  let ssl_flags = binding
    .tls_certificate
    .as_ref()
    .and_then(|certificate| certificate.ssl_flags)
    .unwrap_or(0);
  let ssl_flags_argument = if binding.protocol.eq_ignore_ascii_case("https") {
    format!(" -SslFlags {ssl_flags}")
  } else {
    String::new()
  };
  let certificate_script = binding
    .tls_certificate
    .as_ref()
    .filter(|_| binding.protocol.eq_ignore_ascii_case("https"))
    .map(|certificate| {
      format!(
        r#"
$binding = Get-WebBinding -Name '{site_name}' -Protocol '{protocol}' | Where-Object {{ $_.bindingInformation -eq '{binding_information}' }} | Select-Object -First 1
if ($null -eq $binding) {{
  throw "IIS binding '{binding_information}' was created but could not be found for certificate restore."
}}
$binding.AddSslCertificate('{thumbprint}', '{store_name}')
"#,
        site_name = ps_escape(&binding.site_name),
        protocol = ps_escape(&binding.protocol),
        binding_information = ps_escape(&binding.binding_information),
        thumbprint = ps_escape(&certificate.thumbprint),
        store_name = ps_escape(&certificate.store_name),
      )
    })
    .unwrap_or_default();
  let script = format!(
    r#"
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration -ErrorAction Stop
$created = $false
try {{
  New-WebBinding -Name '{site_name}' -Protocol '{protocol}' -IPAddress '{ip_address}' -Port {port} -HostHeader '{host_header}'{ssl_flags_argument}
  $created = $true
  {certificate_script}
  $site = Get-Website -Name '{site_name}'
  if ($site.State -ne 'Started') {{
    Start-WebSite -Name '{site_name}'
  }}
}} catch {{
  if ($created) {{
    Remove-WebBinding -Name '{site_name}' -Protocol '{protocol}' -BindingInformation '{binding_information}' -ErrorAction SilentlyContinue
  }}
  throw
}}
"#,
    site_name = ps_escape(&binding.site_name),
    protocol = ps_escape(&binding.protocol),
    ip_address = ps_escape(&binding.ip_address),
    port = binding.port,
    host_header = ps_escape(&binding.host_header),
    ssl_flags_argument = ssl_flags_argument,
    certificate_script = certificate_script,
    binding_information = ps_escape(&binding.binding_information),
  );
  run_powershell_mutation(script).await
}

#[cfg(not(windows))]
async fn system_add_binding(_binding: &IisBindingRecord) -> Result<(), IisIssue> {
  Err(IisIssue::new(
    IisIssueKind::IisUnavailable,
    "IIS handoff is only available on Windows.",
  ))
}

#[cfg(windows)]
async fn system_restore_binding(binding: &IisBindingRecord) -> Result<(), IisIssue> {
  system_add_binding(binding).await
}

#[cfg(not(windows))]
async fn system_restore_binding(_binding: &IisBindingRecord) -> Result<(), IisIssue> {
  Err(IisIssue::new(
    IisIssueKind::IisUnavailable,
    "IIS handoff is only available on Windows.",
  ))
}

#[cfg(windows)]
async fn run_powershell_mutation(script: String) -> Result<(), IisIssue> {
  use tokio::process::Command;

  let output = Command::new("powershell")
    .arg("-NoProfile")
    .arg("-NonInteractive")
    .arg("-Command")
    .arg(script)
    .output()
    .await
    .map_err(provider_error)?;
  if output.status.success() {
    Ok(())
  } else {
    Err(classify_powershell_error(&output.stderr))
  }
}

#[cfg(windows)]
fn ps_escape(value: &str) -> String {
  value.replace('\'', "''")
}

#[cfg(windows)]
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawPowerShellBinding {
  site_name: String,
  protocol: String,
  binding_information: String,
  #[serde(default)]
  certificate_hash: Option<String>,
  #[serde(default)]
  certificate_store_name: Option<String>,
  #[serde(default)]
  ssl_flags: Option<i32>,
}

#[cfg(windows)]
fn parse_powershell_bindings(raw: &[u8]) -> Result<Vec<IisBindingRecord>, IisIssue> {
  let text = String::from_utf8_lossy(raw).trim().to_string();
  if text.is_empty() {
    return Ok(Vec::new());
  }
  let values = if text.starts_with('[') {
    serde_json::from_str::<Vec<RawPowerShellBinding>>(&text)
  } else {
    serde_json::from_str::<RawPowerShellBinding>(&text).map(|binding| vec![binding])
  }
  .map_err(|error| {
    IisIssue::new(
      IisIssueKind::ProviderError,
      format!("Could not parse IIS binding output: {error}"),
    )
  })?;

  values
    .into_iter()
    .map(|binding| {
      let tls_certificate = binding
        .certificate_hash
        .filter(|thumbprint| !thumbprint.trim().is_empty())
        .map(|thumbprint| IisTlsCertificate {
          thumbprint,
          store_name: binding
            .certificate_store_name
            .filter(|store| !store.trim().is_empty())
            .unwrap_or_else(|| "My".to_string()),
          ssl_flags: binding.ssl_flags,
        });
      Ok(
        IisBindingRecord::from_binding_information(
          binding.site_name,
          binding.protocol,
          binding.binding_information,
        )
        .map_err(|error| IisIssue::new(IisIssueKind::UnsupportedBindingShape, error.to_string()))?
        .with_tls_certificate(tls_certificate),
      )
    })
    .collect()
}

#[cfg(windows)]
fn provider_error(error: std::io::Error) -> IisIssue {
  IisIssue::new(
    IisIssueKind::IisUnavailable,
    format!("Could not execute PowerShell WebAdministration command: {error}"),
  )
}

#[cfg(windows)]
fn classify_powershell_error(stderr: &[u8]) -> IisIssue {
  let message = String::from_utf8_lossy(stderr).trim().to_string();
  let kind = if message.contains("Access is denied")
    || message.contains("UnauthorizedAccess")
    || message.contains("administrator")
  {
    IisIssueKind::InsufficientPrivileges
  } else if message.contains("WebAdministration") || message.contains("module") {
    IisIssueKind::IisUnavailable
  } else {
    IisIssueKind::ProviderError
  };
  IisIssue::new(kind, message)
}

pub fn binding_to_view(
  binding: &IisBindingRecord,
  state: IisHandoffState,
  issue: Option<IisIssue>,
  restore_metadata: Option<IisRestoreMetadataSummary>,
) -> IisBinding {
  let host = binding.host_header.trim();
  IisBinding {
    identity: binding.identity(),
    ip_address: binding.ip_address.clone(),
    port: binding.port,
    host_header: binding.host_header.clone(),
    domain_key: (!host.is_empty() && host != "*").then(|| canonicalize_domain(host)),
    handoff_state: state,
    issue,
    restore_metadata,
  }
}

pub fn unsupported_binding_issue(binding: &IisBindingRecord) -> Option<IisIssue> {
  let protocol = binding.protocol.to_ascii_lowercase();
  if protocol != "http" && protocol != "https" {
    return Some(IisIssue::new(
      IisIssueKind::UnsupportedBindingShape,
      format!(
        "IIS protocol `{}` is not supported for handoff.",
        binding.protocol
      ),
    ));
  }
  if protocol == "http" && binding.port != 80 {
    return Some(IisIssue::new(
      IisIssueKind::UnsupportedBindingShape,
      format!(
        "IIS port {} is not supported for HTTP handoff.",
        binding.port
      ),
    ));
  }
  if protocol == "https" && binding.port != 443 {
    return Some(IisIssue::new(
      IisIssueKind::UnsupportedBindingShape,
      format!(
        "IIS port {} is not supported for HTTPS handoff.",
        binding.port
      ),
    ));
  }
  None
}

#[cfg(test)]
mod tests {
  use super::*;

  #[test]
  fn parses_iis_binding_information_from_right() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:App.Localhost")
        .unwrap();

    assert_eq!(binding.ip_address, "*");
    assert_eq!(binding.port, 80);
    assert_eq!(binding.host_header, "App.Localhost");
    assert_eq!(
      binding.binding_id(),
      "Default Web Site|http|*:80:App.Localhost"
    );
  }

  #[test]
  fn rejects_unsupported_binding_shapes() {
    let https = IisBindingRecord::from_binding_information(
      "Default Web Site",
      "https",
      "*:443:app.localhost",
    )
    .unwrap();
    let ftp =
      IisBindingRecord::from_binding_information("Default Web Site", "ftp", "*:21:app.localhost")
        .unwrap();
    let high_port = IisBindingRecord::from_binding_information(
      "Default Web Site",
      "http",
      "*:8080:app.localhost",
    )
    .unwrap();

    assert!(unsupported_binding_issue(&https).is_none());
    assert!(
      unsupported_binding_issue(&ftp)
        .unwrap()
        .message
        .contains("protocol")
    );
    assert!(
      unsupported_binding_issue(&high_port)
        .unwrap()
        .message
        .contains("port 8080")
    );
  }

  #[test]
  fn rejects_invalid_binding_information() {
    assert!(
      IisBindingRecord::from_binding_information("Default Web Site", "http", "missing-port")
        .unwrap_err()
        .to_string()
        .contains("port segment")
    );
    assert!(
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:not-a-port:host")
        .unwrap_err()
        .to_string()
        .contains("parse IIS binding port")
    );
  }

  #[test]
  fn binding_to_view_sets_domain_key_and_restore_metadata() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:App.Localhost")
        .unwrap();

    let view = binding_to_view(
      &binding,
      IisHandoffState::HandedOff,
      None,
      Some(binding.restore_summary()),
    );

    assert_eq!(view.domain_key.as_deref(), Some("app.localhost"));
    assert_eq!(view.identity.site_name, "Default Web Site");
    assert!(view.restore_metadata.is_some());
  }

  #[test]
  fn backend_http_binding_uses_loopback_ip_and_route_host_for_iis_listener() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "https", "*:443:").unwrap();

    let backend = binding.backend_http_binding(41043, "iis-app.localhost");

    assert_eq!(backend.protocol, "http");
    assert_eq!(backend.ip_address, "127.0.0.1");
    assert_eq!(
      backend.binding_information,
      "127.0.0.1:41043:iis-app.localhost"
    );
  }

  #[tokio::test]
  async fn metadata_store_persists_and_removes_handoffs() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("nested").join("daemon.json");
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:app.localhost")
        .unwrap();
    let record = IisRestoreRecord {
      binding: binding.clone(),
      domain_key: "app.localhost".to_string(),
      registration_id: Some("shim-1".to_string()),
      backend_binding: Some(binding.backend_http_binding(41000, "app.localhost")),
    };

    let store = IisMetadataStore::load(path.clone()).await.unwrap();
    store.insert(binding.binding_id(), record).await.unwrap();
    let reloaded = IisMetadataStore::load(path).await.unwrap();

    assert_eq!(reloaded.snapshot().await.len(), 1);
    let removed = reloaded.remove(&binding.binding_id()).await.unwrap();
    assert_eq!(removed.unwrap().domain_key, "app.localhost");
    assert!(reloaded.snapshot().await.is_empty());
  }

  #[tokio::test]
  async fn metadata_store_persists_https_certificate_metadata() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("daemon.json");
    let mut binding = IisBindingRecord::from_binding_information(
      "Default Web Site",
      "https",
      "*:443:secure.localhost",
    )
    .unwrap();
    binding.tls_certificate = Some(IisTlsCertificate {
      thumbprint: "aabbcc".to_string(),
      store_name: "My".to_string(),
      ssl_flags: Some(1),
    });
    let record = IisRestoreRecord {
      binding: binding.clone(),
      domain_key: "secure.localhost".to_string(),
      registration_id: None,
      backend_binding: Some(binding.backend_http_binding(41043, "secure.localhost")),
    };

    let store = IisMetadataStore::load(path.clone()).await.unwrap();
    store.insert(binding.binding_id(), record).await.unwrap();
    let reloaded = IisMetadataStore::load(path).await.unwrap();
    let snapshot = reloaded.snapshot().await;
    let tls = snapshot
      .get(&binding.binding_id())
      .and_then(|record| record.binding.tls_certificate.as_ref())
      .unwrap();

    assert_eq!(tls.thumbprint, "aabbcc");
    assert_eq!(tls.store_name, "My");
    assert_eq!(tls.ssl_flags, Some(1));
  }

  #[tokio::test]
  async fn metadata_store_reports_invalid_json() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("daemon.json");
    tokio::fs::write(&path, "{not-json").await.unwrap();

    let error = IisMetadataStore::load(path).await.unwrap_err();

    assert!(error.to_string().contains("parse daemon metadata"));
  }

  #[tokio::test]
  async fn fake_provider_reports_configured_mutation_failures() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:app.localhost")
        .unwrap();
    let provider = IisProvider::fake(vec![binding.clone()]);
    provider
      .set_fail_remove(IisIssue::new(
        IisIssueKind::InsufficientPrivileges,
        "denied",
      ))
      .await;

    let remove = provider.remove_binding(&binding).await.unwrap_err();
    provider
      .set_fail_restore(IisIssue::new(IisIssueKind::ProviderError, "restore failed"))
      .await;
    let restore = provider.restore_binding(&binding).await.unwrap_err();

    assert_eq!(remove.kind, IisIssueKind::InsufficientPrivileges);
    assert_eq!(restore.kind, IisIssueKind::ProviderError);
  }

  #[tokio::test]
  async fn fake_provider_reports_missing_remove_binding() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:app.localhost")
        .unwrap();
    let provider = IisProvider::fake(Vec::new());

    let error = provider.remove_binding(&binding).await.unwrap_err();

    assert_eq!(error.kind, IisIssueKind::MissingBinding);
  }

  #[tokio::test]
  async fn fake_provider_discovers_and_restores_without_duplicates() {
    let binding =
      IisBindingRecord::from_binding_information("Default Web Site", "http", "*:80:app.localhost")
        .unwrap();
    let provider = IisProvider::fake(vec![binding.clone()]);

    assert_eq!(provider.discover().await.unwrap().len(), 1);
    provider.restore_binding(&binding).await.unwrap();
    assert_eq!(provider.discover().await.unwrap().len(), 1);
  }

  #[cfg(windows)]
  #[test]
  fn parses_powershell_binding_json_shapes() {
    let single = br#"{"siteName":"Default Web Site","protocol":"http","bindingInformation":"*:80:app.localhost"}"#;
    let array = br#"[{"siteName":"Default Web Site","protocol":"http","bindingInformation":"*:80:app.localhost"}]"#;

    assert_eq!(parse_powershell_bindings(single).unwrap().len(), 1);
    assert_eq!(parse_powershell_bindings(array).unwrap().len(), 1);
    assert!(parse_powershell_bindings(b"").unwrap().is_empty());
    assert_eq!(
      parse_powershell_bindings(b"{bad-json").unwrap_err().kind,
      IisIssueKind::ProviderError
    );
  }

  #[cfg(windows)]
  #[test]
  fn classifies_powershell_errors_and_escapes_strings() {
    assert_eq!(ps_escape("Bob's Site"), "Bob''s Site");
    assert_eq!(
      classify_powershell_error(b"Access is denied").kind,
      IisIssueKind::InsufficientPrivileges
    );
    assert_eq!(
      classify_powershell_error(b"WebAdministration module missing").kind,
      IisIssueKind::IisUnavailable
    );
    assert_eq!(
      classify_powershell_error(b"unexpected").kind,
      IisIssueKind::ProviderError
    );
  }
}
