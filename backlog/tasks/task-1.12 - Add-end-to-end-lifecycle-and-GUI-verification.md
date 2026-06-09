---
id: TASK-1.12
title: Add end-to-end lifecycle and GUI verification
status: To Do
assignee: []
created_date: '2026-06-09 11:44'
labels: []
dependencies:
  - TASK-1.3
  - TASK-1.4
  - TASK-1.5
  - TASK-1.8
  - TASK-1.9
  - TASK-1.11
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
  - .local/examples/gui/docs/images/openclawwindows1.png
  - .local/examples/gui/docs/images/openclawwindows2.png
parent_task_id: TASK-1
priority: medium
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add automated and manual verification that proves Cadder handles the intended lifecycle: zero projects, one project, many projects, shim death, domain toggles, logs, and GUI status.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Automated tests cover zero, one, and at least ten concurrent shim registrations.
- [ ] #2 Tests prove killing one shim removes only that shim owned registration and preserves other active registrations.
- [ ] #3 Tests cover smarketing-style Caddyfile parsing and effective config reload behavior.
- [ ] #4 Tests cover domain toggles, conflict reporting, runtime failure reporting, and per-domain log queries.
- [ ] #5 A GUI smoke checklist or automation verifies tray popup, panel overview, instance grouping, toggles, log opening, and quit daemon flow.
<!-- AC:END -->
