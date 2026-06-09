---
id: TASK-1.9
title: Build Cadder panel overview and registry pages
status: To Do
assignee: []
created_date: '2026-06-09 11:43'
labels: []
dependencies:
  - TASK-1.4
  - TASK-1.8
references:
  - .local/examples/gui/docs/images/openclawwindows2.png
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/HubWindow.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Pages/InstancesPage.xaml
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
- [ ] #1 Opening the panel from the tray shows a single window with a top daemon overview card and navigation for Overview, Instances, Domains, Logs, Settings, and Diagnostics.
- [ ] #2 The Instances view renders one card per caddy.exe entrypoint process with project path, config path, process status, age, domain count, and activation summary.
- [ ] #3 Domains are grouped by entrypoint instance and can be expanded to reveal hostname, upstream target, enabled state, conflict state, and last error.
- [ ] #4 Search or filtering can find domains and source paths across all registered instances.
- [ ] #5 Empty, loading, disconnected, stale owner, config conflict, and runtime error states are represented inline without modal interruptions.
<!-- AC:END -->
