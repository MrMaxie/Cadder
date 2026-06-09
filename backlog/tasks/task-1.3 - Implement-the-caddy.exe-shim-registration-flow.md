---
id: TASK-1.3
title: Implement the caddy.exe shim registration flow
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:42'
updated_date: '2026-06-09 14:04'
labels: []
dependencies:
  - TASK-1.1
  - TASK-1.2
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\package.json'
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
modified_files:
  - docs/ARCHITECTURE.md
  - src/Cadder.CaddyShim/Program.cs
  - src/Cadder.CaddyShim/ShimEntrypoint.cs
  - src/Cadder.CaddyShim/ShimRunCommand.cs
  - src/Cadder.CaddyShim/ShimRegistration.cs
  - src/Cadder.CaddyShim/ShimDaemonConnection.cs
  - src/Cadder.CaddyShim/ShimDaemonStartup.cs
  - src/Cadder.CaddyShim/ShimRuntime.cs
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Contracts/IpcContracts.cs
  - src/Cadder.Contracts/IpcPipeProtocol.cs
  - src/Cadder.Daemon/CadderIpcEndpoint.cs
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Daemon/DaemonLifecycle.cs
  - src/Cadder.Daemon/DaemonRegistrationStore.cs
  - src/Cadder.Daemon/NamedPipeDaemonIpcServer.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj
  - tests/Cadder.Daemon.Tests/DaemonLifecycleTests.cs
  - tests/Cadder.Daemon.Tests/NamedPipeDaemonIpcServerTests.cs
  - tests/Cadder.Daemon.Tests/ShimCommandParserTests.cs
  - tests/Cadder.Daemon.Tests/ShimEntrypointTests.cs
parent_task_id: TASK-1
priority: high
ordinal: 4000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the PATH-facing caddy.exe shim that project scripts invoke. The shim should start or connect to the singleton daemon, register the invoking Caddy configuration, and keep that registration alive only for the lifetime of the shim process.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Running caddy.exe run from a project directory registers that directory and its Caddyfile with the daemon.
- [x] #2 The shim supports explicit Caddy config and adapter flags needed by caddy run, including --config and --adapter.
- [x] #3 If the daemon is not running, the shim starts it and waits until IPC is ready before registering.
- [x] #4 The shim keeps the registration alive until normal exit, Ctrl+C, parent terminal close, or process termination is detected by the daemon.
- [x] #5 Unsupported Caddy commands either delegate to the real Caddy binary or fail with a clear message that names the supported Cadder command set.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Implement the PATH-facing `caddy.exe run` shim flow so project scripts can start or connect to the singleton Cadder daemon, register the invoking Caddy configuration, and keep that registration alive only while the shim process is alive.

## Scope
- Support `caddy.exe run` from a project working directory, including the default `Caddyfile` lookup.
- Support the explicit `caddy run` flags required by the task: `--config` and `--adapter`, in both separated and equals forms where applicable.
- Add the minimal daemon IPC needed for shim registration, keepalive/session lifetime, and unregister/disconnect behavior.
- Start the daemon when IPC is not available, then wait for IPC readiness with a bounded timeout before registering.
- Keep the full owner-aware registry, broad IPC API, GUI subscriptions, domain extraction, config composition, real Caddy runtime management, and packaging/PATH installer behavior deferred to later tasks.
- For unsupported Caddy commands in this task, prefer a clear Cadder error message that names the supported command set instead of delegating to the real Caddy binary, because real Caddy resolution is deferred to TASK-1.6 and TASK-1.11.

