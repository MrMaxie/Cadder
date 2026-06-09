---
id: TASK-1.2
title: Implement single-instance daemon lifecycle
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:41'
updated_date: '2026-06-09 13:18'
labels: []
dependencies:
  - TASK-1.1
modified_files:
  - src/Cadder.Daemon/DaemonLifecycle.cs
  - src/Cadder.Daemon/DaemonSingleton.cs
  - src/Cadder.Daemon/Properties/AssemblyInfo.cs
  - tests/Cadder.Daemon.Tests/DaemonLifecycleTests.cs
  - src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj
  - src/Cadder.Tray.WinUI/Program.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - src/Cadder.Tray.WinUI/MainWindow.xaml.cs
  - src/Cadder.Tray.WinUI/MainPage.xaml
  - src/Cadder.Tray.WinUI/MainPage.xaml.cs
  - src/Cadder.Tray.WinUI/DaemonTrayPresence.cs
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: high
ordinal: 3000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the Cadder singleton daemon lifecycle. The daemon is the long-lived process behind the tray icon and must remain alive with zero registered projects until the user explicitly quits it.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Starting Cadder when no daemon is running creates exactly one daemon process and tray presence.
- [x] #2 Starting Cadder again while the daemon is running forwards intent to the existing daemon instead of creating a second daemon.
- [x] #3 The daemon remains alive and visible in the tray when the registration count drops to zero.
- [x] #4 The daemon exposes an explicit quit path that shuts down IPC, removes transient registrations, and stops any Cadder-owned Caddy runtime cleanly.
- [x] #5 Single-instance protection handles stale locks or abandoned mutex state without permanently blocking startup.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Implement the Cadder singleton daemon lifecycle so the tray-backed daemon is a long-lived per-user process, survives zero registrations, forwards repeat launches to the existing instance, and exits only through an explicit quit path.

## Scope
- Add daemon lifecycle coordination and single-instance protection for the tray/daemon process.
- Use a per-user named mutex for deterministic single-instance ownership and abandoned/stale lock recovery.
- Use Windows App SDK app lifecycle redirection for repeat WinUI activations before creating a second window.
- Add a minimal intent-forwarding path for repeat Cadder launches without implementing the full shim registration flow.
- Add an explicit daemon quit path that shuts down IPC, clears transient registrations, asks the Cadder-owned Caddy runtime boundary to stop, releases the singleton lock, and exits the app.
- Keep zero registrations as a normal active daemon state.

## Key Files And Modules
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Daemon/` new lifecycle/single-instance classes as needed
- `src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj`
- `src/Cadder.Tray.WinUI/Program.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `src/Cadder.Tray.WinUI/MainWindow.xaml.cs`
- `src/Cadder.CaddyShim/ShimEntrypoint.cs` only if a minimal launch/forwarding probe is needed
- `src/Cadder.Contracts/IpcContracts.cs` only for minimal lifecycle intent/quit DTOs if required
- `tests/Cadder.Daemon.Tests/`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Add daemon lifecycle types in `Cadder.Daemon` that model singleton acquisition results, lifecycle state, and shutdown sequencing. Keep the API small and testable without WinUI.
2. Implement per-user named mutex acquisition with immediate ownership when available, a clear `already running` result when owned elsewhere, and `AbandonedMutexException` handling that treats the lock as recoverable after verifying local lifecycle state can be rebuilt.
3. Add tests for first acquisition, second acquisition forwarding result, abandoned mutex recovery, lock release on quit, and no shutdown when the registration list is empty.
4. Extend daemon boundaries only where the lifecycle needs explicit hooks: start IPC, stop IPC, clear transient registrations, and stop Cadder-owned runtime. Use no-op/fake implementations in tests; do not implement real Caddy runtime behavior.
5. Add a custom WinUI `Program.cs` and set `DISABLE_XAML_GENERATED_MAIN` in `Cadder.Tray.WinUI.csproj` so single-instance/redirection logic runs before any window is created.
6. In the WinUI startup path, call `AppInstance.FindOrRegisterForKey` with a stable Cadder per-user key. If the current process is not the registered instance, redirect activation with `RedirectActivationToAsync`, signal/forward the launch intent as available, and exit before creating `MainWindow`.
7. Update `App.xaml.cs` so the primary instance creates the daemon lifecycle host, keeps the tray/window state alive with zero registrations, and routes explicit quit through the lifecycle shutdown path.
8. Add a visible but minimal explicit quit command if the current scaffold lacks one. It may be a simple UI command or lifecycle method wired from the main window; full tray popup design remains TASK-1.8.
9. Keep `Cadder.CaddyShim` changes minimal. If needed, let it detect an existing daemon/start intent boundary, but leave real registration, argument parsing, owner cleanup, and process-lifetime registration semantics to TASK-1.3 and TASK-1.4.
10. Update `docs/ARCHITECTURE.md` with the singleton lifecycle decision: the tray host is the daemon, the singleton is per-user, zero registrations do not terminate the process, repeat starts forward intent, and stale/abandoned locks are recoverable.

