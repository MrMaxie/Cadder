---
id: TASK-1.5
title: Compose and reload Caddy config from registrations
status: In Progress
assignee:
  - '@agent'
created_date: '2026-06-09 11:42'
updated_date: '2026-06-09 16:49'
labels: []
dependencies:
  - TASK-1.4
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
modified_files:
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Daemon/CadderIpcEndpoint.cs
  - src/Cadder.Daemon/CaddyAdaptedConfig.cs
  - src/Cadder.Daemon/CaddyConfigCoordinator.cs
  - src/Cadder.Daemon/CaddyJsonConfigComposer.cs
  - src/Cadder.Daemon/CaddyJsonConfigInspector.cs
  - src/Cadder.Daemon/ProcessCaddyfileConfigAdapter.cs
  - src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs
  - tests/Cadder.Daemon.Tests/CaddyfileConfigAdapterTests.cs
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: high
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Convert active registrations into one effective Caddy configuration and apply it to the real Caddy runtime. The composition must preserve each source instance as a group while producing a valid runtime config.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cadder extracts hostnames/domains from registered Caddyfiles and associates every domain with its source entrypoint instance.
- [x] #2 The smarketing reverse-proxy Caddyfile registers api.smarketing.localhost, app.smarketing.localhost, mailbox.smarketing.localhost, and storage.smarketing.localhost as domains from one instance.
- [x] #3 Disabling a domain removes or neutralizes only that domain from the effective config while preserving the rest of the instance.
- [x] #4 Conflicting domains across instances are detected and reported with source paths before reload.
- [x] #5 Invalid composed config does not replace the last known good running Caddy config.
- [x] #6 Successful changes reload the real Caddy runtime without restarting unrelated shim processes.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Build the daemon-side pipeline that turns active entrypoint registrations into one effective Caddy JSON config, validates it, detects conflicts, preserves source grouping, and reloads the configured real Caddy runtime boundary only after validation succeeds.

## Scope
- Use Caddy's own adapter command shape, `caddy adapt --config <Caddyfile> --adapter caddyfile`, as the source of truth for interpreting registered Caddyfiles.
- Extract domains from adapted Caddy JSON host matchers and associate each domain with the source entrypoint registration.
- Support the referenced smarketing reverse-proxy Caddyfile and register api.smarketing.localhost, app.smarketing.localhost, mailbox.smarketing.localhost, and storage.smarketing.localhost as domains from one instance.
- Compose deterministic effective Caddy JSON from active registrations while preserving each source instance's routes as a contiguous group in registration-id order.
- Allow a single domain to be disabled by filtering only that host matcher while preserving the rest of the source instance.
- Detect conflicting active domains across registrations before runtime validation or reload and report the domain plus source config paths.
- Keep the last known good runtime config state when adaptation, composition, validation, or reload fails.
- Trigger successful runtime reload through a narrow process runtime boundary that calls the configured real Caddy command, currently `caddy-real`, without restarting or touching shim processes.
- Keep full real Caddy binary resolution, managed process lifecycle, health tracking, and packaging/PATH installation behavior in TASK-1.6 and TASK-1.11.

## Key Files And Modules
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Daemon/CadderIpcEndpoint.cs`
- `src/Cadder.Daemon/CaddyAdaptedConfig.cs`
- `src/Cadder.Daemon/CaddyConfigCoordinator.cs`
- `src/Cadder.Daemon/CaddyJsonConfigComposer.cs`
- `src/Cadder.Daemon/CaddyJsonConfigInspector.cs`
- `src/Cadder.Daemon/ProcessCaddyfileConfigAdapter.cs`
- `src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `tests/Cadder.Contracts.Tests/ContractShapeTests.cs`
- `tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs`
- `tests/Cadder.Daemon.Tests/CaddyfileConfigAdapterTests.cs`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Add JSON-friendly config state and diagnostics DTOs for GUI snapshots.
2. Extend the real runtime boundary with validate and reload operations that accept composed JSON.
3. Add a Caddyfile adapter boundary that runs `caddy-real adapt --config <path> --adapter caddyfile` and returns structured JSON or diagnostics.
4. Extract registered domains from adapted Caddy JSON host matchers, preserving raw and canonical lowercase host names.
5. Wire registration and update flows so the daemon populates `RegisteredDomains` from adapted source config metadata.
6. Compose effective Caddy JSON from active registrations, grouping each registration's routes contiguously and filtering inactive domain host matchers.
7. Detect active-domain conflicts before validation/reload and report source config paths.
8. Maintain last-known-good config hash and successful reload time across failed adaptation, composition, validation, or reload attempts.
9. Use a process runtime adapter for `caddy-real validate --config <json>` and `caddy-real reload --config <json>` without owning process lifecycle.
10. Surface config apply status and diagnostics in GUI state snapshots.
11. Update architecture documentation with TASK-1.5 semantics and the TASK-1.6/TASK-1.11 scope boundary.

