---
id: TASK-1.6
title: Manage real Caddy runtime and health
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:42'
updated_date: '2026-06-09 17:31'
labels: []
dependencies:
  - TASK-1.5
modified_files:
  - docs/ARCHITECTURE.md
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Daemon/CaddyConfigCoordinator.cs
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - src/Cadder.Tray.WinUI/MainPage.xaml
  - src/Cadder.Tray.WinUI/MainPage.xaml.cs
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs
  - tests/Cadder.Daemon.Tests/RealCaddyRuntimeAdapterTests.cs
parent_task_id: TASK-1
priority: high
ordinal: 7000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Own the real Caddy runtime behind Cadder without confusing it with the caddy.exe shim. Cadder should be able to start, reload, observe, and stop its managed Caddy process or connect to a configured runtime path safely.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cadder resolves the real Caddy executable without recursively invoking its own caddy.exe shim.
- [x] #2 The daemon starts the real Caddy runtime when needed and tracks its process ID, admin endpoint, version, and health.
- [x] #3 With zero active domains, Cadder reaches a defined idle runtime state that the tray and panel can display.
- [x] #4 Runtime failures surface structured errors to the registry and GUI without crashing the daemon.
- [x] #5 Quitting the daemon stops only Cadder-owned Caddy runtime processes and does not kill unrelated user Caddy processes.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Turn the current thin `caddy-real validate/reload/version` boundary into a safe daemon-owned real Caddy runtime manager. Cadder must resolve a real Caddy executable without selecting its own PATH-facing `caddy.exe` shim, start and observe only the runtime it owns, expose runtime health to GUI snapshots, enter a defined idle state when there are no active domains, and stop only Cadder-owned runtime processes on daemon quit.

## Scope
- Build runtime ownership in the daemon, not in the shim.
- Preserve the TASK-1.5 config composition pipeline and keep `validate`/`reload` semantics atomic from Cadder's perspective.
- Replace the local `caddy-real` command default with a resolver-backed runtime identity while keeping `caddy-real` as the current development fallback.
- Track process metadata for Cadder-owned runtime instances: PID, process start time when available, admin endpoint, resolved binary identity, version, health, and structured diagnostics.
- Define and surface an idle runtime state for zero active domains that the tray/panel can render.
- Report runtime lifecycle/health failures through contracts and GUI state without crashing the daemon.
- Stop only the process Cadder started and still identifies as its owned runtime. Do not enumerate and kill unrelated `caddy.exe` or `caddy-real.exe` processes.

## Key Files And Modules
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs`
- `src/Cadder.Daemon/CaddyConfigCoordinator.cs`
- `src/Cadder.Daemon/CadderIpcEndpoint.cs`
- `src/Cadder.Daemon/DaemonLifecycle.cs`
- `src/Cadder.Daemon/RegistrationOwnerWatcher.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `src/Cadder.Tray.WinUI/MainPage.xaml`
- `src/Cadder.Tray.WinUI/MainPage.xaml.cs`
- `tests/Cadder.Contracts.Tests/ContractShapeTests.cs`
- `tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs`
- `tests/Cadder.Daemon.Tests/CaddyfileConfigAdapterTests.cs`
- `tests/Cadder.Daemon.Tests/DaemonLifecycleTests.cs`
- `tests/Cadder.Daemon.Tests/DaemonBoundaryTests.cs`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Extend shared runtime contracts with JSON-friendly fields for idle/running/unhealthy state, owned process ID, admin endpoint, runtime diagnostics, and enough binary/process identity for GUI and tests.
2. Add a real Caddy executable resolver in `Cadder.Daemon` that accepts a configured path/command, supports the current `caddy-real` development command, normalizes candidate paths, records file identity where practical, and rejects candidates that resolve to Cadder's own `caddy.exe` shim.
3. Refactor `ProcessRealCaddyRuntimeAdapter` into a resolver-backed process runtime boundary that can inspect the resolved binary, run version/validate/reload, and surface structured operation failures instead of returning only `NotResolved`.
4. Add a daemon-owned runtime manager implementing the existing shutdown boundary and the runtime adapter boundary. It should idempotently start Caddy when active domains need a runtime, remember only the process it started, probe/admin-observe that runtime, and expose a stable state snapshot.
5. Update `CaddyConfigCoordinator.ApplyAsync` so active composed configs ensure the owned runtime is running before validation/reload, while zero active domains produce the defined idle runtime/config state instead of treating absence of domains as an error.
6. Wire the owned runtime manager into `App.xaml.cs` so `DaemonLifecycleHost.ShutdownAsync` calls the real owned-runtime stop path instead of `NoopCadderOwnedRuntime`.
7. Route runtime lifecycle and health diagnostics through `CadderIpcEndpoint`/GUI snapshots, keeping registration and config diagnostics distinct but visible in the same snapshot model.
8. Update the minimal WinUI panel to display runtime status, PID, admin endpoint, version, idle state, and the latest runtime diagnostic using the existing quiet dashboard style.
9. Update architecture documentation with resolver precedence, shim exclusion, owned process lifecycle, idle behavior, health reporting, and the boundary left for TASK-1.11 packaging/PATH installation.

## Validation
- Add focused daemon unit tests with fakes/spies for resolver selection, shim rejection, owned runtime start, PID/admin/version/health tracking, idle transition with zero active domains, structured runtime failure reporting, and shutdown stopping only the owned process handle.
- Add contract serialization tests for new runtime state and diagnostics fields.
- Keep real `caddy-real` integration tests optional/skipped when the command is unavailable; the current environment has `caddy-real.exe` available through Scoop, but tests must remain deterministic without it.
- Run `dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1` from the repository root.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- For UI-facing changes, run the tray app when practical and inspect the panel/browser automation path available for the environment; do not claim live UI verification if it cannot be completed.

