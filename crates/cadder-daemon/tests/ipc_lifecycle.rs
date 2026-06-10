use cadder_daemon::{
    CadderClient, CadderSession, CaddyConfigAdapter, CaddyConfigCoordinator, DaemonServer,
    DaemonState, ProcessRuntime, RealCaddyResolver, RuntimePaths,
};
use cadder_protocol::{
    ActivationState, BasicResponse, EntrypointInstanceIdentity, EntrypointRegistration,
    LogStreamIdentity, OwnerProcessIdentity, QueryStateRequest, QueryStateResponse,
    RegisterEntrypointRequest, RegisterEntrypointResponse, ShimRunMetadata, SourcePath,
    UnregisterEntrypointRequest, message_types, new_request_id,
};
use chrono::Utc;
use std::{
    fs,
    path::{Path, PathBuf},
    time::Duration,
};
use tokio::{sync::watch, time::sleep};

#[tokio::test]
async fn ipc_lifecycle_supports_many_registrations_and_owner_cleanup() {
    let fixture = include_str!("fixtures/SmarketingReverseProxy.Caddyfile");
    let harness = Harness::start(fixture).await;

    let mut owners = Vec::new();
    let mut sessions = Vec::new();
    for index in 0..10 {
        let registration = registration(
            &format!("shim-{index}"),
            &format!("nonce-{index}"),
            &harness.config_path,
        );
        owners.push((
            registration.registration_id.clone(),
            registration.entrypoint_instance.shim_session_nonce.clone(),
        ));
        let mut session = CadderSession::connect(&harness.paths).await.unwrap();
        let response: RegisterEntrypointResponse = session
            .request(
                message_types::REGISTER_ENTRYPOINT_REQUEST,
                message_types::REGISTER_ENTRYPOINT_RESPONSE,
                &RegisterEntrypointRequest {
                    request_id: new_request_id("test-register"),
                    registration,
                },
            )
            .await
            .unwrap();
        assert!(response.accepted, "{}", response.message);
        sessions.push(session);
    }

    let snapshot = query_state(&harness.client).await;
    assert_eq!(snapshot.registrations.len(), 10);
    assert_eq!(snapshot.registrations[0].registered_domains.len(), 4);

    let wrong_owner: BasicResponse = harness
        .client
        .request(
            message_types::UNREGISTER_ENTRYPOINT_REQUEST,
            message_types::UNREGISTER_ENTRYPOINT_RESPONSE,
            &UnregisterEntrypointRequest {
                request_id: new_request_id("test-unregister"),
                registration_id: owners[0].0.clone(),
                shim_session_nonce: "wrong".to_string(),
            },
        )
        .await
        .unwrap();
    assert!(!wrong_owner.accepted);
    assert_eq!(query_state(&harness.client).await.registrations.len(), 10);

    let right_owner: BasicResponse = sessions[0]
        .request(
            message_types::UNREGISTER_ENTRYPOINT_REQUEST,
            message_types::UNREGISTER_ENTRYPOINT_RESPONSE,
            &UnregisterEntrypointRequest {
                request_id: new_request_id("test-unregister"),
                registration_id: owners[0].0.clone(),
                shim_session_nonce: owners[0].1.clone(),
            },
        )
        .await
        .unwrap();
    assert!(right_owner.accepted);
    assert_eq!(query_state(&harness.client).await.registrations.len(), 9);

    drop(sessions);
    for _ in 0..50 {
        if query_state(&harness.client).await.registrations.is_empty() {
            harness.shutdown().await;
            return;
        }
        sleep(Duration::from_millis(20)).await;
    }
    panic!("pipe disconnect cleanup did not remove owned registrations");
}

async fn query_state(client: &CadderClient) -> cadder_protocol::GuiStateSnapshot {
    let response: QueryStateResponse = client
        .request(
            message_types::QUERY_STATE_REQUEST,
            message_types::QUERY_STATE_RESPONSE,
            &QueryStateRequest {
                request_id: new_request_id("test-query"),
            },
        )
        .await
        .unwrap();
    response.snapshot.unwrap()
}

struct Harness {
    client: CadderClient,
    paths: RuntimePaths,
    shutdown_tx: watch::Sender<bool>,
    config_path: PathBuf,
    _temp: tempfile::TempDir,
}

