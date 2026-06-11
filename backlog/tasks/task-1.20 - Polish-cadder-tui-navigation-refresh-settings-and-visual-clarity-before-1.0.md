---
id: TASK-1.20
title: 'Polish cadder-tui navigation, refresh, settings, and visual clarity before 1.0'
status: Done
assignee:
  - '@agent'
created_date: '2026-06-11 07:43'
updated_date: '2026-06-11 08:22'
labels:
  - tui
  - usability
  - pre-1.0
milestone: v1.0
dependencies:
  - TASK-1.12
references:
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
documentation:
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - 'https://docs.rs/ratatui/latest/ratatui/widgets/struct.Table.html'
  - 'https://ratatui.rs/examples/style/colors/'
  - 'https://ratatui.rs/highlights/v026/'
  - >-
    https://raw.githubusercontent.com/jesseduffield/lazygit/master/docs/Config.md
  - 'https://textual.textualize.io/guide/CSS/'
modified_files:
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
  - docs/ARCHITECTURE.md
  - docs/site/src/content/docs/guides/tui-diagnostics.mdx
  - docs/verification/tui-smoke.md
parent_task_id: TASK-1
priority: high
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Improve the current Ratatui terminal UI so it is suitable for normal pre-1.0 usage rather than only smoke verification. The TUI should keep daemon state fresh without requiring manual refresh for routine changes, support both Tab-based and left/right view navigation, make the selected Domains row and activation state obvious, move log severity selection into a Settings view, and use a more intentional color system inspired by established terminal UI patterns. Keep the work scoped to `cadder-tui` usability unless a small protocol/model change is required to support the UI cleanly.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The TUI automatically refreshes daemon state snapshots on a reasonable interval while preserving explicit `r` as a manual refresh command and avoiding overlapping state requests.
- [x] #2 Top-level view navigation works with both `Tab`/`Shift+Tab` and left/right arrow keys, with tests covering wraparound and selection preservation.
- [x] #3 The Domains view makes the selected row visibly distinct using Ratatui table selection styling or an equivalent high-contrast marker, and enabled versus disabled domains are distinguishable by both color and text markers such as `[x]` and `[ ]`.
- [x] #4 The log severity filter is controlled from a Settings view where the user can choose the level with up/down navigation and apply it without relying on the `i`/`w`/`e`/`0` footer shortcut model as the primary interaction.
- [x] #5 The TUI uses a cohesive, higher-contrast color palette across tabs, status summaries, entrypoints, domains, logs, diagnostics, selected rows, disabled states, warnings, and errors while remaining legible on common Windows Terminal, macOS, and Linux terminal themes.
- [x] #6 The footer/help text reflects the new navigation and Settings interactions without advertising obsolete severity shortcuts as the main control path.
- [x] #7 Automated tests cover the new refresh timing, keyboard navigation, Settings severity selection, Domains selection state, and enabled/disabled styling decisions where they can be asserted without a live terminal.
- [x] #8 The manual TUI smoke checklist and user-facing documentation are updated to describe auto-refresh, left/right navigation, Settings severity selection, and the visual state conventions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Polish `cadder-tui` so it is usable for normal pre-1.0 operation: daemon state refreshes automatically, top-level navigation supports both tab and left/right flows, Domains selection and activation state are visually obvious, log severity is controlled from Settings, and the UI uses one cohesive high-contrast terminal color system.

## Scope
- Keep the work focused on `crates/cadder-tui` usability and documentation.
- Avoid daemon/protocol changes unless a small model contract change is clearly required to support the UI cleanly.
- Preserve existing manual commands where useful, especially `r` for explicit state refresh and log controls that are not being replaced.
- Do not expand into CI, packaging, release, or unrelated daemon behavior.

## Key Files And Modules
- `crates/cadder-tui/src/model.rs`
- `crates/cadder-tui/src/main.rs`
- `crates/cadder-tui/Cargo.toml` only if a justified test/helper dependency is needed through Cargo CLI
- `docs/verification/tui-smoke.md`
- `docs/site/src/content/docs/guides/tui-diagnostics.mdx`
- `docs/ARCHITECTURE.md` only if durable architecture notes need a small update

