---
id: TASK-1.8
title: Build Cadder tray popup UI
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:43'
updated_date: '2026-06-10 11:06'
labels: []
dependencies:
  - TASK-1.4
  - TASK-1.6
references:
  - .local/examples/gui/docs/images/openclawwindows1.png
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/TrayMenuWindow.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Services/TrayMenuStateBuilder.cs
modified_files:
  - Cadder.slnx
  - build.ps1
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - src/Cadder.Tray.WinUI/DaemonTrayPresence.cs
  - src/Cadder.Tray.WinUI/Properties/AssemblyInfo.cs
  - src/Cadder.Tray.WinUI/TrayPopupPositioner.cs
  - src/Cadder.Tray.WinUI/TrayPopupState.cs
  - src/Cadder.Tray.WinUI/TrayPopupWindow.xaml
  - src/Cadder.Tray.WinUI/TrayPopupWindow.xaml.cs
  - tests/Cadder.Tray.WinUI.Tests/Cadder.Tray.WinUI.Tests.csproj
  - tests/Cadder.Tray.WinUI.Tests/TrayPopupPositionerTests.cs
  - tests/Cadder.Tray.WinUI.Tests/TrayPopupStateBuilderTests.cs
parent_task_id: TASK-1
priority: high
ordinal: 9000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build the compact tray popup for Cadder using only the interaction patterns from the OpenClaw reference: brand header, state rows, grouped entities, right-aligned toggles, flyouts, and direct actions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The tray popup opens from the tray icon and displays daemon state, Caddy runtime state, active entrypoint count, and active domain count.
- [x] #2 The popup groups visible domains under their entrypoint instance and shows source path or project name for each group.
- [x] #3 Domain toggles are visible in the popup and call the daemon toggle API without closing the daemon.
- [x] #4 The popup exposes Open panel, reload or refresh, and Quit daemon actions.
- [x] #5 Keyboard navigation works with Up, Down, Enter, Space, Escape, and focus-loss dismissal.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Build Cadder's compact tray popup UI from the tray icon using only the interaction patterns from the OpenClaw reference: brand header, state rows, grouped entities, right-aligned toggles, flyouts, and direct actions.

## Scope
- Add a transient tray popup surface for the existing WinUI tray/daemon host.
- Keep `MainWindow` as the panel surface; implement the tray popup as a separate lightweight popup window.
- Open the popup from the existing tray icon path.
- Render daemon state, real Caddy runtime state, active entrypoint count, and active domain count from `GuiStateSnapshot`.
- Group visible domains under their entrypoint registration and show a source path or derived project name for each group.
- Show right-aligned domain toggles and update daemon state without closing the daemon.
- Expose `Open panel`, `Refresh` or `Reload`, and `Quit daemon` actions.
- Support keyboard navigation with Up, Down, Enter, Space, Escape, and focus-loss dismissal.
- Preserve light/dark/high-contrast-safe WinUI styling and stable UI automation identifiers.

## Key Files And Modules
- `src/Cadder.Tray.WinUI/DaemonTrayPresence.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `src/Cadder.Tray.WinUI/MainWindow.xaml`
- `src/Cadder.Tray.WinUI/MainWindow.xaml.cs`
- New popup files under `src/Cadder.Tray.WinUI`, likely `TrayPopupWindow.xaml`, `TrayPopupWindow.xaml.cs`, and a tray popup state/builder service.
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Contracts/IpcContracts.cs`
- `src/Cadder.Daemon/CadderIpcEndpoint.cs`
- `src/Cadder.Daemon/CaddyConfigCoordinator.cs`
- Existing tests under `tests/Cadder.Contracts.Tests` and `tests/Cadder.Daemon.Tests`; add focused tests where logic is testable without UI automation.
- OpenClaw references: `.local/examples/gui/docs/images/openclawwindows1.png`, `.local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/TrayMenuWindow.xaml`, `.local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/TrayMenuWindow.xaml.cs`, `.local/examples/gui/src/OpenClaw.Tray.WinUI/Services/TrayMenuStateBuilder.cs`.

