---
id: TASK-1.24
title: Support mixed-elevation operations with minimal admin prompting
status: To Do
assignee: []
created_date: '2026-06-11 13:59'
labels:
  - windows
  - iis
  - elevation
  - pre-1.0
milestone: v1.0
dependencies:
  - TASK-1.21
references:
  - crates/cadder-protocol/src/lib.rs
  - crates/cadder-daemon/src/iis.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
documentation:
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - docs/site/src/content/docs/guides/windows.mdx
modified_files:
  - crates/cadder-protocol/src/lib.rs
  - crates/cadder-daemon/src/iis.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - docs/site/src/content/docs/guides/windows.mdx
parent_task_id: TASK-1
priority: high
ordinal: 24000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Allow Cadder to handle workflows where only part of an operation requires administrator privileges, especially Windows IIS handoff changes. Non-privileged daemon, TUI, and registration work should continue in the normal user context, while privileged sub-operations are isolated, batched where safe, and executed through an OS-appropriate admin prompt only when needed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder can represent operation steps that require different privilege levels and can execute the non-admin portion without forcing the whole workflow to run elevated.
- [ ] #2 Windows IIS operations that require administrator privileges request elevation through an OS-appropriate prompt for the smallest practical privileged subset, while non-IIS or read-only/user-level work remains unelevated.
- [ ] #3 Mixed-elevation workflows report which steps succeeded, which steps require elevation, which privileged steps were approved or denied, and which follow-up rollback or retry actions are available.
- [ ] #4 Privileged operations are batched only when doing so reduces prompts without broadening the set of actions performed as administrator beyond what the user requested.
- [ ] #5 The implementation prevents silent privilege escalation: the TUI and daemon surface the reason for admin access before requesting it, and denial leaves existing user-level operations usable.
- [ ] #6 Cross-platform behavior is explicit: non-Windows builds compile and test without Windows-only elevation dependencies, and unsupported elevation flows return typed unsupported responses rather than panicking.
- [ ] #7 Automated tests cover privilege classification, partial success/failure reporting, admin-denied handling, and non-Windows fallback behavior using fakes instead of requiring elevated CI.
- [ ] #8 Windows documentation and the TUI smoke checklist explain mixed-elevation behavior, expected prompts, denial behavior, and how IIS handoff is applied in privileged and non-privileged batches.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