## Validation
- Run focused daemon tests for adapt-based extraction, smarketing source support, composition, conflicts, disabled domains, invalid-config protection, and process validation.
- Run contract serialization tests for new shared DTOs.
- Run `dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1` from the repository root.
- Run `dotnet format Cadder.slnx --verify-no-changes`.

## Scope Boundaries
- Do not implement full real Caddy executable resolution, runtime process start/stop ownership, health polling, or configured runtime settings UI here; TASK-1.6 owns that.
- Do not implement global PATH/Scoop packaging or `caddy.exe` shim installation here; TASK-1.11 owns that.
- Do not build tray popup, registry pages, logs UI, or durable persistence in this task.
- Do not silently change acceptance criteria. If advanced Caddy JSON merging beyond host-routed HTTP servers becomes necessary, pause and ask whether to expand scope or create a follow-up.

## Runtime And Future Task Context
The user's local operating model is that the real global Caddy executable has been renamed to `caddy-real.exe` and is currently invoked as `caddy-real`. Future runtime/configuration work should let Cadder settings point at the real Caddy path or command. Future packaging work should install Cadder's PATH-facing shim globally with a command such as `scoop shim add caddy "path_to_cadder_caddy.exe"`.

## Risks And Notes
- Caddyfile interpretation is delegated to Caddy itself through `adapt`; Cadder only inspects and filters the adapted JSON.
- The supported merge scope is host-routed HTTP servers. More advanced Caddy JSON merging should be handled as a follow-up if needed.
- Validation/reload must remain atomic from Cadder's perspective: invalid new config must not replace the last known good running config state.
- Conflict detection must happen before reload so one bad registration cannot disrupt unrelated registrations.
- Reload sequencing must not depend on shim process restarts; registration IPC sessions remain untouched.
- The configured real Caddy command/path must not resolve back to Cadder's own shim; full resolver safety remains TASK-1.6/TASK-1.11.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Additional user-provided runtime context: the real global Caddy command has been renamed to `caddy-real.exe`/`caddy-real`; future runtime settings should point to the real Caddy path or command, and future packaging should install Cadder's shim globally with `scoop shim add caddy "path_to_cadder_caddy.exe"`.

Implementation direction updated after user feedback: do not hand-parse Caddyfile syntax as the source of truth. Use the real Caddy adapter command shape caddy adapt --config <Caddyfile> --adapter caddyfile to convert each source Caddyfile into structured JSON, then extract host matchers and compose/filter the effective runtime JSON from that adapted representation. Keep TASK-1.6 real runtime ownership separate; TASK-1.5 may use a narrow adapter-command boundary with caddy-real as the current local default when available.

Implemented TASK-1.5 using Caddy's own adapter path instead of hand-parsing Caddyfiles. Added daemon-side ProcessCaddyfileConfigAdapter for caddy-real adapt --config <path> --adapter caddyfile, JSON host extraction, deterministic JSON composition, per-domain host filtering, conflict diagnostics, last-known-good config state, and process-based real Caddy validate/reload calls. Wired the coordinator into IPC registration/update/toggle/unregister flows and WinUI daemon startup. Updated architecture documentation. Validation run: dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64, dotnet test tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64, dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64, ./build.ps1, and dotnet format Cadder.slnx --verify-no-changes all passed.
<!-- SECTION:NOTES:END -->
