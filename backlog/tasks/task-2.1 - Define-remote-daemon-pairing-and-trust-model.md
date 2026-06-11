---
id: TASK-2.1
title: Define remote daemon pairing and trust model
status: To Do
assignee: []
created_date: '2026-06-11 16:34'
labels: []
milestone: m-3
dependencies:
  - TASK-1.18
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-2
priority: high
ordinal: 26100
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Define the v2.0 foundation for paired local and remote `cadderd` instances. Cadder needs durable profiles for remote daemon endpoints, trust material, daemon identity, capabilities, health, and provenance so later remote Caddy, DNS, certificate, and UI work can share one safe remote-management model.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Users can create, list, inspect, and remove paired remote daemon profiles without exposing secret material in state snapshots, logs, or diagnostics.
- [ ] #2 The shared protocol can represent local and remote daemon identity, endpoint, trust state, capabilities, health, and last-seen status with clear provenance.
- [ ] #3 Management clients receive typed outcomes for reachable, unreachable, unauthorized, untrusted, version-incompatible, and unsupported-capability remote daemons.
- [ ] #4 Remote pairing is explicit and opt-in; existing local-only daemon and TUI workflows continue to work without remote configuration.
- [ ] #5 Automated tests cover profile persistence, secret redaction, trust-state transitions, capability negotiation, unreachable and unauthorized remotes, and version mismatch behavior using fakes.
- [ ] #6 Architecture and user documentation explain the paired `cadderd` model, trust boundary, local-only fallback, and operator setup flow.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
