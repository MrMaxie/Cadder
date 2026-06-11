---
id: TASK-1.24
title: Support mixed-elevation operations with minimal admin prompting
status: Done
assignee:
  - '@Codex'
created_date: '2026-06-11 13:59'
updated_date: '2026-06-11 17:19'
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
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-tui/src/main.rs
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - docs/site/src/content/docs/guides/windows.mdx
  - docs/site/src/content/docs/cookbooks/windows/iis.mdx
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
- [x] #1 Cadder can represent operation steps that require different privilege levels and can execute the non-admin portion without forcing the whole workflow to run elevated.
- [x] #2 Windows IIS operations that require administrator privileges request elevation through an OS-appropriate prompt for the smallest practical privileged subset, while non-IIS or read-only/user-level work remains unelevated.
- [x] #3 Mixed-elevation workflows report which steps succeeded, which steps require elevation, which privileged steps were approved or denied, and which follow-up rollback or retry actions are available.
- [x] #4 Privileged operations are batched only when doing so reduces prompts without broadening the set of actions performed as administrator beyond what the user requested.
- [x] #5 The implementation prevents silent privilege escalation: the TUI and daemon surface the reason for admin access before requesting it, and denial leaves existing user-level operations usable.
- [x] #6 Cross-platform behavior is explicit: non-Windows builds compile and test without Windows-only elevation dependencies, and unsupported elevation flows return typed unsupported responses rather than panicking.
- [x] #7 Automated tests cover privilege classification, partial success/failure reporting, admin-denied handling, and non-Windows fallback behavior using fakes instead of requiring elevated CI.
- [x] #8 Windows documentation and the TUI smoke checklist explain mixed-elevation behavior, expected prompts, denial behavior, and how IIS handoff is applied in privileged and non-privileged batches.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add a low-risk mixed-elevation model for Windows IIS handoff so Cadder can keep daemon, TUI, registration, Caddy config planning, and non-privileged work in the normal user context while isolating only IIS mutation steps behind an explicit Windows elevation prompt. Non-Windows behavior must remain typed and testable without Windows-only dependencies.

## Scope
- Extend the existing IIS handoff flow rather than replacing it.
- Keep the public TUI flow synchronous from the user's perspective, but make operation results step-aware.
- Keep privileged execution limited to IIS binding add/remove/restore operations.
- Do not require elevated CI or real IIS for automated tests.
- Do not introduce OS service installation, long-running privileged daemons, or broad privilege escalation.

## Key Files
- `crates/cadder-protocol/src/lib.rs`
- `crates/cadder-daemon/src/iis.rs`
- `crates/cadder-daemon/src/state.rs`
- `crates/cadder-daemon/src/ipc.rs`
- `crates/cadder-tui/src/main.rs`
- `crates/cadder-tui/src/model.rs`
- `docs/ARCHITECTURE.md`
- `docs/verification/tui-smoke.md`
- `docs/site/src/content/docs/guides/windows.mdx`
- `docs/site/src/content/docs/cookbooks/windows/iis.mdx`

## Implementation Steps
1. Extend protocol DTOs with typed mixed-elevation concepts: operation step id, privilege level, step status, approval outcome, rollback/retry action, and `ElevationRequired` / `ElevationDenied` style issue kinds.
2. Refactor `DaemonState::set_iis_handoff` into a small planner plus executor: preflight/discovery/conflict checks stay unelevated; IIS add/remove/restore steps are marked privileged; Caddy route updates and metadata handling remain normal user operations.
3. Add an `ElevationBroker` abstraction inside the daemon/IIS layer. Provide fake implementations for tests, direct unsupported behavior for non-Windows, and a Windows implementation that launches the smallest privileged batch through an OS-appropriate prompt.
4. Batch only adjacent IIS mutations that belong to the selected handoff/restore request, such as creating the loopback binding and removing the public binding. Do not include Caddy config apply, registration changes, discovery, or unrelated IIS rows in the elevated batch.
5. Preserve current rollback guarantees: write restore metadata before privileged mutation, report partial success, keep metadata when rollback/restore fails, and expose retry/rollback availability in the response.
6. Update TUI model/rendering to show why admin access is requested before dispatch, then show approved/denied/partial results without treating denial as a fatal daemon state.
7. Keep non-Windows explicit: daemon IPC returns typed unsupported/elevation-unavailable responses; non-Windows TUI still omits the IIS view; cross-target builds must not pull Windows-only elevation APIs.
8. Expand fake-provider tests for privilege classification, admin approval, admin denial, partial success, rollback/retry reporting, and non-Windows fallback.
9. Update architecture, Windows guide/cookbook, and TUI smoke checklist to describe normal-user operation, expected prompts, denial behavior, batching boundaries, and IIS handoff recovery.

