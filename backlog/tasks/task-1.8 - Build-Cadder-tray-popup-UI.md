---
id: TASK-1.8
title: Build Cadder tray popup UI
status: To Do
assignee: []
created_date: '2026-06-09 11:43'
labels: []
dependencies:
  - TASK-1.4
  - TASK-1.6
references:
  - .local/examples/gui/docs/images/openclawwindows1.png
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/TrayMenuWindow.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Services/TrayMenuStateBuilder.cs
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
- [ ] #1 The tray popup opens from the tray icon and displays daemon state, Caddy runtime state, active entrypoint count, and active domain count.
- [ ] #2 The popup groups visible domains under their entrypoint instance and shows source path or project name for each group.
- [ ] #3 Domain toggles are visible in the popup and call the daemon toggle API without closing the daemon.
- [ ] #4 The popup exposes Open panel, reload or refresh, and Quit daemon actions.
- [ ] #5 Keyboard navigation works with Up, Down, Enter, Space, Escape, and focus-loss dismissal.
<!-- AC:END -->
