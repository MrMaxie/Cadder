---
id: TASK-1.9
title: Build Cadder panel overview and registry pages
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:43'
updated_date: '2026-06-09 20:17'
labels: []
dependencies:
  - TASK-1.4
  - TASK-1.8
references:
  - .local/examples/gui/docs/images/openclawwindows2.png
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/HubWindow.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Pages/InstancesPage.xaml
modified_files:
  - src/Cadder.Tray.WinUI/MainWindow.xaml
  - src/Cadder.Tray.WinUI/MainWindow.xaml.cs
  - src/Cadder.Tray.WinUI/PanelState.cs
  - src/Cadder.Tray.WinUI/PanelStateStore.cs
  - src/Cadder.Tray.WinUI/Pages/PanelPageBase.cs
  - src/Cadder.Tray.WinUI/Pages/OverviewPage.cs
  - src/Cadder.Tray.WinUI/Pages/InstancesPage.cs
  - src/Cadder.Tray.WinUI/Pages/DomainsPage.cs
  - src/Cadder.Tray.WinUI/Pages/LogsPage.cs
  - src/Cadder.Tray.WinUI/Pages/SettingsPage.cs
  - src/Cadder.Tray.WinUI/Pages/DiagnosticsPage.cs
  - tests/Cadder.Tray.WinUI.Tests/PanelStateBuilderTests.cs
  - tests/Cadder.Tray.WinUI.Tests/PanelStateStoreTests.cs
  - src/Cadder.Tray.WinUI/MainPage.xaml
  - src/Cadder.Tray.WinUI/MainPage.xaml.cs
parent_task_id: TASK-1
priority: high
ordinal: 10000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build the main Cadder panel that shows the full daemon state. The panel should follow the OpenClaw-style Windows companion shell: searchable title bar, left navigation, overview card, and stacked cards with inline status.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Opening the panel from the tray shows a single window with a top daemon overview card and navigation for Overview, Instances, Domains, Logs, Settings, and Diagnostics.
- [x] #2 The Instances view renders one card per caddy.exe entrypoint process with project path, config path, process status, age, domain count, and activation summary.
- [x] #3 Domains are grouped by entrypoint instance and can be expanded to reveal hostname, upstream target, enabled state, conflict state, and last error.
- [x] #4 Search or filtering can find domains and source paths across all registered instances.
- [x] #5 Empty, loading, disconnected, stale owner, config conflict, and runtime error states are represented inline without modal interruptions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Build the main Cadder panel as the durable full-state companion window for the tray daemon. The panel should follow the OpenClaw Windows companion interaction pattern: searchable title bar, left navigation, overview card, stacked cards with inline status, and non-modal error/state representation.

## Scope
- Replace the current minimal `MainWindow` + `MainPage` panel with a single WinUI shell window that owns title-bar search, navigation, and shared panel state.
- Keep the existing tray popup integration point: `TrayPopupWindow` continues to use `App.ActivateMainWindow()` for `Open panel`; the popup itself is not redesigned in this task.
- Add navigation destinations for Overview, Instances, Domains, Logs, Settings, and Diagnostics.
- Render state from `GuiStateSnapshot` through a shared panel read model instead of page-local control mutation.
- Show full daemon/runtime/config/registration state inline, including empty, loading, disconnected, stale owner, config conflict, and runtime error states.
- Add local search/filtering for domains, source working directories, config paths, and page commands.
- Add minimal upstream-target data/projection support needed by the Domains acceptance criteria; if a target cannot be extracted reliably from the adapted Caddy config, show a clear inline fallback such as `Not detected` instead of inventing data.
- Keep full per-domain log capture/tailing out of scope because TASK-1.7 and TASK-1.10 own that backend and UI surface. TASK-1.9 only adds the Logs navigation destination and an inline unavailable/coming state.
- Keep durable settings, installer/PATH behavior, and packaging out of scope.

