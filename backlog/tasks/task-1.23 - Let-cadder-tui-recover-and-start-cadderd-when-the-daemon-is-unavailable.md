---
id: TASK-1.23
title: Let cadder-tui recover and start cadderd when the daemon is unavailable
status: Done
assignee:
  - '@agent'
created_date: '2026-06-11 13:59'
updated_date: '2026-06-11 16:13'
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
  - crates/cadder-daemon/src/ipc.rs
  - docs/ARCHITECTURE.md
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
- [x] #1 Starting `cadder-tui` without a running daemon opens the TUI successfully and shows an explicit daemon-unavailable state with no panic, terminal corruption, or immediate process exit.
- [x] #2 The daemon-unavailable state distinguishes at least not-running, connection-failed, and start-failed outcomes when the underlying error can be determined safely.
- [x] #3 The TUI exposes a clear action to start `cadderd` from inside the TUI, reports progress while the start/reconnect attempt is in flight, and refreshes normal state after a successful connection.
- [x] #4 Daemon start uses the same per-user runtime/path assumptions as the shim and does not spawn recursive or duplicate daemon instances when one becomes available during the attempt.
- [x] #5 If daemon start fails, the TUI remains usable and shows an actionable error while keeping retry and quit flows available.
- [x] #6 Automated tests cover the TUI model/state transitions for unavailable, starting, connected, and failed daemon states without requiring a real daemon process.
- [x] #7 Documentation or smoke verification notes describe the no-daemon startup path and the in-TUI daemon start flow.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Make `cadder-tui` resilient when `cadderd` is not running or cannot be reached. The TUI should enter normally, show a clear daemon-unavailable state, allow the user to start `cadderd` from inside the TUI, and reconnect to normal state after a successful start.

## Scope
- Keep the work focused on `cadder-tui` startup/recovery behavior, daemon launch wiring, model state, tests, and verification docs.
- Reuse the existing Cadder daemon launch path instead of inventing a separate launcher.
- Preserve current per-user runtime behavior through `RuntimePaths`, `CADDER_RUNTIME_DIR`, and existing launch options.
- Do not expand into OS service installation, protocol redesign, packaging, release workflow, or real-Caddy-dependent automated tests.

## Key Files And Modules
- `crates/cadder-tui/src/main.rs`
- `crates/cadder-tui/src/model.rs`
- `crates/cadder-daemon/src/ipc.rs` only if the existing daemon launcher needs a narrow safety/testability improvement
- `crates/cadderd/src/main.rs` only if CLI/help behavior must be adjusted for the TUI start flow
- `docs/verification/tui-smoke.md`
- `docs/site/src/content/docs/guides/tui-diagnostics.mdx` if user-facing TUI guidance needs an update
- `docs/ARCHITECTURE.md` if durable daemon/TUI startup notes need a small update

## Implementation Steps
1. Rework `cadder-tui` bootstrap so terminal initialization and the app loop do not depend on a successful daemon start or initial `query-state`. The first connection attempt should set model state instead of returning an error that exits the process.
2. Add an explicit daemon connectivity model in `crates/cadder-tui/src/model.rs`, covering at least connected, not-running, connection-failed, starting, and start-failed. Classify errors conservatively so unknown errors do not get mislabeled.
3. Render the daemon-unavailable state prominently in Overview and status/footer text while keeping navigation, retry, and quit responsive. Use the existing style system for warning/error/progress states.
4. Add a clear in-TUI start/reconnect action, expected to be a keyboard command advertised in the footer. While it is in flight, mark the daemon as starting and prevent overlapping start attempts from the same TUI instance.
5. Implement the start action by calling `ensure_daemon_running_with_options` with the same runtime path and launch inputs already accepted by `cadder-tui` (`--runtime-dir`, `--daemon-path`, `--real-caddy-command`). After success, immediately issue a state refresh and clear the unavailable state only after a valid response.
6. Gate daemon-dependent actions while unavailable or starting. State refresh/reconnect and quit should remain available; toggles, log refreshes, IIS discovery/handoff, and shutdown should report actionable guidance instead of silently spawning doomed IPC requests.
7. Review `crates/cadder-daemon/src/ipc.rs` for duplicate/recursive start risk. If needed, make a narrow improvement around connect-before-spawn, post-spawn retry, or typed launch-result reporting, while preserving daemon lock ownership as the final duplicate-instance guard.
8. Add automated tests for TUI model/state transitions and app response handling: no-daemon startup state, connection failure, in-flight start, successful reconnect, start failure, retry availability, and no overlapping start attempts. Tests must not require a real daemon process.
9. Update smoke verification and user-facing docs to describe starting `cadder-tui` with no daemon, the daemon-unavailable screen, the in-TUI start/reconnect command, progress/error behavior, retry, and quit.

