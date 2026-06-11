---
id: TASK-1.23
title: Let cadder-tui recover and start cadderd when the daemon is unavailable
status: To Do
assignee: []
created_date: '2026-06-11 13:59'
labels:
  - tui
  - daemon
  - pre-1.0
milestone: v1.0
dependencies:
  - TASK-1.20
references:
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
  - crates/cadderd/src/main.rs
documentation:
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
modified_files:
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
  - crates/cadderd/src/main.rs
  - docs/verification/tui-smoke.md
parent_task_id: TASK-1
priority: high
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make `cadder-tui` usable when `cadderd` is not running or cannot be reached. The TUI should start normally, show a clear daemon-unavailable state instead of exiting or behaving like the app is broken, and provide an in-TUI action to start the per-user daemon and reconnect. This is pre-1.0 polish for first-run and recovery workflows.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Starting `cadder-tui` without a running daemon opens the TUI successfully and shows an explicit daemon-unavailable state with no panic, terminal corruption, or immediate process exit.
- [ ] #2 The daemon-unavailable state distinguishes at least not-running, connection-failed, and start-failed outcomes when the underlying error can be determined safely.
- [ ] #3 The TUI exposes a clear action to start `cadderd` from inside the TUI, reports progress while the start/reconnect attempt is in flight, and refreshes normal state after a successful connection.
- [ ] #4 Daemon start uses the same per-user runtime/path assumptions as the shim and does not spawn recursive or duplicate daemon instances when one becomes available during the attempt.
- [ ] #5 If daemon start fails, the TUI remains usable and shows an actionable error while keeping retry and quit flows available.
- [ ] #6 Automated tests cover the TUI model/state transitions for unavailable, starting, connected, and failed daemon states without requiring a real daemon process.
- [ ] #7 Documentation or smoke verification notes describe the no-daemon startup path and the in-TUI daemon start flow.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
