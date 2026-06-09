---
id: TASK-1.3
title: Implement the caddy.exe shim registration flow
status: To Do
assignee: []
created_date: '2026-06-09 11:42'
labels: []
dependencies:
  - TASK-1.1
  - TASK-1.2
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\package.json'
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
parent_task_id: TASK-1
priority: high
ordinal: 4000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the PATH-facing caddy.exe shim that project scripts invoke. The shim should start or connect to the singleton daemon, register the invoking Caddy configuration, and keep that registration alive only for the lifetime of the shim process.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Running caddy.exe run from a project directory registers that directory and its Caddyfile with the daemon.
- [ ] #2 The shim supports explicit Caddy config and adapter flags needed by caddy run, including --config and --adapter.
- [ ] #3 If the daemon is not running, the shim starts it and waits until IPC is ready before registering.
- [ ] #4 The shim keeps the registration alive until normal exit, Ctrl+C, parent terminal close, or process termination is detected by the daemon.
- [ ] #5 Unsupported Caddy commands either delegate to the real Caddy binary or fail with a clear message that names the supported Cadder command set.
<!-- AC:END -->