## Implementation Steps
1. Add tray icon click plumbing in `DaemonTrayPresence`. Extend the existing `Shell_NotifyIcon` integration with a callback message, window message hook, and a raised event for left/right tray activation. Keep icon lifetime and disposal behavior intact.
2. Add a `TrayPopupWindow` as a separate transient WinUI window. Use OpenClaw's popup window pattern as the interaction reference: captionless/tool-window styling, work-area-aware placement near the cursor/tray, measured height with a vertical `ScrollViewer`, rounded popup region, focus-loss dismissal, and Escape dismissal.
3. Add pure popup positioning/sizing helpers or adapt the OpenClaw helper shape locally so monitor work-area and DPI math can be unit-tested without launching WinUI.
4. Add a tray popup state projection/builder layer. Map `GuiStateSnapshot` into compact rows for daemon state, runtime state, counts, entrypoint groups, domain rows, source path/project label, and diagnostics/flyout details. Keep this layer independent enough to unit-test with sample snapshots.
5. Compose popup UI using standard WinUI controls and theme resources. Use a compact brand header, status rows, grouped entrypoint/domain rows, right-aligned `ToggleSwitch` controls, separators, and flyout/detail rows matching the OpenClaw interaction model rather than inventing a new menu style.
6. Implement domain toggle behavior. Because there is no dedicated `ToggleDomainRequest`, use the existing daemon update path by sending an `UpdateEntrypointRequest` with the selected domain's `RegisteredDomain.ActivationState` changed while preserving other domains. Treat `ToggleEntrypointRequest` as entrypoint-level only unless the task scope is explicitly revised.
7. Wire direct actions: `Open panel` should show/activate `MainWindow`; `Refresh` should query the latest GUI state and rebuild the popup in place; `Reload` may reuse the existing update/apply path only if a clear daemon API exists, otherwise label it `Refresh`; `Quit daemon` should call the existing `App.QuitDaemonAsync` path.
8. Add keyboard behavior. Use a single key handler on the popup content root/list to move among enabled interactive rows with Up/Down, invoke focused buttons with Enter/Space, toggle focused switches with Space, and dismiss with Escape. Ensure focus-loss dismissal does not race with flyout interactions.
9. Add accessibility and automation IDs. Set `AutomationProperties.AutomationId` on popup root, action buttons, entrypoint rows, domain toggles, and status fields. Set accessible names for icon-only or compact controls and keep focus order logical.
10. Replace tight popup polling with an initial query plus explicit refresh. If feasible within scope, subscribe to GUI state changes while the popup is open and rebuild visible state on dispatcher updates; otherwise keep subscription as a follow-up candidate and ensure the visible refresh action works.
11. Keep `MainPage` and existing panel behavior stable. Do not redesign the full panel; TASK-1.9 owns panel overview and registry pages.

## Validation
- Add focused unit tests for popup state projection from `GuiStateSnapshot`, including zero registrations, one entrypoint with domains, multiple entrypoints, inactive domains, runtime idle/running/unhealthy states, and diagnostics.
- Add unit tests for popup positioning/sizing helpers if introduced.
- Add or extend daemon/config tests for per-domain activation via `UpdateEntrypointRequest` if existing coverage is insufficient for the exact tray toggle behavior.
- Run `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1` from the repository root.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- Launch the WinUI app and verify the popup visually with `winapp ui screenshot`. Use `winapp ui inspect`, `wait-for`, `invoke`, and keyboard interactions where the environment exposes child controls. If child-level UIA remains limited, record that limitation and keep screenshot evidence.
- Confirm the final working tree with `git status --short`.

