---
id: TASK-1.10
title: Build per-domain logs TUI
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 12:48'
labels: []
dependencies:
  - TASK-1.7
references:
  - 'https://docs.rs/ratatui/latest/ratatui/'
  - 'https://github.com/ratatui/awesome-ratatui#-widgets'
modified_files:
  - crates/cadder-tui/src/model.rs
  - crates/cadder-tui/src/main.rs
  - crates/cadder-daemon/src/logs.rs
  - crates/cadder-daemon/src/state.rs
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: medium
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a log-focused Ratatui surface that lets users inspect domain-scoped Caddy logs from `cadder-tui` without requiring a Windows GUI. The surface should use the daemon's lazy log query IPC and remain responsive while registrations change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each domain row in `cadder-tui` exposes a View logs action or shortcut that opens a domain-scoped log view.
- [x] #2 The log view lazy-loads lines, tails new entries by default, and lets the user pause auto-scroll.
- [x] #3 Users can filter by severity and copy or export a redacted diagnostic excerpt through a cross-platform terminal-friendly path.
- [x] #4 The log view shows clear empty, loading, paused, stale, removed, and read-error states.
- [x] #5 The TUI remains responsive when logs update while domains are toggled or registrations are removed.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Build a domain-scoped logs surface in `cadder-tui` that opens from a selected domain row, uses the daemon's lazy log query IPC, tails new entries by default, supports pause/severity/export workflows, and remains responsive while registrations and domains change.

## Scope
- Implement the Ratatui log UX for domain streams in `crates/cadder-tui`.
- Use the existing `QueryLogsRequest` / `QueryLogsResponse` IPC contracts and `LogStreamIdentity` values already attached to registered domains.
- Store only redacted log excerpts in exported diagnostics by using `LogEntry.raw_message` returned from the daemon.
- Add focused daemon-side metadata fixes only where needed for the TUI to show correct status, cursor, or retention state.
- Do not broaden Caddy log parsing or durable log persistence unless implementation proves the current contract cannot satisfy the accepted UI behavior.

## Key Files And Modules
- `crates/cadder-tui/src/model.rs`
- `crates/cadder-tui/src/main.rs`
- `crates/cadder-protocol/src/lib.rs`
- `crates/cadder-daemon/src/logs.rs`
- `crates/cadder-daemon/src/state.rs`
- `crates/cadder-daemon/src/ipc.rs`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Add a dedicated log view model in `cadder-tui` for the active domain log target, stream identity, loaded entries, next cursor, loading/read-error state, stream status, paused/follow-tail mode, severity filter, and display/export metadata.
2. Add an explicit domain-row action in the Domains view, such as `Enter` or `l`, that selects the highlighted domain's `LogStreamIdentity`, resets log state for that target, switches to the Logs view, and performs the initial lazy query.
3. Change log refresh behavior to use the daemon cursor. The initial query loads a bounded page of recent entries; follow-up tail queries pass `next_cursor` and append only new entries. When paused, stop auto-scroll and avoid automatic tail refresh until resumed or manually refreshed.
4. Keep the TUI event loop responsive by moving IPC calls for state, toggles, and log refreshes into small background tasks with a response channel. Allow at most one in-flight log refresh for the current stream and surface timeout/read failures as log view state instead of blocking key handling.
5. Render clear log states in the Logs view: loading, empty, tailing, paused, stale, removed, read-error, retention gap, and truncated-by-retention. Include the active domain, severity filter, and tail/pause state in the title/status area.
6. Implement severity controls against the selected log stream. Changing severity should reset the log cursor and reload the current domain stream so old entries with the previous filter are not mixed with the new filter.
7. Implement a terminal-friendly diagnostic export path. Write a timestamped `cadder-logs-<domain>-<timestamp>.txt` excerpt in the current working directory containing timestamp, severity, domain/source metadata, and redacted raw message text. Treat file export as the accepted cross-platform copy/export path for this task.
8. Harden daemon log query metadata where needed. Add or correct `has_gap`, `has_more_before`, and `truncated_by_retention` behavior in `CaddyLogStore::query`, and verify `DaemonState::stream_is_active` returns statuses that let the TUI distinguish active, stale, removed, and empty streams.
9. Add focused tests for TUI model transitions, selected domain-to-log stream behavior, pause/tail cursor behavior, severity reset, export formatting, log store cursor/retention metadata, and daemon query status after domain removal.
10. Update `docs/ARCHITECTURE.md` with the per-domain logs TUI behavior, cursor-based tailing, pause/export semantics, and the boundary that diagnostic excerpts use daemon-redacted log messages.