## Key Files And Modules
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `src/Cadder.Tray.WinUI/MainWindow.xaml`
- `src/Cadder.Tray.WinUI/MainWindow.xaml.cs`
- `src/Cadder.Tray.WinUI/MainPage.xaml`
- `src/Cadder.Tray.WinUI/MainPage.xaml.cs`
- New panel pages under `src/Cadder.Tray.WinUI/Pages`, likely `OverviewPage`, `InstancesPage`, `DomainsPage`, `LogsPage`, `SettingsPage`, and `DiagnosticsPage`.
- New shared panel state/projection code under `src/Cadder.Tray.WinUI`, likely `PanelState`, `PanelStateBuilder`, and a shell-level state store/service.
- `src/Cadder.Tray.WinUI/TrayPopupWindow.xaml.cs` only if the Open panel activation needs a tiny adjustment; avoid popup redesign.
- `src/Cadder.Contracts/DomainContracts.cs` and daemon config projection code if upstream target data must be exposed in `GuiStateSnapshot`.
- `src/Cadder.Daemon/CaddyJsonConfigComposer.cs`, `src/Cadder.Daemon/CaddyConfigCoordinator.cs`, and related tests if target extraction or domain diagnostics need contract support.
- `tests/Cadder.Tray.WinUI.Tests` for panel read-model tests, search/filter tests, and any non-visual helpers.
- `tests/Cadder.Contracts.Tests` and `tests/Cadder.Daemon.Tests` if shared DTOs or daemon projection behavior changes.
- OpenClaw references: `.local/examples/gui/docs/images/openclawwindows2.png`, `.local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/HubWindow.xaml`, `.local/examples/gui/src/OpenClaw.Tray.WinUI/Pages/InstancesPage.xaml`.

## Implementation Steps
1. Baseline the current shell and references. Keep `MainWindow` as the single durable panel window, preserve Mica/system backdrop and hide-on-close behavior, and use OpenClaw only for interaction patterns rather than product logic or dependencies.
2. Introduce a panel state read model. Build a pure `PanelStateBuilder` from `GuiStateSnapshot` that computes daemon summary, runtime/config summary, instance rows, domain rows, diagnostics, search records, active counts, age labels, stale-owner heuristics, and conflict/error badges.
3. Define the stale owner heuristic in the panel layer using `LastHeartbeatUtc` age relative to a deterministic clock/time provider. Do not add arbitrary daemon state unless the implementation discovers that a contract change is necessary.
4. Address upstream target data for domain rows. First inspect the existing adapted Caddy JSON/config pipeline; if target extraction is available or can be added cleanly, expose it through the read model/contract with focused tests. If not reliably extractable for every route, surface `Not detected` inline for those rows while still satisfying the visible field requirement.
5. Replace the shell in `MainWindow.xaml` with a title bar/search/navigation layout modeled after OpenClaw: a pane toggle button, Cadder title/icon, `AutoSuggestBox` search, right-side status indicators, `NavigationView` with Overview, Instances, Domains, Logs, Settings, and Diagnostics, and a `Frame` content host.
6. Move shell-level navigation and command search into `MainWindow.xaml.cs`. Keep route tags stable, synchronize `NavigationView.SelectedItem`, support keyboard search focus with Ctrl+E/Ctrl+K/Ctrl+F, and ensure search results can navigate to the correct page and apply a filter or selection.
7. Add shared panel state refresh/subscription. Prefer consuming the existing GUI state subscription model so Overview, Instances, Domains, and Diagnostics update from one shared store. If the direct in-process route cannot stream cleanly inside the WinUI host, use an initial query plus explicit refresh as a scoped fallback and record the limitation before execution continues.
8. Build `OverviewPage` with a top daemon overview card and stacked runtime/config/diagnostic cards. Use theme resources, status stripes/dots, compact metadata rows, and inline `InfoBar`/status rows for unavailable, config conflict, and runtime error states.
9. Build `InstancesPage` with one card per entrypoint registration. Each card should show project/source path, config path, owner process status/PID, age, domain count, activation summary, and inline diagnostics related to that registration. Keep card actions minimal and non-modal.
10. Build `DomainsPage` grouped by entrypoint instance. Each group can expand to domain rows showing hostname, upstream target, enabled/activation state, conflict state, and last error. Use conflict diagnostics matched by `DomainKey` and source paths; show runtime/global errors separately when they are not domain-scoped.
11. Build lightweight `LogsPage`, `SettingsPage`, and `DiagnosticsPage`. `LogsPage` should explain through inline state that domain log capture/query is not available until TASK-1.7/TASK-1.10. `SettingsPage` should avoid durable settings scope. `DiagnosticsPage` should summarize runtime/config diagnostics and provide copyable identifiers or messages only when already available from current state.
12. Add automation IDs and accessibility names for shell navigation, search, overview card, instance cards, domain group expanders, domain rows, inline state banners, and key actions. Ensure keyboard navigation works across nav, search, pages, expanders, and buttons.
13. Make responsive behavior explicit. Wide layout uses left navigation and centered content up to a readable max width; medium/narrow layout should reclaim content width, reduce padding, and avoid squeezed cards or overlapping title-bar controls.
14. Remove or retire the old `MainPage` only after replacement pages are wired. Avoid leaving duplicate polling UI paths or dead routes.
15. Keep implementation notes updated if the plan changes, especially around upstream target extraction or GUI state subscription fallback.

