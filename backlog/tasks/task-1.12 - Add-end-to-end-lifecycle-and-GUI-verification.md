---
id: TASK-1.12
title: Add end-to-end lifecycle and TUI verification
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 13:12'
labels: []
dependencies:
  - TASK-1.3
  - TASK-1.4
  - TASK-1.5
  - TASK-1.7
  - TASK-1.11
references:
  - 'https://docs.rs/ratatui/latest/ratatui/'
  - 'https://docs.rs/crossterm/latest/crossterm/'
documentation:
  - docs/verification/tui-smoke.md
modified_files:
  - crates/cadder-daemon/tests/ipc_lifecycle.rs
  - crates/cadder-tui/src/model.rs
  - docs/verification/tui-smoke.md
parent_task_id: TASK-1
priority: medium
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add automated and manual verification that proves the Rust Cadder implementation handles the intended cross-platform lifecycle: zero projects, one project, many projects, shim death, domain toggles, logs, daemon state, and Ratatui status views.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Automated tests cover zero, one, and at least ten concurrent shim registrations.
- [x] #2 Tests prove killing one shim removes only that shim-owned registration and preserves other active registrations.
- [x] #3 Tests cover Caddyfile parsing/adaptation and effective config reload behavior using fake Caddy fixtures, with optional ignored tests for a local real Caddy binary.
- [x] #4 Tests cover domain toggles, conflict reporting, runtime failure reporting, and per-domain log queries.
- [x] #5 A TUI smoke checklist or automation verifies overview, entrypoint grouping, domain toggles, log opening, diagnostics, and daemon shutdown flow on supported terminal backends.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add automated and manual verification that proves the Rust Cadder implementation handles the intended cross-platform lifecycle: zero projects, one project, many projects, shim death, domain toggles, logs, daemon state, and Ratatui status views.

## Scope
- Add verification only; do not change daemon, shim, or TUI behavior except for small testability refactors required by the tests.
- Use fake Caddy fixtures for deterministic automated tests. Optional real-Caddy checks may be added as ignored tests only.
- Cover daemon/shim lifecycle through IPC/session integration tests and TUI behavior through model/render tests plus a smoke checklist or lightweight automation.
- Keep coverage gates, CI release publishing, installer behavior, and documentation-site work outside this task.

## Key Files And Modules
- `crates/cadder-daemon/tests/ipc_lifecycle.rs`
- `crates/cadder-daemon/tests/fixtures/SmarketingReverseProxy.Caddyfile`
- `crates/cadder-daemon/src/ipc.rs`
- `crates/cadder-daemon/src/state.rs`
- `crates/cadder-daemon/src/caddy.rs`
- `crates/cadder-daemon/src/runtime.rs`
- `crates/cadder-daemon/src/logs.rs`
- `crates/cadder-protocol/src/lib.rs`
- `crates/cadder-shim/src/main.rs`
- `crates/cadder-tui/src/model.rs`
- `crates/cadder-tui/src/main.rs`
- `docs/ARCHITECTURE.md`
- New verification documentation such as `docs/verification/tui-smoke.md` if a manual TUI checklist is used.

