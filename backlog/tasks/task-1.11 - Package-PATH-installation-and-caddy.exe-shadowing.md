---
id: TASK-1.11
title: Package PATH installation and caddy.exe shadowing
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
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
Package Cadder so a user can place it on PATH and intentionally shadow caddy.exe while still allowing Cadder to find the real Caddy binary. Include install, validation, and uninstall behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The package produces a Cadder daemon executable and a caddy.exe shim entrypoint suitable for adding to global PATH.
- [ ] #2 Cadder detects whether its caddy.exe shim is before the real Caddy binary on PATH and reports actionable status in diagnostics.
- [ ] #3 The real Caddy binary path can be configured, validated, and displayed without being overwritten by the shim path.
- [ ] #4 Uninstall or disable instructions remove the PATH shadowing without deleting user Caddy configs.
- [ ] #5 The installer or packaging script validates that caddy.exe run from the smarketing reverse-proxy folder reaches Cadder rather than a random binary.
<!-- AC:END -->