## Implementation Steps
1. Extend the TUI model with a `Settings` top-level view and a small settings model for log severity choices (`All`, `Info`, `Warn`, `Error`). Keep the existing log severity state as the source of truth for log queries.
2. Rework top-level navigation so `Tab`/`Shift+Tab` and `Left`/`Right` share the same wraparound behavior. Preserve per-view selection where it matters instead of resetting row selection on every view change, and clamp selection after snapshot/filter changes.
3. Add state auto-refresh timing to the app loop with a reasonable interval, while keeping manual `r` refresh. Reuse `state_request_in_flight` to prevent overlapping state requests and make the timing logic small enough to unit-test.
4. Implement the Settings view as the primary severity UI: use up/down to choose the severity row and `Enter` or `Space` to apply it through the existing `set_log_severity` path so log cursor and entries reset consistently.
5. Improve Domains rendering by using Ratatui table selection styling or an equivalent high-contrast row marker, plus explicit activation text markers such as `[x] Active` and `[ ] Disabled`. Style enabled and disabled domains differently, but do not rely on color alone.
6. Introduce a cohesive color/style helper for tabs, blocks, summaries, entrypoints, domains, log severities, diagnostics, selected rows, disabled states, warnings, and errors. Prefer named terminal colors plus bold/reverse modifiers over subtle RGB-only styling so common Windows Terminal, macOS, and Linux themes stay legible.
7. Update the footer/help text to show `Tab`/`Shift+Tab`/`Left`/`Right`, manual refresh, Settings severity selection, domain log opening, log pause/export, search, shutdown, and quit. Do not advertise `i`/`w`/`e`/`0` as the primary severity interaction.
8. Add focused automated tests for refresh timing/no-overlap behavior, keyboard navigation wraparound and selection preservation, Settings severity selection/application, Domains selected-row behavior, enabled/disabled markers, style decisions that can be asserted through `TestBackend`, and updated footer/help text.
9. Update the manual TUI smoke checklist and user-facing TUI diagnostics documentation to describe auto-refresh, left/right navigation, Settings-based severity selection, and visual state conventions.

## Validation
- Focused while iterating: `cargo test -p cadder-tui`
- Formatting: `cargo fmt --check`
- Lints: `cargo clippy --workspace --all-targets -- -D warnings`
- Full tests: `cargo test --workspace`
- Repository validation: `cargo run -p xtask -- check`
- Coverage gate: `cargo run -p xtask -- coverage`
- Manual smoke checklist: `docs/verification/tui-smoke.md` on an available terminal backend after implementation

## Assumptions
- TASK-1.12 is complete, so its TUI verification baseline is available.
- The current daemon/protocol state query and log query contracts are sufficient for this task.
- `Settings` should be a top-level tab rather than a popup because the acceptance criteria call for a Settings view.
- Legacy severity shortcuts may remain as secondary compatibility behavior if they do not confuse the footer/help model, but they are not the primary interaction.

## Risks And Boundaries
- Stateful table selection can require additional app/model state; keep it narrow and avoid a broad TUI architecture rewrite unless tests prove the current shape is too brittle.
- Auto-refresh must not starve keyboard input or create IPC request storms; the in-flight guard and a fixed interval are the boundary.
- Style tests should assert durable choices such as markers, modifiers, and semantic style helper output rather than fragile full-screen snapshots.
- Do not silently broaden the task into daemon behavior, protocol redesign, CI/release work, or real-Caddy-dependent automated tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation.

Implemented the planned cadder-tui polish: added auto state refresh with no overlapping requests and pending refresh handoff for toggle/manual races; added Settings as a top-level severity control; preserved per-view selection across Tab/Shift+Tab/Left/Right navigation; made Domains rows stateful with high-contrast selected-row styling and explicit `[x]`/`[ ]` activation markers; centralized named terminal color styles for tabs, summaries, tables, logs, diagnostics, disabled rows, warnings, and errors. Legacy log severity shortcuts remain as secondary compatibility behavior, but the footer and docs present Settings as the primary interaction.

Addressed independent review findings before closeout: queued one pending state refresh when refresh is requested during an in-flight state poll, prevented Settings severity changes from overlapping log IPC requests by waiting for the current response and then dispatching a pending refresh, and clarified the manual smoke checklist so Settings severity verification starts from an open Logs stream and returns to Logs for confirmation.

Validation after final changes: `cargo fmt --check`; `cargo test -p cadder-tui` (41 passed); `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage` (total line coverage 86.93%, above the 85% gate). Manual live terminal smoke was not run in this session; `docs/verification/tui-smoke.md` was updated for the new flow.

Additional documentation validation after closeout update: `bun run build` in `docs/site` completed successfully and did not leave extra tracked working-tree changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
## Summary
- Added automatic daemon state refresh in `cadder-tui` with in-flight guards and pending refresh handoff so manual refreshes and successful toggles do not overlap or leave stale state waiting for the next interval.
- Added a top-level Settings view for log severity selection, preserving the existing log query state while making `Up`/`Down` plus `Enter`/`Space` the primary filter flow.
- Reworked top-level navigation so `Tab`/`Shift+Tab` and `Left`/`Right` share wraparound behavior while preserving per-view row selection.
- Improved Domains, status, logs, diagnostics, and footer rendering with named high-contrast terminal styles, stateful selected rows, and explicit `[x]`/`[ ]` activation markers.
- Updated architecture notes, the TUI diagnostics guide, and the manual smoke checklist for auto-refresh, Settings severity control, left/right navigation, and visual state conventions.

## Tests
- `cargo fmt --check`
- `cargo test -p cadder-tui` (41 passed)
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage` (total line coverage 86.93%)

## Notes
- Manual live terminal smoke was not run in this session; the checklist was updated so it can be run on Windows Terminal, macOS, and Linux terminal backends.

Additional validation: `bun run build` in `docs/site` completed successfully for the updated Starlight documentation page.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
