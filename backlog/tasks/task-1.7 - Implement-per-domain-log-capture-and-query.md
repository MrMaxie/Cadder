---
id: TASK-1.7
title: Implement per-domain log capture and query
status: To Do
assignee: []
created_date: '2026-06-09 11:43'
labels: []
dependencies:
  - TASK-1.5
  - TASK-1.6
parent_task_id: TASK-1
priority: medium
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Capture and expose logs in a way the GUI can filter per domain. Logs should help the user understand which source instance and hostname produced a request or runtime error.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder stores or streams recent Caddy runtime logs with timestamps, severity, domain, source instance, and raw message fields.
- [ ] #2 Each registered domain can query its own recent log lines without loading all logs into the GUI upfront.
- [ ] #3 Log capture survives config reloads and clearly marks reload, validation, and runtime errors.
- [ ] #4 Sensitive values such as tokens, environment secrets, and full command arguments are redacted before logs are exposed in diagnostics or GUI views.
- [ ] #5 A bounded retention policy prevents unbounded disk or memory growth.
<!-- AC:END -->