## Validation
- Run `cargo fmt --check`.
- Run `cargo clippy --workspace --all-targets -- -D warnings`.
- Run `cargo test -p cadder-tui` after TUI model/render changes.
- Run `cargo test -p cadder-daemon` after log query/status changes.
- Run `cargo test --workspace`.
- Run `cargo run -p xtask -- check` before closeout.
- Manually smoke test `cadder-tui`: open Logs from a selected domain row, verify tailing, pause/resume, severity filtering, export, empty/stale/removed/read-error rendering, and responsiveness while toggling or unregistering domains.

## Assumptions
- File export satisfies the acceptance criterion for a cross-platform terminal-friendly copy/export path; no system clipboard dependency is required.
- The current protocol shape is sufficient for forward tailing. Backward pagination is not required unless the existing UI behavior cannot satisfy lazy loading without it.
- The task does not require new Caddy JSON log attribution heuristics beyond the domain streams already exposed by the daemon.
- Exported excerpts must use already-redacted daemon log messages and must not reintroduce raw process arguments or secrets.

## Risks And Boundaries
- Awaiting IPC in the key handling loop can make the TUI feel stuck during daemon churn; background requests and single-flight log refreshes are required for acceptance criterion #5.
- Cursor and retention metadata are partially implemented today, so UI state may be misleading unless daemon query tests cover gap/truncation paths.
- A selected domain may disappear while the Logs view is active. The TUI should retain the last selected stream label, show a stale or removed state, and avoid silently falling back to an entrypoint or unrelated stream.
- Broad runtime log parsing, durable log persistence, and clipboard integration should be treated as follow-up work unless the accepted criteria cannot be met without them.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Task prepared for execution with status `In Progress` and assignee `@agent`.

Implemented the per-domain logs TUI surface. Domain rows open logs with Enter or l; the log view keeps a domain target, cursor, status, severity filter, pause state, retention flags, and redacted entries in the TUI model. Log refreshes, state refreshes, activation toggles, and shutdown requests now run as background IPC tasks with channel responses; stale log responses are ignored by request serial so an old response cannot clear the current request state. Export writes a timestamped cadder-logs-<domain>-<timestamp>.txt file in the current working directory using daemon-redacted LogEntry.raw_message content. Daemon log queries now report gap, more-before, and truncated-by-retention metadata, and inactive domain streams are reported stale when retained entries exist. Validation run: cargo fmt --check; cargo clippy --workspace --all-targets -- -D warnings; cargo test -p cadder-tui; cargo test -p cadder-daemon; cargo test --workspace; cargo run -p xtask -- check. Manual interactive cadder-tui smoke testing was not run in this environment.

Fresh-eyes review before commit found and fixed a log status regression: cursor tail queries with no new entries now preserve Active/Stale status when matching retained entries exist, instead of reporting Empty for a quiet poll. Added regression coverage in `logs::tests::active_tail_query_stays_active_when_no_new_entries_arrive`. Re-ran validation after the fix: cargo fmt --check; cargo clippy --workspace --all-targets -- -D warnings; cargo test -p cadder-daemon logs::tests::active_tail_query_stays_active_when_no_new_entries_arrive; cargo test -p cadder-daemon; cargo test --workspace; cargo run -p xtask -- check.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from WinUI panel logs to Ratatui logs as part of the Rust cross-platform rewrite.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the per-domain logs TUI for `cadder-tui`.

Changes:
- Added a stateful domain log model with selected domain target, cursor, loading/read-error status, pause/tail mode, severity filter, retention flags, and retained log entries.
- Added `Enter` / `l` domain-row shortcuts to open domain-scoped logs, cursor-based tail refresh, pause/resume, severity reset, and timestamped redacted text export.
- Moved TUI state refreshes, activation toggles, log refreshes, and shutdown IPC into background tasks with response channels so terminal input stays responsive; stale log responses are ignored by request serial.
- Improved daemon log query metadata for retention gaps, more-before, truncated-by-retention, and stale inactive domain streams.
- Documented the per-domain logs behavior and daemon-redacted export boundary in `docs/ARCHITECTURE.md`.

Validation:
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test -p cadder-tui`
- `cargo test -p cadder-daemon`
- `cargo test --workspace`
- `cargo run -p xtask -- check`

Not run:
- Manual interactive `cadder-tui` smoke test; the environment was non-interactive for terminal UI verification.

Fresh-eyes follow-up before commit fixed a daemon log status regression for quiet cursor tail polls and added regression coverage. Full validation was re-run after that fix.
<!-- SECTION:FINAL_SUMMARY:END -->