## Validation
- Add focused unit tests for `PanelStateBuilder` covering empty state, one instance, multiple instances, inactive domains, config conflict diagnostics, runtime errors, stale heartbeat owner heuristic, unknown/disconnected state, upstream target fallback, and search/filter matches for domains/source paths/config paths.
- Add contract and daemon tests if upstream target data is added to shared DTOs or daemon projection behavior.
- Run `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1` from the repository root.
- Run `dotnet format Cadder.slnx --verify-no-changes`.
- Launch the WinUI app and verify the panel with `winapp ui`: Open panel from tray popup, navigate every page, use title-bar search, expand domain groups, verify keyboard focus/order, and confirm inline states are visible without modal interruptions.
- Capture screenshots with `winapp ui screenshot` for Overview, Instances, Domains, Logs placeholder, Diagnostics, and at least one narrow window layout. Inspect screenshots for clipping, overlap, unintended scrollbars, unreadable text, and broken light/dark/high-contrast behavior where feasible.
- Run `git status --short` before closeout and keep unrelated files out of the task.

## Risks And Boundaries
- Upstream target is the main contract risk. Current `RegisteredDomain` does not carry target data, so execution must either add a small tested contract/projection extension or display a truthful fallback for routes where extraction is unsupported.
- Per-domain last error is only reliable when diagnostics are domain-scoped. Runtime/global diagnostics should not be falsely attached to individual domains.
- Stale owner is not an explicit daemon state today. Use a documented heartbeat-age heuristic unless a stronger contract becomes necessary and is approved.
- The existing panel polls every two seconds; a shared subscription-driven state store is preferable, but it must not destabilize daemon lifecycle or tray popup behavior.
- Do not expand Logs into TASK-1.10, Settings into durable configuration, or tray popup behavior into a redesign.
- OpenClaw references are interaction references only. Do not import OpenClaw product logic, dependencies, localization, or unrelated settings pages.

## Assumptions
- TASK-1.4 and TASK-1.8 remain complete and provide the IPC, GUI snapshot, owner metadata, tray popup, and Open panel integration this task builds on.
- TASK-1.7 and TASK-1.10 are still future work, so full per-domain log capture and tailing are outside this task.
- The local machine can build and run the WinUI project with .NET 10, Windows App SDK, x64/win-x64, and `winapp.exe`.
- Project-facing text, source comments, task notes, and commit messages remain English; chat with the user remains Polish.

Execution note: The final shell keeps the approved searchable NavigationView layout, but uses a ContentControl page host instead of Frame navigation because Frame navigation to the new WinUI page types produced native XAML crashes during local validation. The shared panel store currently uses initial query plus periodic refresh rather than a streaming subscription to keep the tray host lifecycle stable. UIA-based winapp traversal of the dynamic content tree also crashes inside Microsoft.UI.Xaml on this machine; visual verification used Win32 PrintWindow screenshots as a scoped fallback.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Planning context: `.local` exists and is listed in `.git/info/exclude`; TASK-1.4 and TASK-1.8 are Done; .NET 10.0.204 and winapp.exe were observed as available. No code changes were made during planning. A UI subagent reviewed the plan and highlighted contract risks around upstream target, stale owner, and per-domain last error; those risks are captured in the plan.