## Key Files And Modules
- `src/Cadder.CaddyShim/ShimEntrypoint.cs`
- `src/Cadder.Contracts/IpcContracts.cs`
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Daemon/DaemonLifecycle.cs`
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `docs/ARCHITECTURE.md`
- `tests/Cadder.Daemon.Tests/`
- New or updated shim-focused tests, in an existing or new test project depending on the smallest clean fit.

## Implementation Steps
1. Add a small, testable shim argument parser for `run`. It should recognize `--config <path>`, `--config=<path>`, `--adapter <name>`, and `--adapter=<name>`, preserve raw arguments, reject missing option values, and resolve the default config as `Caddyfile` under the current working directory when `--config` is omitted.
2. Add a shim run options/session model that captures the source working directory, raw and canonical config path, adapter name, raw command line arguments, started time, process identity, and a generated shim session nonce.
3. Extend the shared IPC contracts only as much as this task needs: request/response shapes for registering a shim-owned entrypoint, maintaining a live shim session, and ending the registration on normal disconnect/unregister.
4. Implement a minimal per-user named-pipe IPC transport shared by the daemon and shim. Keep it intentionally narrow and JSON-friendly so TASK-1.4 can expand it into the full register/update/list/toggle/unregister API without replacing the task's core behavior.
5. Replace the WinUI daemon's `NoopDaemonIpcServer` with the minimal real IPC server. It should accept shim registration requests, update the daemon lifecycle registration count, and remove the registration when the client disconnects or unregisters.
6. Implement shim daemon discovery/startup: attempt IPC connection first; if unavailable, start the Cadder tray daemon executable through a testable resolver, then poll IPC readiness with cancellation and timeout.
7. Keep daemon executable resolution separate from real Caddy binary resolution. Provide a test/environment override for local tests and use the current build/package layout as the default local resolver path.
8. After registration succeeds, keep the shim process alive until normal exit, Ctrl+C, terminal close, cancellation, or hard process termination. On graceful exit, send unregister/disconnect; on hard termination, rely on daemon-side pipe/session loss detection for this task.
9. Return clear exit codes and diagnostics: successful registration lifecycle returns `0`, parse/unsupported-command failures return non-zero, daemon startup/readiness failures name the timeout/IPC problem, and unsupported commands name the supported Cadder command set (`caddy run` plus `--cadder-shim-info`).
10. Update `docs/ARCHITECTURE.md` with the shim registration flow, the minimal IPC boundary added in this task, and the explicit handoff boundaries to TASK-1.4, TASK-1.6, and TASK-1.11.

## Validation
- Run `./build.ps1` from the repository root.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- Add focused unit tests for shim argument parsing, config path resolution, unsupported command messaging, daemon startup fallback, and registration/unregister lifecycle behavior through fakes.
- Smoke test from `D:\Projects\Selleo\smarketing\apps\reverse-proxy`: run the built shim as `caddy.exe run` and confirm the working directory and `Caddyfile` are registered with the daemon.
- Smoke test `caddy.exe run --config Caddyfile --adapter caddyfile`.
- Smoke test an unsupported command such as `caddy.exe version` and confirm the message clearly names the supported Cadder command set.
- Run `git status --short` before finishing and remove only temporary artifacts created by the implementation.

## Scope Boundaries
- Do not implement full owner-aware registration store behavior from TASK-1.4 beyond the minimum transient session tracking needed for this shim lifecycle.
- Do not implement register/update/list/toggle API breadth, GUI state subscriptions, or at-least-ten-concurrent-registration stress behavior; those are TASK-1.4.
- Do not parse domains from Caddyfiles, compose effective Caddy config, reload Caddy, or validate composed config; those are TASK-1.5.
- Do not resolve, inspect, start, reload, or delegate to the real Caddy binary; those are TASK-1.6 and TASK-1.11.
- Do not implement installer/PATH shadowing validation; that is TASK-1.11.
- Do not build tray/panel UI beyond any minimal diagnostics needed to validate daemon registration state.

## Risks And Notes
- Detecting parent terminal close may surface as process cancellation or named-pipe/session loss rather than a distinct terminal-close signal. That is acceptable for this task if the daemon removes only the affected shim registration.
- The daemon executable resolver must be testable and must not be confused with the future real Caddy resolver.
- The IPC implementation should stay simple enough to avoid preempting TASK-1.4 while still making TASK-1.3 behavior real and testable.
- Concurrent shim sessions should avoid shared mutable state races, but broad concurrency guarantees remain part of TASK-1.4.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation.

Implemented the TASK-1.3 shim registration flow in narrow vertical slices: parser/session metadata, minimal JSON named-pipe IPC, daemon-side transient registration cleanup, shim daemon startup/readiness fallback, and docs/tests. Validation passed: .\build.ps1; dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64; dotnet format Cadder.slnx --verify-no-changes; smoke-tested built caddy.exe --cadder-shim-info and unsupported caddy.exe version. Full external caddy run smoke against the referenced smarketing folder was not run to avoid launching the real tray UI; equivalent registration and disconnect behavior is covered by NamedPipeDaemonIpcServerTests.

Addressed adversarial review findings after initial closeout: moved daemon shutdown boundary calls outside the lifecycle gate to avoid cleanup deadlock, made shim register/unregister IPC failures controlled, rejected option flags as missing separated option values, assigned server-owned registration IDs per pipe session, and handled malformed IPC JSON before it can fault StopAsync. Added regression tests for each path. Validation after these fixes passed: dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64; .\build.ps1; dotnet format Cadder.slnx --verify-no-changes; caddy.exe --cadder-shim-info; caddy.exe version unsupported-command smoke.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the PATH-facing caddy.exe run shim flow for TASK-1.3.

What changed:
- Added a testable caddy run parser supporting default Caddyfile resolution plus --config/--adapter in separated and equals forms, including missing-value validation.
- Added shim session metadata, registration construction, daemon startup/readiness fallback, graceful unregister, controlled IPC failure diagnostics, and clear unsupported-command diagnostics.
- Added a minimal per-user JSON named-pipe IPC protocol, daemon endpoint, transient in-memory registration store, server-owned per-pipe registration IDs, and disconnect cleanup for shim-owned sessions.
- Replaced the WinUI daemon Noop IPC server with the minimal real IPC server and documented the TASK-1.3 flow and handoff boundaries.
- Adjusted daemon shutdown so boundary calls do not hold the lifecycle gate while client cleanup updates registration count.
- Added focused contract, shim, lifecycle, parser, and named-pipe regression tests.

Validation:
- .\build.ps1
- dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
- dotnet format Cadder.slnx --verify-no-changes
- Built caddy.exe --cadder-shim-info smoke test
- Built caddy.exe version unsupported-command smoke test returned exit code 2 with the supported Cadder command set

Review follow-up:
- A code-review subagent found shutdown, IPC failure, parser, ownership, and malformed-message edge cases. Each was fixed and covered by regression tests.

Notes:
- Real Caddy delegation/resolution, full owner-aware registration APIs, GUI subscriptions, domain extraction, and PATH installer behavior remain deferred to TASK-1.4, TASK-1.5, TASK-1.6, and TASK-1.11 as planned.
<!-- SECTION:FINAL_SUMMARY:END -->