## Validation
- Focused tests while iterating: `cargo test -p cadder-tui`
- If daemon launcher code changes: `cargo test -p cadder-daemon ipc`
- Formatting: `cargo fmt --check`
- Lints: `cargo clippy --workspace --all-targets -- -D warnings`
- Full tests: `cargo test --workspace`
- Repository validation: `cargo run -p xtask -- check`
- Coverage gate: `cargo run -p xtask -- coverage`
- Manual smoke: follow `docs/verification/tui-smoke.md` for the no-daemon startup path and successful in-TUI start/reconnect flow on an available terminal backend.

## Assumptions
- `--no-start` should still prevent automatic daemon start during initial TUI launch, but the explicit in-TUI start action remains available because it is user-initiated.
- The existing `ensure_daemon_running_with_options` API is the preferred daemon launch path unless implementation reveals a narrow testability or safety gap.
- Distinguishing not-running versus connection-failed should be best-effort and based on safely available error details.
- The default behavior may still attempt startup automatically, but startup failure must degrade into the TUI recovery state instead of exiting.

## Risks And Boundaries
- Error classification can become brittle if it depends on platform-specific string matching; keep labels conservative and tests focused on controlled errors.
- Concurrent daemon starts from separate processes may still race before the daemon lock rejects duplicates; avoid adding broad global coordination unless the existing guard proves insufficient.
- Startup progress must not block terminal input or redraws; run launch/reconnect as a background app response, matching existing TUI request patterns.
- Do not mark acceptance criteria complete until implementation and verification prove each behavior.

## Execution Adjustment
- The daemon launcher now serializes startup attempts with a per-runtime `cadder-launch.lock` while waiting for the daemon socket. This replaced the earlier narrower connect-before-spawn-only guard and better satisfies the duplicate-start boundary while leaving the daemon runtime lock as the final ownership guard.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation.

Implemented the daemon recovery slice: cadder-tui now models connected, not-running, connection-failed, starting, and start-failed daemon states; daemon start/query-state runs in background responses instead of blocking terminal startup; daemon-dependent actions are gated while unavailable; the daemon launcher now rechecks connectivity immediately before spawning to reduce duplicate-start races. Focused validation so far: `cargo test -p cadder-tui` passed with 50 tests.

Addressed code-review findings: `Start failed` now remains visible until a manual retry/start action instead of being overwritten by automatic polling; Windows IIS discovery starts on the first successful daemon connection to preserve the previous eager-load behavior; daemon launch attempts now use a per-runtime launch lock so concurrent callers wait for readiness instead of spawning duplicate daemon children. Added focused tests for these cases.

Final validation passed after code-review fixes: `cargo fmt --check`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test -p cadder-tui` (51 tests); `cargo test -p cadder-daemon ipc` (8 focused tests); `cargo test --workspace`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage` with line coverage 86.69747689233076% (6941/8006), above the 85% gate. Manual PTY smoke ran `cargo run -p cadder-tui -- --runtime-dir <temp> --no-start` with no daemon; the TUI opened Overview showing `Daemon: Not running` and recovery guidance, then quit cleanly with `q` and exit code 0.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
## Summary
- Reworked `cadder-tui` startup so terminal initialization no longer depends on a successful daemon start or first `query-state`; unavailable daemon states are now rendered in the TUI instead of exiting.
- Added explicit daemon connectivity states for connected, not-running, connection-failed, starting, and start-failed, plus an in-TUI `s` start/reconnect action that uses the existing `ensure_daemon_running_with_options` path and refreshes state after a valid connection.
- Gated daemon-dependent actions while unavailable or starting, preserved responsive navigation/retry/quit flows, restored eager Windows IIS discovery on first successful connection, and added a per-runtime daemon launch lock to prevent duplicate spawn attempts across concurrent callers.
- Updated architecture and TUI smoke verification docs for no-daemon startup and recovery flows.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test -p cadder-tui` (51 tests)
- `cargo test -p cadder-daemon ipc` (8 focused tests)
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage` (line coverage 86.69747689233076%, 6941/8006)
- Manual PTY smoke: `cargo run -p cadder-tui -- --runtime-dir <temp> --no-start` opened Overview with `Daemon: Not running` and recovery guidance, then quit cleanly with `q` and exit code 0.

## Risks / Follow-ups
- No real Windows IIS environment smoke was run; IIS eager discovery is covered by Windows unit tests in this workspace.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
