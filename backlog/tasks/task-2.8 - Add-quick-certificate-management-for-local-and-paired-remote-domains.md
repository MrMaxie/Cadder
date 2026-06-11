---
id: TASK-2.8
title: Add quick certificate management for local and paired remote domains
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
ordinal: 26800
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add quick certificate management for Cadder-managed local and paired remote domains. Operators should be able to inspect certificate readiness, expiry, issuer/source, and errors, then trigger safe certificate-related actions through the daemon that owns the domain without copying remote private keys to the local machine.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Management clients can show certificate readiness, expiry, issuer/source, renewal or provisioning status, and recent certificate errors for local and paired remote domains.
- [ ] #2 Certificate actions are routed through the daemon that owns the selected domain, and remote private keys or secret challenge material are not exposed to the local daemon or UI state.
- [ ] #3 Quick certificate actions support both local domains and paired remote domains when the owning daemon advertises the required capability.
- [ ] #4 Unsupported certificate capabilities, authorization failures, ACME/DNS challenge failures, and unreachable owning daemons produce typed diagnostics visible to operators.
- [ ] #5 Certificate status integrates with aggregated local/remote entity views without hiding the domain's owning daemon provenance.
- [ ] #6 Automated tests cover local status inspection, remote status inspection through a fake paired daemon, safe action routing, secret redaction, unsupported capability, failed authorization, and provider failure diagnostics.
- [ ] #7 Architecture and user documentation describe certificate status fields, supported quick actions, local versus remote ownership boundaries, and safe operational expectations.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
