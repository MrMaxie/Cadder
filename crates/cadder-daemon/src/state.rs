use crate::{
  CaddyConfigCoordinator,
  logs::{CaddyLogStore, LogQuery},
};
use cadder_protocol::{
  ActivationState, BasicResponse, ConfigState, EntrypointRegistration, GuiStateSnapshot,
  HeartbeatEntrypointRequest, LogStreamIdentity, QueryLogsResponse, QueryStateResponse,
  RegisterEntrypointResponse, RuntimeState, SetDomainEnabledRequest, SetEntrypointEnabledRequest,
  StateChangeKind, StateChangedEvent,
};
use chrono::Utc;
use std::{collections::BTreeMap, sync::Arc};
use tokio::sync::{Mutex, broadcast};

#[derive(Debug, Clone)]
pub struct DaemonState {
  inner: Arc<Mutex<DaemonInner>>,
  events: broadcast::Sender<StateChangedEvent>,
  logs: CaddyLogStore,
}

#[derive(Debug)]
struct DaemonInner {
  registrations: BTreeMap<String, EntrypointRegistration>,
  coordinator: CaddyConfigCoordinator,
  sequence: u64,
}

impl DaemonState {
  pub fn new(coordinator: CaddyConfigCoordinator) -> Self {
    let (events, _) = broadcast::channel(256);
    Self {
      inner: Arc::new(Mutex::new(DaemonInner {
        registrations: BTreeMap::new(),
        coordinator,
        sequence: 0,
      })),
      events,
      logs: CaddyLogStore::default(),
    }
  }

  pub fn subscribe(&self) -> broadcast::Receiver<StateChangedEvent> {
    self.events.subscribe()
  }

  pub fn logs(&self) -> CaddyLogStore {
    self.logs.clone()
  }

  pub async fn register(
    &self,
    request_id: String,
    registration: EntrypointRegistration,
  ) -> RegisterEntrypointResponse {
    if let Err(message) = registration.validate_owner() {
      return RegisterEntrypointResponse {
        request_id,
        accepted: false,
        message,
        registration_id: None,
      };
    }

    let mut inner = self.inner.lock().await;
    let mut prepared = inner.coordinator.prepare_registration(registration).await;
    let now = Utc::now();
    prepared.created_at_utc = now;
    prepared.last_heartbeat_utc = now;
    let id = prepared.registration_id.clone();
    inner.registrations.insert(id.clone(), prepared);
    let registrations = inner.registrations.values().cloned().collect::<Vec<_>>();
    inner.coordinator.apply(&registrations, &self.logs).await;
    self
      .publish_locked(
        &mut inner,
        StateChangeKind::RegistrationsChanged,
        Some(id.clone()),
      )
      .await;

    RegisterEntrypointResponse {
      request_id,
      accepted: true,
      message: "Entrypoint registered.".to_string(),
      registration_id: Some(id),
    }
  }

  pub async fn unregister(
    &self,
    request_id: String,
    registration_id: &str,
    shim_session_nonce: &str,
  ) -> BasicResponse {
    let mut inner = self.inner.lock().await;
    let removed = inner
      .registrations
      .get(registration_id)
      .is_some_and(|registration| {
        registration.entrypoint_instance.shim_session_nonce == shim_session_nonce
      });
    if removed {
      inner.registrations.remove(registration_id);
      let registrations = inner.registrations.values().cloned().collect::<Vec<_>>();
      inner.coordinator.apply(&registrations, &self.logs).await;
      self
        .publish_locked(
          &mut inner,
          StateChangeKind::RegistrationsChanged,
          Some(registration_id.to_string()),
        )
        .await;
    }

    BasicResponse {
      request_id,
      accepted: removed,
      message: if removed {
        "Entrypoint unregistered."
      } else {
        "Entrypoint was not found for the requested owner."
      }
      .to_string(),
    }
  }

  pub async fn heartbeat(&self, request: HeartbeatEntrypointRequest) -> BasicResponse {
    let mut inner = self.inner.lock().await;
    let accepted = inner
      .registrations
      .get_mut(&request.registration_id)
      .filter(|registration| {
        registration.entrypoint_instance.shim_session_nonce == request.shim_session_nonce
      })
      .map(|registration| {
        registration.last_heartbeat_utc = Utc::now();
      })
      .is_some();
    if accepted {
      self
        .publish_locked(
          &mut inner,
          StateChangeKind::RegistrationsChanged,
          Some(request.registration_id),
        )
        .await;
    }

    BasicResponse {
      request_id: request.request_id,
      accepted,
      message: if accepted {
        "Heartbeat accepted."
      } else {
        "Entrypoint was not found for the requested owner."
      }
      .to_string(),
    }
  }