## Risks And Notes
- Tray icon activation is the highest-risk platform boundary because the current `DaemonTrayPresence` only adds an icon and tooltip; it does not yet handle callback messages.
- Domain toggles are not backed by a dedicated domain-toggle contract. The planned path is to use `UpdateEntrypointRequest` with an updated domain array. If this is considered insufficient for acceptance criterion #3, pause and ask whether to add a new daemon contract.
- `winapp ui inspect` had child-level limitations on this project in prior WinUI verification. Do not claim child-level automation passed unless it actually works in this task.
- Popup focus-loss dismissal can race with flyout windows; use a short delayed foreground-window check similar to the OpenClaw reference.
- Avoid broad panel redesign, durable persistence, logs UI, installer/PATH work, or new Backlog scope. Those belong to other tasks.

## Assumptions
- `TASK-1.4` and `TASK-1.6` are complete and provide the IPC, GUI state, runtime state, and daemon lifecycle boundaries this popup consumes.
- The local machine can build and run the WinUI app with .NET 10, Windows App SDK, `winapp.exe`, and the existing x64/win-x64 project settings.
- Project-facing code, comments, task notes, and commit messages remain English; chat with the user remains Polish.

## Closeout Fix Plan
- Guard the domain toggle rollback path so programmatic `ToggleSwitch.IsOn` restoration does not re-enter `DomainToggle_Toggled` and send an inverse update request.
- Keep the change scoped to `TrayPopupWindow.xaml.cs`; do not alter daemon IPC contracts or popup layout.
- Re-run focused WinUI tray tests, WinUI build, and formatting verification after the fix. Re-run broader checks if the edit touches shared behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Planning context: `.local` exists and is listed in `.git/info/exclude`; TASK-1.4 and TASK-1.6 are Done; .NET 10.0.204, `winapp.exe`, and `caddy-real.exe` were observed as available. No code changes were made during planning.

Implemented tray icon callback plumbing via Shell_NotifyIcon callback messages and a Win32 window subclass. Added a reusable transient TrayPopupWindow that renders a compact Cadder tray surface with brand header, daemon/runtime/config summary, grouped entrypoint/domain rows, right-aligned domain ToggleSwitch controls, flyout detail rows, Open panel/Refresh/Quit daemon actions, keyboard navigation, Escape dismissal, and delayed focus-loss dismissal.

Added TrayPopupStateBuilder and TrayPopupPositioner as testable non-visual helpers. Added Cadder.Tray.WinUI.Tests with focused coverage for empty state, multiple entrypoint/domain grouping, inactive domain state, failed diagnostics, and work-area-aware popup positioning. Synchronized Cadder.slnx and build.ps1 with the new test project.

Domain toggles use the existing UpdateEntrypointRequest path by preserving the registration and replacing only the selected RegisteredDomain ActivationState. No new daemon IPC contract was added.

Validation run: dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64 passed 5/5 after formatting. dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64 passed 6/6. dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64 passed with 0 warnings. .\build.ps1 passed. dotnet format Cadder.slnx --verify-no-changes passed after applying dotnet format.

Validation limitation: dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj and dotnet test Cadder.slnx both failed only in CaddyfileConfigAdapterTests.ProcessAdapter_WithSmarketingReverseProxy_UsesCaddyAdaptAndExtractsExpectedDomains because the local external smarketing Caddyfile now adapts an extra crm.smarketing.localhost host. This failure is outside TASK-1.8 changes and depends on D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile.

WinUI smoke verification: launched Cadder.Tray.WinUI, triggered the tray callback message, confirmed a foreground Cadder tray popup window, captured .local/verification/task-1.8-tray-popup.png, verified focus-loss dismissal keeps the process alive, verified Down moves focus to Open panel via .local/verification/task-1.8-keyboard-down.png, verified Enter invokes Open panel, and verified Escape dismisses the popup while leaving the daemon process alive. winapp ui child-level inspect/wait-for returned stale_element for the transient popup, so child-level UIA assertions were not claimed.