## Assumptions
- TASK-1.5 remains the source of truth for Caddyfile adaptation, domain extraction, composed JSON validation, and reload ordering.
- The local development real Caddy command is `caddy-real`/`caddy-real.exe`.
- `caddy.exe` may later be installed as Cadder's PATH-facing shim by TASK-1.11, so TASK-1.6 resolver tests must simulate shim and real-binary candidates even when the current PATH does not contain `caddy.exe`.
- The idle state for zero active domains is a normal daemon runtime state, not a daemon shutdown trigger.

## Risks And Scope Boundaries
- Do not implement global PATH/Scoop installation or `caddy.exe` shim packaging here; TASK-1.11 owns that.
- Do not build full logs capture or logs UI; TASK-1.7 and TASK-1.10 own those surfaces.
- Do not kill processes by name. Runtime shutdown must use stored Cadder-owned process identity/handle only.
- Avoid broad UI redesign; only add the runtime state needed for TASK-1.6 visibility.
- If Caddy admin endpoint probing requires config shape changes beyond the current composed JSON scope, pause and ask whether to expand TASK-1.6 or create a follow-up task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Local prerequisite check during planning found .NET SDK 10.0.204 and `caddy-real.exe` available at `D:\Projects\Internal\scoop\shims\caddy-real.exe`; `caddy.exe` is not currently on PATH, so shim-recursion safety should be covered with deterministic resolver tests.

Implemented TASK-1.6 runtime ownership path. Added JSON-friendly runtime contract fields for idle state, owned process identity, admin endpoint, version, and structured runtime diagnostics. Added resolver-backed ProcessRealCaddyRuntimeAdapter that resolves caddy-real/CADDER_CADDY_REAL_COMMAND, rejects known Cadder shim paths, starts a Cadder-owned Caddy process for active config, tracks its handle/metadata, and stops only that stored owned process on idle or daemon shutdown. Updated CaddyConfigCoordinator so empty active composition enters Idle instead of reloading `{}` and so runtime startup failures become structured config/runtime diagnostics. Wired WinUI daemon shutdown to the real runtime adapter and added a minimal runtime status panel. Updated architecture docs. Validation run: dotnet test tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64; dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64; dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64; ./build.ps1; dotnet format Cadder.slnx --verify-no-changes. Live WinUI launch was partially verified: PowerShell and Win32 automation confirmed a visible Cadder top-level window from the built exe, but UIA child inspection was unstable with pywinauto COM errors, so panel child text was not fully machine-verified. The temporary verification process was stopped.

Follow-up UI verification after adding the `winui-ui-testing` skill: added explicit AutomationProperties.AutomationId values to the runtime panel fields and Quit button in MainPage.xaml. Rebuilt the WinUI app successfully and reran dotnet format verification plus full solution tests. `winapp ui screenshot` captured the Cadder window and visually confirmed the runtime panel renders `Resolved`, admin endpoint, version, config status, registration count, diagnostic field, and Quit daemon button without visible overlap or clipping. `winapp ui inspect` / `wait-for` still reported only a disabled top-level pane or stale_element for this WinUI window, so child-element assertions through winapp could not be completed in this environment.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-09 16:31
---
Future runtime context from user approval of TASK-1.5: the real global Caddy executable has been renamed to `caddy-real.exe` and is currently expected to be invoked as `caddy-real`. Runtime configuration should allow Cadder settings to point at the real Caddy path or command, and must not resolve back to Cadder's own PATH-facing shim.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented real Caddy runtime ownership for Cadder. The daemon now resolves the real Caddy executable through a resolver-backed runtime boundary, rejects known Cadder `caddy.exe` shim paths, validates composed JSON, starts and tracks only the Cadder-owned runtime process when active domains need it, exposes PID/admin endpoint/version/runtime diagnostics in GUI snapshots, and stops only the stored owned process on idle or daemon shutdown.

The config coordinator now treats zero active domains as an explicit idle config/runtime state instead of reloading an empty `{}` config. Runtime start/version/resolve failures are surfaced as structured diagnostics without crashing daemon flows. The WinUI tray host is wired to the real owned-runtime stop path and the panel displays runtime status, process, admin endpoint, version, config state, registration count, and latest diagnostics. Architecture documentation and focused contract/daemon tests were updated, including deterministic fakes for shim rejection, owned-process stop behavior, idle transition, and startup failure reporting.

Additional follow-up from closeout: added root `AGENTS.md` guidance for future agents, including required routing to `winui-*`, `dotnet-*`, Windows desktop, Backlog.md, commit, and `winui-ui-testing` skills. Added explicit AutomationId values to the runtime panel controls and retained the user-provided WinApp SDK BuildTools connector package change so future WinUI test/debug flows can use `winapp ui`.

Validation run: `dotnet test tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `dotnet test tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `./build.ps1`, `dotnet build src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, and `dotnet format Cadder.slnx --verify-no-changes` passed. `winapp ui screenshot` visually confirmed the Cadder runtime panel renders correctly without visible overlap or clipping. `winapp ui inspect`/`wait-for` still saw only a disabled top-level pane or stale element for this WinUI window in this environment, so child-level WinApp assertions were not claimed as passing.
<!-- SECTION:FINAL_SUMMARY:END -->
