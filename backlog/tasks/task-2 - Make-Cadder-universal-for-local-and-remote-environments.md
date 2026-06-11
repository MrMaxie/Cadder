---
id: TASK-2
title: Make Cadder universal for local and remote environments
status: To Do
assignee: []
created_date: '2026-06-11 16:34'
labels: []
milestone: m-3
dependencies: []
documentation:
  - docs/ARCHITECTURE.md
priority: high
ordinal: 26000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make Cadder suitable for both local development and homelab-style remote environments by introducing paired local and remote `cadderd` instances as the primary remote-management model. This v2.0 initiative tracks remote daemon pairing, remote Caddy coordination, port negotiation, optional DNS registration, web and desktop management surfaces, multi-daemon aggregation, and quick certificate management while preserving the existing local-first workflows.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder can model paired local and remote `cadderd` environments as the primary v2.0 remote-management topology.
- [ ] #2 Remote and local entities can be represented with clear ownership and provenance so management clients can route actions to the correct daemon.
- [ ] #3 Remote Caddy configuration, negotiated backend ports, DNS registration, UI aggregation, and certificate workflows are tracked as independent implementation tasks under this initiative.
- [ ] #4 Architecture and user documentation explain the local-only, paired remote, and mixed local/remote operating modes for v2.0 users.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