  pub async fn set_entrypoint_enabled(
    &self,
    request: SetEntrypointEnabledRequest,
  ) -> BasicResponse {
    let mut inner = self.inner.lock().await;
    let accepted = inner
      .registrations
      .get_mut(&request.registration_id)
      .filter(|registration| {
        request
          .shim_session_nonce
          .as_ref()
          .is_none_or(|nonce| registration.entrypoint_instance.shim_session_nonce == *nonce)
      })
      .map(|registration| {
        registration.activation_state = ActivationState::from_enabled(request.enabled);
      })
      .is_some();

    if accepted {
      let registrations = inner.registrations.values().cloned().collect::<Vec<_>>();
      inner.coordinator.apply(&registrations, &self.logs).await;
      self
        .publish_locked(
          &mut inner,
          StateChangeKind::RegistrationsChanged,
          Some(request.registration_id),
        )
        .await;
    }

    BasicResponse {
      request_id: request.request_id,
      accepted,
      message: if accepted {
        "Entrypoint activation updated."
      } else {
        "Entrypoint was not found."
      }
      .to_string(),
    }
  }

  pub async fn set_domain_enabled(&self, request: SetDomainEnabledRequest) -> BasicResponse {
    let mut inner = self.inner.lock().await;
    let accepted = inner
      .registrations
      .get_mut(&request.registration_id)
      .and_then(|registration| {
        registration.registered_domains.iter_mut().find(|domain| {
          domain
            .name
            .canonical
            .eq_ignore_ascii_case(&request.domain_key)
        })
      })
      .map(|domain| {
        domain.activation_state = ActivationState::from_enabled(request.enabled);
      })
      .is_some();

    if accepted {
      let registrations = inner.registrations.values().cloned().collect::<Vec<_>>();
      inner.coordinator.apply(&registrations, &self.logs).await;
      self
        .publish_locked(
          &mut inner,
          StateChangeKind::RegistrationsChanged,
          Some(request.registration_id),
        )
        .await;
    }

    BasicResponse {
      request_id: request.request_id,
      accepted,
      message: if accepted {
        "Domain activation updated."
      } else {
        "Domain was not found."
      }
      .to_string(),
    }
  }

  pub async fn query_state(&self, request_id: String) -> QueryStateResponse {
    QueryStateResponse {
      request_id,
      accepted: true,
      message: "State snapshot returned.".to_string(),
      snapshot: Some(self.snapshot().await),
    }
  }

  pub async fn query_logs(&self, request: cadder_protocol::QueryLogsRequest) -> QueryLogsResponse {
    let active = self.stream_is_active(&request.stream).await;
    let result = self.logs.query(
      LogQuery {
        stream: request.stream.clone(),
        limit: request.limit.unwrap_or(100).clamp(1, 500),
        after_sequence: request
          .cursor
          .as_deref()
          .and_then(|cursor| cursor.strip_prefix("seq:"))
          .and_then(|sequence| sequence.parse::<u64>().ok()),
        minimum_severity: request.minimum_severity,
      },
      active,
    );

    QueryLogsResponse {
      request_id: request.request_id,
      accepted: true,
      message: "Caddy logs returned.".to_string(),
      stream: request.stream,
      stream_status: result.status,
      entries: result.entries,
      next_cursor: result.next_cursor,
      has_gap: result.has_gap,
      has_more_before: result.has_more_before,
      truncated_by_retention: result.truncated_by_retention,
    }
  }

  pub async fn shutdown(&self) -> BasicResponse {
    let mut inner = self.inner.lock().await;
    let result = inner.coordinator.shutdown().await;
    BasicResponse {
      request_id: "shutdown".to_string(),
      accepted: result.is_ok(),
      message: result
        .map(|_| "Daemon shutdown requested.".to_string())
        .unwrap_or_else(|error| error.to_string()),
    }
  }

  pub async fn snapshot(&self) -> GuiStateSnapshot {
    let inner = self.inner.lock().await;
    self.snapshot_locked(&inner).await
  }

  async fn publish_locked(
    &self,
    inner: &mut DaemonInner,
    kind: StateChangeKind,
    registration_id: Option<String>,
  ) {
    inner.sequence += 1;
    let event = StateChangedEvent {
      request_id: "state-change".to_string(),
      sequence_number: inner.sequence,
      change_kind: kind,
      snapshot: self.snapshot_locked(inner).await,
      registration_id,
    };
    let _ = self.events.send(event);
  }

  async fn snapshot_locked(&self, inner: &DaemonInner) -> GuiStateSnapshot {
    GuiStateSnapshot {
      captured_at_utc: Utc::now(),
      registrations: inner.registrations.values().cloned().collect(),
      runtime: self.runtime_state_locked(inner).await,
      config: self.config_state_locked(inner),
    }
  }

