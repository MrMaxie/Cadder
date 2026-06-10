---
id: TASK-1.10
title: Build per-domain logs TUI
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 10:44'
labels: []
dependencies:
  - TASK-1.7
references:
  - 'https://docs.rs/ratatui/latest/ratatui/'
  - 'https://github.com/ratatui/awesome-ratatui#-widgets'
parent_task_id: TASK-1
priority: medium
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a log-focused Ratatui surface that lets users inspect domain-scoped Caddy logs from `cadder-tui` without requiring a Windows GUI. The surface should use the daemon's lazy log query IPC and remain responsive while registrations change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each domain row in `cadder-tui` exposes a View logs action or shortcut that opens a domain-scoped log view.
- [ ] #2 The log view lazy-loads lines, tails new entries by default, and lets the user pause auto-scroll.
- [ ] #3 Users can filter by severity and copy or export a redacted diagnostic excerpt through a cross-platform terminal-friendly path.
- [ ] #4 The log view shows clear empty, loading, paused, stale, removed, and read-error states.
- [ ] #5 The TUI remains responsive when logs update while domains are toggled or registrations are removed.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from WinUI panel logs to Ratatui logs as part of the Rust cross-platform rewrite.
---
<!-- COMMENTS:END -->
