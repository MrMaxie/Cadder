---
id: TASK-2.2
title: Configure remote Caddy through paired daemons
status: To Do
assignee: []
created_date: '2026-06-11 16:34'
labels: []
milestone: m-3
dependencies:
  - TASK-2.1
  - TASK-1.5
  - TASK-1.6
  - TASK-1.11
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-2
priority: high
ordinal: 26200
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Allow Cadder to configure Caddy on a remote host through the remote host's paired `cadderd`, not by directly driving a remote Caddy Admin API from the local machine. This enables homelab setups where Caddy and hosted backend servers can live on different machines while preserving daemon ownership, diagnostics, and rollback boundaries.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A local Cadder client can register or update a route that is owned and applied by a paired remote `cadderd` with explicit remote provenance in state snapshots.
- [ ] #2 Remote route configuration supports homelab upstream targets where the reverse proxy and backend service are on different machines.
- [ ] #3 Remote Caddy apply failures are reported with typed diagnostics and preserve the remote daemon's last-known-good Caddy state.
- [ ] #4 Local-only Caddy configuration behavior remains unchanged when no remote target is selected.
- [ ] #5 Automated tests cover successful remote registration, remote apply failure, unreachable remote daemon, unsupported remote capability, and last-known-good preservation using fake daemons and fake Caddy boundaries.
- [ ] #6 Architecture and user documentation describe remote Caddy ownership, supported homelab routing shapes, diagnostics, and the boundary that direct remote Caddy Admin API control is not the primary v2.0 path.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
