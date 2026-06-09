---
id: TASK-1.4
title: Build IPC and owner-aware registration store
status: To Do
assignee: []
created_date: '2026-06-09 11:42'
labels: []
dependencies:
  - TASK-1.2
  - TASK-1.3
parent_task_id: TASK-1
priority: high
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build the daemon-side IPC and owner-aware registration store used by the shim and GUI. Registrations are grouped by entrypoint process and removed automatically when their owning shim dies.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The IPC API can register, update, list, toggle, and unregister entrypoint-owned Caddy configs.
- [ ] #2 Each registration records owner process ID, process start identity, executable path, working directory, config path, command line, created time, and last heartbeat.
- [ ] #3 A daemon-side watcher removes only the registrations owned by a dead or disconnected shim.
- [ ] #4 The registry supports zero, one, and at least ten simultaneous registrations without state corruption.
- [ ] #5 The GUI can subscribe to state changes without polling tight loops.
<!-- AC:END -->
