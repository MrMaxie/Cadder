---
id: TASK-1.4
title: Build IPC and owner-aware registration store
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:42'
updated_date: '2026-06-09 15:59'
labels: []
dependencies:
  - TASK-1.2
  - TASK-1.3
modified_files:
  - docs/ARCHITECTURE.md
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Contracts/IpcContracts.cs
  - src/Cadder.Contracts/IpcPipeProtocol.cs
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Daemon/DaemonRegistrationStore.cs
  - src/Cadder.Daemon/CadderIpcEndpoint.cs
  - src/Cadder.Daemon/GuiStateBroadcaster.cs
  - src/Cadder.Daemon/RegistrationOwnerWatcher.cs
  - src/Cadder.Daemon/NamedPipeDaemonIpcServer.cs
  - src/Cadder.Daemon/DaemonLifecycle.cs
  - src/Cadder.CaddyShim/ShimDaemonConnection.cs
  - src/Cadder.CaddyShim/ShimEntrypoint.cs
  - src/Cadder.CaddyShim/ShimRegistration.cs
  - src/Cadder.CaddyShim/ShimRuntime.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/CadderIpcEndpointTests.cs
  - tests/Cadder.Daemon.Tests/DaemonRegistrationStoreTests.cs
  - tests/Cadder.Daemon.Tests/NamedPipeDaemonIpcServerTests.cs
  - tests/Cadder.Daemon.Tests/RegistrationOwnerWatcherTests.cs
  - tests/Cadder.Daemon.Tests/ShimEntrypointTests.cs
parent_task_id: TASK-1
priority: high
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build the daemon-side IPC and owner-aware registration store used by the shim and GUI. Registrations are grouped by entrypoint process and removed automatically when their owning shim dies.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The IPC API can register, update, list, toggle, and unregister entrypoint-owned Caddy configs.
- [x] #2 Each registration records owner process ID, process start identity, executable path, working directory, config path, command line, created time, and last heartbeat.
- [x] #3 A daemon-side watcher removes only the registrations owned by a dead or disconnected shim.
- [x] #4 The registry supports zero, one, and at least ten simultaneous registrations without state corruption.
- [x] #5 The GUI can subscribe to state changes without polling tight loops.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Expand the minimal IPC and transient registration path from TASK-1.3 into the owner-aware daemon registration layer used by shim sessions and GUI state consumers.

## Scope
- Keep the registration store transient and in-memory for this task.
- Add the full IPC API required by TASK-1.4: register, update, list, toggle, unregister, heartbeat, and GUI state subscription.
- Record complete owner-aware metadata for each registration: owner PID, owner process start identity, executable path, working directory, config path, command line, created time, and last heartbeat.
- Add daemon-side owner cleanup for dead or disconnected shim sessions, ensuring cleanup removes only registrations owned by the affected owner/session.
- Add state-change notifications so GUI code can subscribe without tight polling loops.
- Preserve Caddyfile parsing, domain extraction, conflict detection, composed config reload, real Caddy runtime management, and full GUI pages for later tasks.

## Key Files And Modules
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Contracts/IpcContracts.cs`
- `src/Cadder.Contracts/IpcPipeProtocol.cs`
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Daemon/DaemonRegistrationStore.cs`
- `src/Cadder.Daemon/CadderIpcEndpoint.cs`
- `src/Cadder.Daemon/NamedPipeDaemonIpcServer.cs`
- `src/Cadder.CaddyShim/ShimDaemonConnection.cs`
- `src/Cadder.CaddyShim/ShimEntrypoint.cs`
- `src/Cadder.CaddyShim/ShimRegistration.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `tests/Cadder.Contracts.Tests/ContractShapeTests.cs`
- `tests/Cadder.Daemon.Tests/NamedPipeDaemonIpcServerTests.cs`
- New focused daemon tests as needed for store, endpoint, owner watcher, heartbeat, and subscription behavior.
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Extend the shared domain model with explicit registration lifecycle metadata. Add created time and last heartbeat fields, and keep owner identity as more than a PID by preserving process start time and shim session nonce.
2. Extend IPC contracts and message type constants for update, list, toggle, heartbeat, and subscribe operations. Keep DTOs JSON-friendly and preserve existing register/unregister/query messages where practical.
3. Replace the TASK-1.3 store's basic upsert/remove API with owner-aware operations. Keep the implementation in-memory, but make register/update/toggle/unregister/list operations safe for concurrent access and deterministic under at least ten simultaneous registrations.
4. Separate stable registration identity from pipe connection identity. The daemon may still track connection-owned registrations internally, but GUI/list/update/toggle flows should use stable registration IDs rather than IDs that accidentally encode transport sessions.
5. Extend `CadderIpcEndpoint` so all mutation operations update the store, refresh heartbeat or activation state as appropriate, publish registration count changes, and emit state-change notifications.
6. Add a daemon-side owner watcher behind a testable process abstraction. It should check owner liveness using PID plus recorded process start time, tolerate process lookup/access failures conservatively, and remove only registrations owned by the dead owner.
7. Extend `NamedPipeDaemonIpcServer` to dispatch the new request types and retain the existing safety behavior for malformed JSON, unknown message types, and disconnect cleanup. Disconnect cleanup should remain scoped to registrations owned by that pipe session.
8. Add GUI subscription support over IPC. Prefer a long-lived subscription stream that sends an initial snapshot followed by state-change events, so GUI consumers do not need tight polling loops.
9. Update shim-side IPC connection and shim session flow to send heartbeats while the shim remains alive, use the server-returned registration ID for later operations, and preserve graceful unregister on normal shutdown.
10. Update `App.xaml.cs` wiring if needed so the daemon host owns the expanded store, endpoint, owner watcher, and state broadcaster lifecycle.
11. Update `docs/ARCHITECTURE.md` with the TASK-1.4 IPC surface, owner identity rules, heartbeat semantics, process watcher cleanup, and GUI subscription model.

## Validation
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1` from the repository root.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- Add contract tests for new DTO shape and serialization.
- Add store/endpoint tests for zero, one, and at least ten simultaneous registrations.
- Add concurrency tests for parallel register, update, toggle, unregister, and list operations.
- Add owner cleanup tests for dead owner, PID reuse, disconnected pipe session, and cleanup isolation when multiple owners share similar metadata.
- Add named-pipe integration tests for all new IPC message types, malformed messages after registration, and subscription snapshot plus delta delivery.
- Smoke test two shim sessions concurrently and confirm closing one removes only that shim's registration while the daemon remains alive.