  async fn runtime_state_locked(&self, inner: &DaemonInner) -> RuntimeState {
    inner.coordinator_runtime_state().await
  }

  fn config_state_locked(&self, inner: &DaemonInner) -> ConfigState {
    inner.coordinator.current_state()
  }

  async fn stream_is_active(&self, stream: &LogStreamIdentity) -> bool {
    let inner = self.inner.lock().await;
    if stream.stream_id == "runtime" || stream.stream_id == "runtime-control" {
      return true;
    }
    inner.registrations.values().any(|registration| {
      (registration.log_stream == *stream && registration.activation_state.is_enabled())
        || registration
          .registered_domains
          .iter()
          .any(|domain| domain.log_stream == *stream && domain.activation_state.is_enabled())
    })
  }
}

impl DaemonInner {
  async fn coordinator_runtime_state(&self) -> RuntimeState {
    self.coordinator.runtime_state().await
  }
}

#[cfg(test)]
mod tests {
  use super::*;
  use crate::{
    CaddyConfigAdapter, CaddyConfigCoordinator, ProcessRuntime, RealCaddyResolver, RuntimePaths,
  };
  use cadder_protocol::{
    EntrypointInstanceIdentity, LogAttributionKind, LogSeverity, LogStreamIdentity,
    LogStreamStatus, OwnerProcessIdentity, QueryLogsRequest, RegisteredDomain, SourcePath,
  };
  use chrono::Utc;

  fn state() -> DaemonState {
    let paths = RuntimePaths::resolve(Some(tempfile::tempdir().unwrap().keep())).unwrap();
    let resolver = RealCaddyResolver::new(Some("definitely-missing-caddy".to_string()));
    let adapter = CaddyConfigAdapter::new(resolver.clone());
    let runtime = ProcessRuntime::new(resolver, paths);
    DaemonState::new(CaddyConfigCoordinator::new(adapter, runtime))
  }

  fn registration(id: &str, nonce: &str) -> EntrypointRegistration {
    let now = Utc::now();
    EntrypointRegistration {
      registration_id: id.to_string(),
      entrypoint_instance: EntrypointInstanceIdentity {
        instance_id: id.to_string(),
        started_at_utc: now,
        shim_session_nonce: nonce.to_string(),
      },
      source_working_directory: SourcePath::new(".", None),
      source_config_path: SourcePath::new("Caddyfile", None),
      registered_domains: vec![RegisteredDomain::active("app.localhost")],
      activation_state: ActivationState::Active,
      owner_process: OwnerProcessIdentity {
        process_id: 1,
        process_start_time_utc: now,
        shim_session_nonce: nonce.to_string(),
        executable_path: None,
      },
      log_stream: LogStreamIdentity::entrypoint(id),
      shim_run: None,
      created_at_utc: now,
      last_heartbeat_utc: now,
    }
  }

  #[tokio::test]
  async fn register_and_unregister_preserve_owner_boundary() {
    let state = state();
    let response = state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;
    assert!(response.accepted);

    let wrong = state
      .unregister("wrong".to_string(), "shim-1", "other")
      .await;
    assert!(!wrong.accepted);
    assert_eq!(state.snapshot().await.registrations.len(), 1);

    let right = state
      .unregister("right".to_string(), "shim-1", "nonce-1")
      .await;
    assert!(right.accepted);
    assert!(state.snapshot().await.registrations.is_empty());
  }

  #[tokio::test]
  async fn disabled_domain_log_stream_is_reported_stale() {
    let state = state();
    let response = state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;
    assert!(response.accepted);
    let stream = LogStreamIdentity::domain("app.localhost");
    state.logs().append(
      stream.clone(),
      LogSeverity::Info,
      "domain log",
      LogAttributionKind::Domain,
      None,
    );

    let toggle = state
      .set_domain_enabled(SetDomainEnabledRequest {
        request_id: "toggle".to_string(),
        registration_id: "shim-1".to_string(),
        domain_key: "app.localhost".to_string(),
        enabled: false,
      })
      .await;
    assert!(toggle.accepted);

    let logs = state
      .query_logs(QueryLogsRequest {
        request_id: "logs".to_string(),
        stream,
        limit: Some(10),
        cursor: None,
        minimum_severity: None,
      })
      .await;

    assert_eq!(logs.stream_status, LogStreamStatus::Stale);
    assert_eq!(logs.entries.len(), 1);
  }

  #[tokio::test]
  async fn register_rejects_invalid_owner_identity() {
    let state = state();
    let mut registration = registration("shim-1", "nonce-1");
    registration.owner_process.shim_session_nonce = "different".to_string();

    let response = state.register("register".to_string(), registration).await;

    assert!(!response.accepted);
    assert!(response.message.contains("nonce values must match"));
    assert!(state.snapshot().await.registrations.is_empty());
  }