## Implementation Steps
1. Refactor the IPC lifecycle test harness in `crates/cadder-daemon/tests/ipc_lifecycle.rs` only as much as needed to create reusable scenarios for zero, one, many registrations, separate IPC sessions, fake Caddy behavior, and controlled session shutdown.
2. Add dedicated lifecycle tests proving the daemon starts with an empty snapshot, a single shim registration can adapt/apply config, at least ten concurrent registrations stay stable, and closing one IPC session removes only the registration owned by that session while preserving other active registrations.
3. Extend the fake Caddy test double to support deterministic `adapt`, `run`, `reload`, and failure modes. Capture enough side effects to assert effective config reload behavior without requiring a locally installed real Caddy.
4. Add config/runtime tests for Caddyfile adaptation, effective config updates after domain toggles, successful reload, reload/runtime failure diagnostics (`runtime-apply-failed`), and runtime-control log entries.
5. Add tests for domain toggles, active-domain conflict reporting with source paths, per-domain log query status and cursor behavior, and retained/stale/removed stream handling where relevant.
6. Extend TUI verification around `crates/cadder-tui/src/model.rs` for overview counts, entrypoint/domain grouping, domain log target opening, severity filter reset, diagnostics state visibility, and daemon shutdown request state.
7. If practical with a small refactor, extract pure render or command-building helpers from `crates/cadder-tui/src/main.rs` into testable functions/modules. If full terminal automation would add too much complexity, add a manual TUI smoke checklist instead.
8. Add a TUI smoke checklist or automation covering overview, entrypoint grouping, domain toggles, log opening, diagnostics, and daemon shutdown flow on supported terminal backends.
9. Keep optional real-Caddy verification as `#[ignore]` tests or documented manual checks, never as required workspace tests.
10. Run focused checks after each slice, then full workspace validation before closeout.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- Focused: `cargo test -p cadder-daemon --test ipc_lifecycle`
- Focused: `cargo test -p cadder-tui`
- Optional packaging smoke if useful: `cargo run -p xtask -- dist --out .local/verification/task-1.12/dist`, with artifacts removed afterward unless intentionally retained under `.local`.

## Assumptions
- Acceptance criterion #5 can be satisfied by a clear manual smoke checklist unless lightweight terminal automation proves straightforward.
- Automated tests should not require a real local Caddy binary.
- Runtime failure reporting means asserting the daemon exposes config diagnostics and runtime-control logs for start/reload/apply failures; deeper runtime health polling is outside this task unless already present.

## Risks And Boundaries
- The current shim process waits for Ctrl+C, so process-level shim death tests may be heavier than IPC-session tests. Prefer direct `CadderSession` integration tests unless a process harness is clearly worth the added complexity.
- Avoid broad TUI restructuring. Only extract testable units when it directly supports the smoke/verification acceptance criteria.
- Do not silently expand scope into coverage gates, CI/CD, docs site generation, packaging installers, or real Caddy dependency requirements.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Task moved to In Progress and assigned to @agent.

Implemented IPC lifecycle coverage for zero, one, and ten concurrent shim registrations; session-disconnect cleanup for a single shim; fake Caddy adapt/run/reload side effects; domain toggles; conflict diagnostics; runtime reload failure diagnostics/control logs; and per-domain log query cursor/status behavior. Extended TUI model tests for overview/domain association/log metadata/diagnostics and added docs/verification/tui-smoke.md for manual terminal smoke verification. Validation passed: cargo fmt --check; cargo clippy --workspace --all-targets -- -D warnings; cargo test --workspace; cargo run -p xtask -- check.

Closeout review completed after final focused checks: cargo test -p cadder-daemon --test ipc_lifecycle; cargo test -p cadder-tui; git diff --check.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from GUI verification to Rust daemon/shim/TUI verification as part of the cross-platform rewrite.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
What changed:
- Added IPC lifecycle verification for empty state, single registration, ten concurrent registrations, session-disconnect cleanup, fake Caddy adapt/run/reload behavior, domain toggles, conflicts, runtime reload failures, and per-domain log queries.
- Extended TUI model coverage for overview counts, entrypoint/domain association, log response metadata, severity cursor resets, and diagnostics visibility.
- Added docs/verification/tui-smoke.md as the manual terminal smoke checklist for overview, entrypoint grouping, domain toggles, log opening, diagnostics, and daemon shutdown.

Why:
- The task required automated and manual verification that Cadder's Rust daemon/shim/TUI lifecycle behaves correctly across the intended cross-platform workflows without depending on a locally installed real Caddy.

Validation:
- cargo fmt --check
- cargo clippy --workspace --all-targets -- -D warnings
- cargo test --workspace
- cargo run -p xtask -- check
- cargo test -p cadder-daemon --test ipc_lifecycle
- cargo test -p cadder-tui
- git diff --check

Risks / follow-ups:
- Real interactive terminal smoke remains manual by checklist; automated terminal driving was intentionally left out of scope.
<!-- SECTION:FINAL_SUMMARY:END -->