## Validation
- `cargo fmt --check`
- `cargo test -p cadder-protocol`
- focused `cadder-daemon` IIS/elevation tests
- focused `cadder-tui` IIS view/model tests
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage`

## Assumptions
- Windows elevation can be implemented as a short-lived helper/prompt path owned by daemon/IIS code, not as an always-elevated daemon.
- User denial is a normal outcome, not an error that should disable TUI or daemon operations.
- Existing fake IIS provider should be expanded rather than replaced.

## Risks And Boundaries
- Highest risk is preserving ordering across metadata, IIS mutation, Caddy apply, and rollback. Keep the transaction shape close to the existing `enable_iis_handoff` / `disable_iis_handoff` flow.
- Avoid changing shim registration, Caddy runtime ownership, or daemon startup semantics.
- Avoid string-only status parsing; all user-visible mixed-elevation outcomes should come from typed protocol data.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation.

Implemented mixed-elevation IIS handoff support with typed protocol operation steps, privilege levels, approval outcomes, step statuses, and follow-up actions. IIS discovery, metadata, Caddy route updates, registration state, and TUI operation remain user-level; only IIS add/remove/restore binding mutations are sent through the privileged batch path. Added Windows UAC-based batch execution for system IIS mutations, fake-provider approval/denial/unsupported behavior for tests, TUI pre-dispatch reason text, and response summaries for approved/denied/failed steps. Updated architecture, Windows guide/cookbook, and TUI smoke checklist for normal-user operation, prompt boundaries, denial behavior, batching, rollback, and retry guidance. Validation passed: cargo fmt --check; cargo test -p cadder-protocol; focused cadder-daemon IIS/elevation tests; focused cadder-tui tests; cargo clippy --workspace --all-targets -- -D warnings; cargo test --workspace; cargo check --workspace --target x86_64-unknown-linux-gnu; cargo run -p xtask -- check; cargo run -p xtask -- coverage. Final line coverage: 86.35%, above the 85% threshold.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
## Summary
- Added typed mixed-elevation IIS operation reporting to the protocol: step ids, privilege levels, statuses, approval outcomes, issues, and follow-up actions.
- Routed IIS binding add/remove/restore mutations through a privileged batch path while keeping discovery, restore metadata, Caddy route application, registration state, daemon, and TUI behavior in the normal user context.
- Added fake-provider coverage for admin approval, admin denial, unsupported elevation, rollback/retry reporting, and partial restore failure paths without requiring real IIS or elevated CI.
- Updated the TUI to explain why administrator approval may be requested before dispatch and to summarize approved, denied, failed, and follow-up operation steps after the daemon response.
- Updated architecture, Windows guide/cookbook, and TUI smoke checklist to document mixed-elevation behavior, expected prompts, denial handling, batching boundaries, and recovery.

## Validation
- `cargo fmt --check`
- `cargo test -p cadder-protocol`
- focused `cargo test -p cadder-daemon iis_`
- focused `cargo test -p cadder-tui`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo check --workspace --target x86_64-unknown-linux-gnu`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage` with 86.35% line coverage

## Risk
- Real Windows UAC/IIS mutation behavior still needs the documented manual smoke on a disposable IIS binding; automated tests use fake providers by design.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