  #[tokio::test]
  async fn subscribe_receives_registration_change_event() {
    let state = state();
    let mut events = state.subscribe();

    let response = state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;
    let event = events.recv().await.unwrap();

    assert!(response.accepted);
    assert_eq!(event.sequence_number, 1);
    assert_eq!(event.change_kind, StateChangeKind::RegistrationsChanged);
    assert_eq!(event.registration_id.as_deref(), Some("shim-1"));
    assert_eq!(event.snapshot.registrations.len(), 1);
  }

  #[tokio::test]
  async fn heartbeat_accepts_owner_and_rejects_wrong_nonce() {
    let state = state();
    state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;

    let accepted = state
      .heartbeat(HeartbeatEntrypointRequest {
        request_id: "heartbeat".to_string(),
        registration_id: "shim-1".to_string(),
        shim_session_nonce: "nonce-1".to_string(),
      })
      .await;
    let rejected = state
      .heartbeat(HeartbeatEntrypointRequest {
        request_id: "heartbeat".to_string(),
        registration_id: "shim-1".to_string(),
        shim_session_nonce: "wrong".to_string(),
      })
      .await;

    assert!(accepted.accepted);
    assert_eq!(accepted.message, "Heartbeat accepted.");
    assert!(!rejected.accepted);
    assert_eq!(
      rejected.message,
      "Entrypoint was not found for the requested owner."
    );
  }

  #[tokio::test]
  async fn set_entrypoint_enabled_accepts_optional_owner_and_rejects_wrong_owner() {
    let state = state();
    state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;

    let accepted = state
      .set_entrypoint_enabled(SetEntrypointEnabledRequest {
        request_id: "disable".to_string(),
        registration_id: "shim-1".to_string(),
        shim_session_nonce: None,
        enabled: false,
      })
      .await;
    let rejected = state
      .set_entrypoint_enabled(SetEntrypointEnabledRequest {
        request_id: "enable".to_string(),
        registration_id: "shim-1".to_string(),
        shim_session_nonce: Some("wrong".to_string()),
        enabled: true,
      })
      .await;
    let snapshot = state.snapshot().await;

    assert!(accepted.accepted);
    assert_eq!(accepted.message, "Entrypoint activation updated.");
    assert!(!rejected.accepted);
    assert_eq!(
      snapshot.registrations[0].activation_state,
      ActivationState::Inactive
    );
  }

  #[tokio::test]
  async fn set_domain_enabled_rejects_unknown_domain() {
    let state = state();
    state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;

    let response = state
      .set_domain_enabled(SetDomainEnabledRequest {
        request_id: "toggle".to_string(),
        registration_id: "shim-1".to_string(),
        domain_key: "missing.localhost".to_string(),
        enabled: false,
      })
      .await;

    assert!(!response.accepted);
    assert_eq!(response.message, "Domain was not found.");
  }

  #[tokio::test]
  async fn query_state_returns_snapshot_with_request_id() {
    let state = state();
    state
      .register("register".to_string(), registration("shim-1", "nonce-1"))
      .await;

    let response = state.query_state("state".to_string()).await;

    assert!(response.accepted);
    assert_eq!(response.request_id, "state");
    assert_eq!(response.message, "State snapshot returned.");
    assert_eq!(response.snapshot.unwrap().registrations.len(), 1);
  }

  #[tokio::test]
  async fn runtime_control_logs_are_active_and_limit_is_clamped() {
    let state = state();
    let stream = LogStreamIdentity::runtime_control();
    state.logs().append(
      stream.clone(),
      LogSeverity::Info,
      "first",
      LogAttributionKind::RuntimeControl,
      Some("start".to_string()),
    );
    state.logs().append(
      stream.clone(),
      LogSeverity::Warn,
      "second",
      LogAttributionKind::RuntimeControl,
      Some("reload".to_string()),
    );

    let response = state
      .query_logs(QueryLogsRequest {
        request_id: "logs".to_string(),
        stream: stream.clone(),
        limit: Some(0),
        cursor: Some("not-a-sequence".to_string()),
        minimum_severity: Some(LogSeverity::Info),
      })
      .await;

    assert_eq!(response.stream, stream);
    assert_eq!(response.stream_status, LogStreamStatus::Active);
    assert_eq!(response.entries.len(), 1);
    assert_eq!(response.entries[0].raw_message, "second");
    assert!(response.next_cursor.is_some());
  }

  #[tokio::test]
  async fn shutdown_returns_success_when_runtime_is_idle() {
    let state = state();

    let response = state.shutdown().await;

    assert!(response.accepted);
    assert_eq!(response.message, "Daemon shutdown requested.");
  }
}