Implemented and tested the first execution slice: added a pure panel read model (`PanelStateBuilder`) for overview, instance rows, domain groups, diagnostics, stale-owner detection, and search records. Upstream targets remain a truthful UI fallback (`Not detected`) because the current shared domain contract does not expose route targets. Focused validation passed: `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` (11 passed).

Implemented the TASK-1.9 panel shell and registry pages: replaced the minimal MainPage with a searchable NavigationView shell, added Overview/Instances/Domains/Logs/Settings/Diagnostics pages, shared PanelStateStore, pure PanelStateBuilder read model, domain grouping/filter/search records, inline empty/loading/disconnected/stale/config/runtime states, and focused panel read-model tests. Upstream target remains a truthful `Not detected` fallback because the current shared domain contract does not expose route targets. Validation passed after formatting: `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` (71 passed), `./build.ps1` (0 warnings, 0 errors), `dotnet format Cadder.slnx --verify-no-changes`, and `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`. The app was launched with `winapp run --detach`; final visual fallback screenshot saved at `.local/verification/TASK-1.9/08-final-after-xaml-crlf-printwindow.png`. `winapp ui` element traversal remains blocked by a native WinUI crash during accessibility tree inspection, so the task should be reviewed/closed out with that limitation explicit.

Follow-up validation after investigating the UIA crash: reduced unnecessary page rerenders in `PanelStateStore` by suppressing non-material refresh notifications, added `PanelStateStoreTests`, and simplified panel pages to pure programmatic `UserControl` classes without empty XAML stubs. Focused WinUI tests now pass with 14 tests, and full solution validation passes with 74 tests. `winapp ui inspect -a <pid> --depth 3 --json` still returns `stale_element` and the WinUI process exits, so UIA traversal remains the only blocked validation path. Final visual fallback screenshot after these changes is `.local/verification/TASK-1.9/09-final-programmatic-pages-printwindow.png`.

Closeout validation on 2026-06-09: `dotnet test tests\Cadder.Tray.WinUI.Tests\Cadder.Tray.WinUI.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed (14/14), `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed (74/74), `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` passed with 0 warnings and 0 errors, `./build.ps1` passed, and `dotnet format Cadder.slnx --verify-no-changes` exited 0 with a generic workspace-loading warning. Visual/UIA limitation remains as previously recorded: `winapp ui inspect` destabilizes the WinUI process on this machine, so final UI validation relies on PrintWindow screenshots under `.local/verification/TASK-1.9/`, especially `09-final-programmatic-pages-printwindow.png`.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the main Cadder panel as a searchable WinUI NavigationView shell with Overview, Instances, Domains, Logs, Settings, and Diagnostics destinations. The old minimal MainPage was replaced by a shared panel read model and store that project daemon snapshots into overview cards, per-entrypoint cards, grouped domain rows, search records, inline diagnostics, and loading/disconnected/empty/stale/conflict/runtime states.

Instances now show project/source paths, config paths, owner process status, age/heartbeat labels, domain counts, activation summary, and scoped diagnostics. Domains are grouped per entrypoint and render hostname, upstream target fallback (`Not detected` when the shared contract has no route target), enabled state, conflict state, and last error. Logs and Settings intentionally remain scoped placeholders because log capture/tailing and durable settings belong to later tasks.

Validation passed on 2026-06-09: focused WinUI tests (14/14), full solution tests (74/74), explicit WinUI build (0 warnings, 0 errors), `./build.ps1`, and `dotnet format Cadder.slnx --verify-no-changes` (exit 0; generic workspace-loading warning only). Visual fallback screenshots exist under `.local/verification/TASK-1.9/`; UIA traversal with `winapp ui inspect` remains blocked by a native WinUI/process crash on this machine, so that limitation is documented rather than hidden.
<!-- SECTION:FINAL_SUMMARY:END -->
