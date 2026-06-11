---
id: TASK-2.4
title: Register Cadder routes in Pi-hole DNS
status: To Do
assignee: []
created_date: '2026-06-11 16:35'
labels: []
milestone: m-3
dependencies:
  - TASK-2.1
  - TASK-2.2
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-2
priority: medium
ordinal: 26400
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add an optional Pi-hole registrar so Cadder-managed reverse-proxy routes can be reflected at the DNS layer. The registrar should map Cadder route hosts to the correct local or remote Caddy front door, clean up records when routes disappear, and report DNS-side issues without leaking credentials.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Users can configure an optional Pi-hole registrar for local or paired remote environments without requiring Pi-hole for normal Cadder operation.
- [ ] #2 Cadder can create or update DNS records for active managed route hosts so they resolve to the owning Caddy front door.
- [ ] #3 Cadder removes or disables DNS records when the corresponding managed route is removed or no longer active, without touching unrelated Pi-hole records.
- [ ] #4 DNS conflicts, authentication failures, unreachable Pi-hole instances, and unsupported registrar capabilities produce typed, redacted diagnostics visible to management clients.
- [ ] #5 Remote route DNS registration uses the route's owning daemon/provenance so local and remote front-door addresses are not mixed.
- [ ] #6 Automated tests cover record creation, update, cleanup, conflict handling, auth failure, unreachable registrar, redacted diagnostics, and local-vs-remote front-door selection using a fake registrar.
- [ ] #7 Architecture and user documentation describe Pi-hole setup, required credentials, DNS ownership boundaries, cleanup behavior, and safe failure modes.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