impl Harness {
    async fn start(caddyfile: &str) -> Self {
        let temp = tempfile::tempdir().unwrap();
        let runtime_dir = temp.path().join("run");
        let paths = RuntimePaths::resolve(Some(runtime_dir)).unwrap();
        paths.ensure_dirs().unwrap();
        let config_path = temp.path().join("Caddyfile");
        fs::write(&config_path, caddyfile).unwrap();
        let fake_caddy = write_fake_caddy(temp.path());

        let resolver = RealCaddyResolver::new(Some(fake_caddy.display().to_string()));
        let adapter = CaddyConfigAdapter::new(resolver.clone());
        let runtime = ProcessRuntime::new(resolver, paths.clone());
        let state = DaemonState::new(CaddyConfigCoordinator::new(adapter, runtime));
        let server = DaemonServer::new(paths.clone(), state);
        let (shutdown_tx, shutdown_rx) = watch::channel(false);
        tokio::spawn(async move {
            let _ = server.run_until(shutdown_rx).await;
        });

        let client = CadderClient::new(paths.clone());
        for _ in 0..50 {
            if client
                .request::<_, QueryStateResponse>(
                    message_types::QUERY_STATE_REQUEST,
                    message_types::QUERY_STATE_RESPONSE,
                    &QueryStateRequest {
                        request_id: new_request_id("wait"),
                    },
                )
                .await
                .is_ok()
            {
                return Self {
                    client,
                    paths,
                    shutdown_tx,
                    config_path,
                    _temp: temp,
                };
            }
            sleep(Duration::from_millis(20)).await;
        }
        panic!("server did not become ready");
    }

    async fn shutdown(self) {
        let _ = self.shutdown_tx.send(true);
    }
}

fn registration(id: &str, nonce: &str, config_path: &Path) -> EntrypointRegistration {
    let now = Utc::now();
    let identity = EntrypointInstanceIdentity {
        instance_id: id.to_string(),
        started_at_utc: now,
        shim_session_nonce: nonce.to_string(),
    };
    EntrypointRegistration {
        registration_id: id.to_string(),
        entrypoint_instance: identity.clone(),
        source_working_directory: SourcePath::new(".", None),
        source_config_path: SourcePath::new(
            config_path.display().to_string(),
            Some(config_path.display().to_string()),
        ),
        registered_domains: Vec::new(),
        activation_state: ActivationState::Active,
        owner_process: OwnerProcessIdentity {
            process_id: 1,
            process_start_time_utc: now,
            shim_session_nonce: nonce.to_string(),
            executable_path: None,
        },
        log_stream: LogStreamIdentity::entrypoint(id),
        shim_run: Some(ShimRunMetadata {
            adapter: Some("caddyfile".to_string()),
            raw_arguments: vec!["run".to_string()],
            command_line: "run".to_string(),
        }),
        created_at_utc: now,
        last_heartbeat_utc: now,
    }
}

fn write_fake_caddy(dir: &Path) -> PathBuf {
    #[cfg(windows)]
    {
        let path = dir.join("fake-caddy.cmd");
        fs::write(
            &path,
            r#"@echo off
if "%1"=="adapt" (
  echo {"apps":{"http":{"servers":{"srv0":{"routes":[{"match":[{"host":["api.smarketing.localhost","app.smarketing.localhost","mailbox.smarketing.localhost","storage.smarketing.localhost"]}],"handle":[{"handler":"static_response","body":"ok"}],"terminal":true}]}}}}}
  exit /b 0
)
if "%1"=="reload" exit /b 0
if "%1"=="run" (
  echo fake runtime started
  exit /b 0
)
exit /b 0
"#,
        )
        .unwrap();
        path
    }

    #[cfg(not(windows))]
    {
        use std::os::unix::fs::PermissionsExt;
        let path = dir.join("fake-caddy");
        fs::write(
            &path,
            r#"#!/usr/bin/env sh
if [ "$1" = "adapt" ]; then
  printf '%s\n' '{"apps":{"http":{"servers":{"srv0":{"routes":[{"match":[{"host":["api.smarketing.localhost","app.smarketing.localhost","mailbox.smarketing.localhost","storage.smarketing.localhost"]}],"handle":[{"handler":"static_response","body":"ok"}],"terminal":true}]}}}}}'
  exit 0
fi
if [ "$1" = "reload" ]; then exit 0; fi
if [ "$1" = "run" ]; then echo fake runtime started; exit 0; fi
exit 0
"#,
        )
        .unwrap();
        let mut permissions = fs::metadata(&path).unwrap().permissions();
        permissions.set_mode(0o755);
        fs::set_permissions(&path, permissions).unwrap();
        path
    }
}
