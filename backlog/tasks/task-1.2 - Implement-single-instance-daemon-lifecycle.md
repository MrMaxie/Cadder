---
id: TASK-1.2
title: Implement single-instance daemon lifecycle
status: To Do
assignee: []
created_date: '2026-06-09 11:41'
labels: []
dependencies:
  - TASK-1.1
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
- [ ] #1 Starting Cadder when no daemon is running creates exactly one daemon process and tray presence.
- [ ] #2 Starting Cadder again while the daemon is running forwards intent to the existing daemon instead of creating a second daemon.
- [ ] #3 The daemon remains alive and visible in the tray when the registration count drops to zero.
- [ ] #4 The daemon exposes an explicit quit path that shuts down IPC, removes transient registrations, and stops any Cadder-owned Caddy runtime cleanly.
- [ ] #5 Single-instance protection handles stale locks or abandoned mutex state without permanently blocking startup.
<!-- AC:END -->
