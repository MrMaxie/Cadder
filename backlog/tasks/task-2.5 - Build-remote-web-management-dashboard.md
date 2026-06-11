---
id: TASK-2.5
title: Build remote web management dashboard
status: To Do
assignee: []
created_date: '2026-06-11 16:35'
labels: []
milestone: m-3
dependencies:
  - TASK-2.1
  - TASK-1.18
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-2
priority: medium
ordinal: 26500
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build a browser-based management dashboard equivalent to the TUI for remote-friendly Cadder operation. The web surface should connect to `cadderd`, expose the core management workflows, and be safe by default through loopback restriction or authentication unless remote access is explicitly enabled.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder provides a web management dashboard that can display daemon status, entrypoints, domains, diagnostics, logs, and activation controls comparable to the TUI's core workflows.
- [ ] #2 The dashboard is loopback-restricted or authenticated by default, and any remotely reachable mode requires explicit user configuration.
- [ ] #3 The web UI receives state updates from the daemon without requiring a full page reload for normal management workflows.
- [ ] #4 Remote daemon pairing status and connection failures are represented clearly enough for operators to diagnose unreachable or unauthorized remotes.
- [ ] #5 Browser-based tests cover initial load, state refresh/update behavior, activation controls, diagnostics display, restricted/unauthorized access, and remote-enable configuration behavior.
- [ ] #6 Architecture, security, and user documentation explain dashboard startup, default exposure, remote enablement, authentication or loopback restrictions, and operational risks.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
