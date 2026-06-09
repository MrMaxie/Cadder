---
id: TASK-1.10
title: Build per-domain logs UI
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
labels: []
dependencies:
  - TASK-1.7
  - TASK-1.9
references:
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Pages/AgentEventsPage.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Pages/DebugPage.xaml
parent_task_id: TASK-1
priority: medium
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a log-focused GUI surface that lets users inspect domain-scoped Caddy logs from the panel without overwhelming the registry view.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each domain row exposes a View logs action that opens a domain-scoped log surface.
- [ ] #2 The log surface lazy-loads lines, tails new entries by default, and lets the user pause auto-scroll.
- [ ] #3 Users can filter by severity and copy selected lines or a redacted diagnostic excerpt.
- [ ] #4 The log UI shows clear empty, loading, paused, stale, and read-error states.
- [ ] #5 The UI remains responsive when logs update while domains are toggled or registrations are removed.
<!-- AC:END -->