## Scope Boundaries
- Do not parse domains from Caddyfiles or populate real domain lists beyond existing DTO placeholders; TASK-1.5 owns parsing and config composition.
- Do not reload or manage the real Caddy runtime; TASK-1.6 owns runtime management.
- Do not build full tray popup or panel pages; TASK-1.8 and TASK-1.9 own UI surfaces. This task only provides subscribable state plumbing.
- Do not implement durable persistence unless explicitly requested later.
- Do not add or change acceptance criteria without user approval.

## Risks And Notes
- PID reuse is the main owner-cleanup risk; watcher logic must compare process start identity in addition to PID.
- Heartbeat semantics should be diagnostic and freshness-oriented, not the only cleanup mechanism, because pipe disconnect and process death are stronger ownership signals.
- Subscription support introduces server-push ordering concerns. The implementation should define initial snapshot plus subsequent event ordering clearly enough for GUI consumers.
- Existing TASK-1.3 behavior around disconnect cleanup must remain intact while expanding API breadth.
- `RegistrationId` stability matters for GUI and future runtime tasks; avoid leaking transport-specific connection IDs into user-facing identity unless the plan is revised deliberately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Baseline observed before recording: `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed, and `git status --short` was clean.

Implemented owner-aware in-memory registration storage with stable registration IDs, full IPC operations, heartbeat refresh, GUI state broadcasting, pipe-session cleanup, and PID/start-time owner cleanup. Kept Caddyfile parsing, real Caddy runtime reloads, durable persistence, and full GUI pages out of scope per plan. Validation passed: `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `./build.ps1`, and `dotnet format Cadder.slnx --verify-no-changes`. Two-session disconnect behavior is covered by named-pipe integration tests rather than a manual GUI smoke.

Independent review found blocking issues before final handoff: subscription ordering could replay pre-initial deltas after the initial snapshot, malformed register payloads could violate owner/session invariants and evade disconnect cleanup, owner probe process-exit races were treated as Unknown instead of Dead, and lifecycle startup did not stop IPC if owner watcher start failed. Reopened task for fixes.

Addressed independent review findings: serialized GUI broadcaster subscription setup with publish delivery so deltas cannot queue ahead of initial snapshot creation; added register invariant validation for stable registration ID and matching shim owner nonce; classified process disappearance during StartTime reads as Dead while keeping access failures Unknown; and added lifecycle rollback so IPC stops if owner watcher startup fails. Added regression tests for all four cases. Final validation passed: daemon targeted tests, full solution tests, `./build.ps1`, `dotnet format Cadder.slnx --verify-no-changes`, and `git diff --check`.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Summary:
- Expanded shared IPC/domain contracts with update, list, toggle, heartbeat, GUI subscription messages, lifecycle timestamps, and preserved shim command line metadata.
- Replaced the basic transient store with owner-aware in-memory operations keyed by stable registration IDs, guarded by validated shim session nonce and owner process identity.
- Added daemon-side GUI state broadcasting with ordered initial snapshots, process-owner watcher cleanup, stable named-pipe session ownership cleanup, lifecycle rollback on watcher startup failure, and shim heartbeat flow.
- Wired the tray daemon host to own the store, endpoint, broadcaster, and owner watcher lifecycle, and updated architecture documentation for TASK-1.4 semantics.

Validation:
- `dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed: 42 tests.
- `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed: 47 tests.
- `./build.ps1` passed with zero warnings/errors.
- `dotnet format Cadder.slnx --verify-no-changes` passed.
- `git diff --check` passed.

Risks / follow-ups:
- Domain parsing, composed Caddy config reloads, durable persistence, real runtime management, and full GUI surfaces remain intentionally deferred to later tasks.
<!-- SECTION:FINAL_SUMMARY:END -->
