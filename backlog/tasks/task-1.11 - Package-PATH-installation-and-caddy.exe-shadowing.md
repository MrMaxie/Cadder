---
id: TASK-1.11
title: Package cross-platform PATH installation and caddy shim shadowing
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 10:44'
labels: []
dependencies:
  - TASK-1.2
  - TASK-1.3
  - TASK-1.6
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\readme.md'
parent_task_id: TASK-1
priority: medium
ordinal: 12000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Package Cadder so users can install Rust binaries on PATH across Windows, Linux, and macOS. The installed `caddy` shim must intentionally shadow real Caddy for managed `caddy run` flows while the daemon resolves and executes a real Caddy binary without recursion.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Install outputs include `cadderd`, `cadder-tui`, and a PATH-facing `caddy` shim for the current platform.
- [ ] #2 The shim can start or connect to the per-user daemon and register `caddy run` invocations from arbitrary project directories.
- [ ] #3 Real Caddy resolution checks `CADDER_CADDY_REAL_COMMAND` first and otherwise searches PATH while excluding the installed Cadder shim path.
- [ ] #4 Unsupported Caddy commands are delegated to real Caddy only after recursion-safe resolution; otherwise they fail with a clear Cadder-owned message.
- [ ] #5 The install and verification workflow is not PowerShell-only and can run through Cargo/xtask on supported platforms.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-09 16:31
---
Future packaging context from user approval of TASK-1.5: the user's real global Caddy command is `caddy-real`/`caddy-real.exe`. Cadder's PATH-facing shim should be installed globally with Scoop using a command shape like `scoop shim add caddy "path_to_cadder_caddy.exe"`, while keeping the real Caddy command configurable and distinguishable from the shim.
---

author: @agent
created: 2026-06-10 10:44
---
Rebaselined from Windows packaging/PATH work to cross-platform Rust binary installation and shim shadowing.
---
<!-- COMMENTS:END -->
