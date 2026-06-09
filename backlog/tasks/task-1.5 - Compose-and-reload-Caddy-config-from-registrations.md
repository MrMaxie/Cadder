---
id: TASK-1.5
title: Compose and reload Caddy config from registrations
status: To Do
assignee: []
created_date: '2026-06-09 11:42'
labels: []
dependencies:
  - TASK-1.4
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
parent_task_id: TASK-1
priority: high
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Convert active registrations into one effective Caddy configuration and apply it to the real Caddy runtime. The composition must preserve each source instance as a group while producing a valid runtime config.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder extracts hostnames/domains from registered Caddyfiles and associates every domain with its source entrypoint instance.
- [ ] #2 The smarketing reverse-proxy Caddyfile registers api.smarketing.localhost, app.smarketing.localhost, mailbox.smarketing.localhost, and storage.smarketing.localhost as domains from one instance.
- [ ] #3 Disabling a domain removes or neutralizes only that domain from the effective config while preserving the rest of the instance.
- [ ] #4 Conflicting domains across instances are detected and reported with source paths before reload.
- [ ] #5 Invalid composed config does not replace the last known good running Caddy config.
- [ ] #6 Successful changes reload the real Caddy runtime without restarting unrelated shim processes.
<!-- AC:END -->