Closeout validation rerun on 2026-06-09: `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 5/5; `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 6/6; `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed with 0 warnings; `.\build.ps1` passed; `dotnet format Cadder.slnx --verify-no-changes` exited 0 with a workspace-load warning. `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` still has the known single external-fixture failure: `CaddyfileConfigAdapterTests.ProcessAdapter_WithSmarketingReverseProxy_UsesCaddyAdaptAndExtractsExpectedDomains` now sees `crm.smarketing.localhost` from `D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile`. No TASK-1.8 code path appears involved in that failure.

Closeout review found a blocker after the task was briefly marked Done: the domain toggle failure path programmatically reverted `ToggleSwitch.IsOn`, which can re-enter `DomainToggle_Toggled` and send a second inverse update request. Reopened TASK-1.8 to fix and revalidate before final Done status.

Closeout blocker fixed: `TrayPopupWindow` now restores a rejected/failed domain toggle state by temporarily detaching `DomainToggle_Toggled` before setting `ToggleSwitch.IsOn`, preventing a reentrant inverse daemon update request. Post-fix validation: `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 5/5; `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed with 0 warnings; `dotnet format Cadder.slnx --verify-no-changes` exited 0 with a workspace-load warning; `.\build.ps1` passed. The known daemon external-fixture failure was not rerun after this WinUI-only fix; it remains recorded from closeout validation.

Post-closeout test fixture correction: the daemon Caddyfile adapter test no longer reads `D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile`. It now uses the repository-local fixture `tests\Cadder.Daemon.Tests\Fixtures\SmarketingReverseProxy.Caddyfile`, copied into the test output by `Cadder.Daemon.Tests.csproj`. Validation after the correction: `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 54/54; `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 65/65; `dotnet format Cadder.slnx --verify-no-changes` exited 0 with a workspace-load warning.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Superseded by the Rust cross-platform rewrite on 2026-06-10. The WinUI tray popup implementation was removed from the final codebase; future UI work lives in `cadder-tui` and related TUI tasks.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the Cadder tray popup as a transient WinUI window opened from the existing tray icon callback path. The popup renders daemon/runtime/config summary state, active entrypoint and domain counts, grouped entrypoint/domain rows with source labels, right-aligned domain toggles, detail flyouts, and direct Open panel, Refresh, and Quit daemon actions.

Domain toggles use the existing `UpdateEntrypointRequest` path, preserving the registration and replacing only the selected domain activation state, so no new daemon IPC contract was introduced. A closeout review found that rejected/failed toggle rollback could re-enter the `Toggled` handler and send an inverse update request; that blocker was fixed by restoring `ToggleSwitch.IsOn` with the handler temporarily detached. Added testable popup state and positioning helpers plus a new `Cadder.Tray.WinUI.Tests` project, and synchronized `Cadder.slnx` and `build.ps1` with that project.

Validation run for closeout: `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 5/5 after the final fix; `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 6/6 before the WinUI-only fix; `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed with 0 warnings after the final fix; `.\build.ps1` passed after the final fix; `dotnet format Cadder.slnx --verify-no-changes` exited 0 with a workspace-load warning after the final fix. WinUI smoke verification artifacts are stored under `.local/verification` and cover popup display, focus movement, Open panel invocation, Escape dismissal, and process survival.

Known remaining validation limitation: `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` and therefore the full solution test remain blocked by the existing external `smarketing` Caddyfile expectation mismatch, where the external file now adapts `crm.smarketing.localhost` in addition to the previously expected hosts. This is outside the TASK-1.8 tray popup scope.

Post-closeout correction: the previous daemon/full-solution validation limitation was resolved by moving the smarketing-style Caddyfile adapter test to a repository-local fixture instead of depending on `D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile`. After this correction, `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 54/54 and `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed 65/65.
<!-- SECTION:FINAL_SUMMARY:END -->
