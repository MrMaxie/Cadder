---
id: TASK-1.6
title: Manage real Caddy runtime and health
status: To Do
assignee: []
created_date: '2026-06-09 11:42'
labels: []
dependencies:
  - TASK-1.5
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
- [ ] #1 Cadder resolves the real Caddy executable without recursively invoking its own caddy.exe shim.
- [ ] #2 The daemon starts the real Caddy runtime when needed and tracks its process ID, admin endpoint, version, and health.
- [ ] #3 With zero active domains, Cadder reaches a defined idle runtime state that the tray and panel can display.
- [ ] #4 Runtime failures surface structured errors to the registry and GUI without crashing the daemon.
- [ ] #5 Quitting the daemon stops only Cadder-owned Caddy runtime processes and does not kill unrelated user Caddy processes.
<!-- AC:END -->