## Validation
- Run `./build.ps1` from the repository root.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -r win-x64`.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- Manually smoke test on Windows: launch Cadder twice and confirm only one daemon/tray presence; request explicit quit and confirm the process exits and a later launch succeeds.
- Run `git status --short` before finishing and remove only temporary artifacts created by the implementation.

## Scope Boundaries
- Do not implement full caddy.exe shim registration semantics.
- Do not implement owner heartbeat, process watcher cleanup, or durable registration store behavior beyond transient shutdown clearing hooks.
- Do not parse, compose, or reload Caddy configs.
- Do not resolve, start, health-check, reload, or manage the real Caddy runtime beyond calling the lifecycle boundary used later by TASK-1.6.
- Do not build the full tray popup, panel overview, toggles, domain logs, or packaging/PATH installation.
- Do not add end-to-end lifecycle automation beyond focused unit tests and manual smoke instructions.

## Risks And Notes
- WinUI apps are multi-instance by default, so single-instance logic must run before `Application.Start` creates windows.
- App lifecycle redirection is useful for WinUI activation, but a named mutex keeps singleton ownership deterministic and testable for the daemon lifecycle.
- Abandoned mutex acquisition means the previous owner exited without releasing the lock; recovery must rebuild transient in-memory state rather than treating the daemon as still running.
- Concurrent launches are the main race risk. The implementation should make lock acquisition atomic and keep forwarding behavior idempotent.
- Explicit quit must be distinct from zero registrations. A daemon with no registered projects is still an active daemon.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation.

Implemented singleton daemon lifecycle in small increments. Added a testable DaemonLifecycleHost, per-user named mutex coordinator with abandoned-mutex recovery handling, explicit shutdown sequencing, zero-registration running state, and forwarded activation recording. Integrated WinUI startup through a custom Program.cs using AppInstance redirection before window creation, added minimal Shell_NotifyIcon tray presence, hid the main window on close so close is not quit, and routed the visible Quit daemon command through lifecycle shutdown. Addressed review findings by rolling lifecycle state back after failed IPC start, recording activation kind/payload metadata for forwarded launches, and adding a retry test.

Validation: dotnet restore Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64; dotnet test Cadder.slnx --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 (13 passed); .\build.ps1; dotnet format Cadder.slnx --verify-no-changes. Runtime smoke: launching the tray app kept one process alive; launching a second instance with --smoke-forward exited with code 0 while one daemon process remained; closing the main window did not exit the daemon. UI Automation could not reliably click the Quit daemon button in this session, so explicit quit is verified by unit shutdown sequencing and compiled UI wiring rather than automated UI click.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the Cadder single-instance daemon lifecycle.

What changed:
- Added daemon lifecycle and singleton mutex primitives in Cadder.Daemon, including abandoned mutex recovery and explicit shutdown sequencing.
- Added focused daemon tests for first/second acquisition, abandoned recovery, shutdown cleanup, zero-registration persistence, forwarded launch recording, and failed-start retry.
- Added a custom WinUI Program.cs that performs AppInstance redirection and singleton acquisition before creating the app/window.
- Wired App.xaml.cs to own the lifecycle host, record redirected activations, expose explicit quit, and keep the daemon alive when the window is closed.
- Added minimal tray presence via Shell_NotifyIcon and documented the singleton lifecycle architecture.

Validation:
- dotnet restore Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
- dotnet test Cadder.slnx --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 (13 passed)
- .\build.ps1
- dotnet format Cadder.slnx --verify-no-changes
- Runtime smoke confirmed a second launch exits with code 0 while one daemon process remains, and closing the window does not quit the daemon.

Risk/follow-up:
- Full tray popup/menu behavior remains deferred to TASK-1.8; the current tray presence is intentionally minimal.
<!-- SECTION:FINAL_SUMMARY:END -->
