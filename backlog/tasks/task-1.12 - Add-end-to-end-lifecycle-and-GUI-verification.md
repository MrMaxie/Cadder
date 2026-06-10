---
id: TASK-1.12
title: Add end-to-end lifecycle and TUI verification
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 10:44'
labels: []
dependencies:
  - TASK-1.3
  - TASK-1.4
  - TASK-1.5
  - TASK-1.7
  - TASK-1.11
references:
  - 'https://docs.rs/ratatui/latest/ratatui/'
  - 'https://docs.rs/crossterm/latest/crossterm/'
parent_task_id: TASK-1
priority: medium
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add automated and manual verification that proves the Rust Cadder implementation handles the intended cross-platform lifecycle: zero projects, one project, many projects, shim death, domain toggles, logs, daemon state, and Ratatui status views.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Automated tests cover zero, one, and at least ten concurrent shim registrations.
- [ ] #2 Tests prove killing one shim removes only that shim-owned registration and preserves other active registrations.
- [ ] #3 Tests cover Caddyfile parsing/adaptation and effective config reload behavior using fake Caddy fixtures, with optional ignored tests for a local real Caddy binary.
- [ ] #4 Tests cover domain toggles, conflict reporting, runtime failure reporting, and per-domain log queries.
- [ ] #5 A TUI smoke checklist or automation verifies overview, entrypoint grouping, domain toggles, log opening, diagnostics, and daemon shutdown flow on supported terminal backends.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from GUI verification to Rust daemon/shim/TUI verification as part of the cross-platform rewrite.
---
<!-- COMMENTS:END -->
