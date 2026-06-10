---
id: TASK-1.11
title: >-
  Package portable cross-platform binaries and configurable Caddy runtime
  resolution
status: In Progress
assignee:
  - '@agent'
created_date: '2026-06-09 11:44'
updated_date: '2026-06-10 12:03'
labels: []
dependencies:
  - TASK-1.2
  - TASK-1.3
  - TASK-1.6
references:
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\readme.md'
documentation:
  - docs/ARCHITECTURE.md
  - 'https://docs.rs/figment/latest/figment/'
modified_files:
  - Cargo.toml
  - Cargo.lock
  - crates/cadder-daemon/Cargo.toml
  - crates/cadder-daemon/src/lib.rs
  - crates/cadder-daemon/src/caddy.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadderd/src/main.rs
  - crates/cadder-shim/src/main.rs
  - crates/cadder-tui/src/main.rs
  - xtask/src/main.rs
  - README.md
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: medium
ordinal: 12000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Package Cadder as portable cross-platform Rust executables and add configurable real Caddy runtime resolution. Cadder must not modify system PATH, shell profiles, package-manager shims, or other system state. Users may choose to place the shim on PATH under any name, but the application should only provide binaries, documentation, and verification tooling. Real Caddy resolution must be configurable through layered sources so Cadder can be used either as a direct coordinator or through an optional Caddy-compatible shim.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Portable outputs include `cadderd`, `cadder-tui`, the current `caddy` shim binary, and a sample `cadder.toml` for the current platform without modifying PATH or other system state.
- [ ] #2 Real Caddy command/path resolution uses layered configuration with precedence: CLI override, `cadder.toml` in the current working directory, `cadder.toml` next to the executable, environment variables, then `caddy` available on PATH as the final fallback.
- [ ] #3 The shim can start or connect to the per-user daemon and register `caddy run` invocations from arbitrary project directories while honoring project-local layered configuration.
- [ ] #4 Unsupported Caddy commands are delegated to the safely resolved real Caddy binary only after recursion-safe resolution; otherwise they fail with a clear Cadder-owned message explaining how to configure the real Caddy command.
- [ ] #5 The portable build and verification workflow runs through Cargo/xtask on supported platforms and is not PowerShell-only.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Package Cadder as portable OS-specific Rust executables and make real Caddy runtime resolution configurable without modifying user PATH or system state.

## Scope
- Produce a portable distribution layout per supported OS containing `cadderd`, `cadder-tui`, the current `caddy` shim binary, and a sample `cadder.toml`.
- Do not implement installers, package-manager integration, shell profile edits, PATH mutation, Scoop/Homebrew/Apt setup, or automatic global shim creation.
- Let users decide whether to place a shim on PATH and under which name. The `caddy-real` convention is a local setup example, not the promoted product model.
- Add typed layered configuration for real Caddy resolution so Cadder can be used directly as Cadder or optionally through a Caddy-compatible shim.
- Keep the current default runtime model as `0-N entrypoints <-> single instance dashboard+backend`. Detached backend with multiple dashboards is future work.

## Key Files And Modules
- `crates/cadder-daemon/src/caddy.rs`
- `crates/cadder-daemon/src/ipc.rs`
- `crates/cadder-daemon/src/lib.rs`
- A new config module in `crates/cadder-daemon/src/` if that is the cleanest boundary
- `crates/cadder-daemon/Cargo.toml`
- `crates/cadderd/src/main.rs`
- `crates/cadder-shim/src/main.rs`
- `crates/cadder-tui/src/main.rs`
- `xtask/src/main.rs`
- `README.md`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Add a typed Cadder runtime configuration model for real Caddy resolution. Prefer `figment` with TOML and ENV features because it supports layered providers, typed extraction, and precise configuration-source diagnostics.
2. Define the effective precedence as CLI override, `cadder.toml` in the current working directory, `cadder.toml` next to the executable, environment variables, then `caddy` on PATH as the final fallback.
3. Preserve compatibility with existing environment names where useful, including `CADDER_CADDY_REAL_COMMAND`, but document them as configuration inputs rather than as the recommended primary path.
4. Refactor `RealCaddyResolver` to consume the effective configuration. Remove `caddy-real` as a built-in default fallback; users can still configure `caddy-real` explicitly through CLI, TOML, or ENV.
5. Keep recursion safety in the resolver by excluding the current executable and any known shim path from fallback PATH resolution. If resolution would point back to Cadder's shim, fail with a clear diagnostic.
6. Update `cadderd`, `cadder-tui`, and the shim to load the same configuration model at their respective startup boundaries. The shim should load project-local config from the invoking working directory so entrypoints can override real Caddy per project.
7. Update unsupported-command delegation so it only delegates after recursion-safe real Caddy resolution. If no safe real Caddy command can be resolved, print a Cadder-owned message that explains the CLI/TOML/ENV options.
8. Extend `xtask` with a portable distribution command, such as `cargo run -p xtask -- dist --out <dir>`, that builds release binaries and copies the OS-appropriate executable names plus a sample `cadder.toml` into the output directory.
9. Add an `xtask` verification path for the portable layout that checks the expected binaries, runs `caddy --cadder-shim-info`, and remains cross-platform.
10. Update README and architecture docs with the portable model, layered configuration precedence, per-system usage notes, optional user-managed PATH/shim setup, and the two runtime architecture modes: current default dashboard+backend and future detached backend with multiple dashboards.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- Focused tests for configuration precedence, TOML next to CWD and executable, environment fallback, PATH fallback, recursion-safe shim exclusion, unsupported-command messaging, and portable dist verification.
- A local portable-output smoke check under `.local/verification/task-1.11/` or another private scratch directory, with generated artifacts cleaned up unless intentionally retained under `.local`.

## Scope Boundaries
- Do not build an installer or modify system PATH.
- Do not add package-manager recipes or OS service registration.
- Do not promote `caddy-real` as the official resolution model; treat it only as a user-configurable example.
- Do not implement detached backend with multiple dashboards in this task.
- Do not require a globally installed real Caddy in automated tests; use fake Caddy fixtures for deterministic validation.
- Do not expand CI release publishing here beyond any local `xtask` shape needed by this task; TASK-1.15 owns GitHub release artifacts.

## Risks And Notes
- PATH fallback can accidentally find the Cadder shim if the user names it `caddy`; recursion-safe exclusions and clear diagnostics are mandatory.
- Config precedence must be documented exactly because users may run Cadder directly, through a renamed shim, or through a `caddy` shim.
- CWD config enables project-local behavior but can be surprising for unsupported delegated commands, so diagnostics should include the selected source when practical.
- Portable artifacts should remain simple enough for CI to archive later without changing their structure.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Scope was corrected from installer/PATH ownership to portable OS-specific executables, user-managed optional PATH/shim setup, and layered real Caddy configuration. The user's `caddy-real` setup is local context only, not a promoted product workflow.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-09 16:31
---
Future packaging context from user approval of TASK-1.5: the user's real global Caddy command is `caddy-real`/`caddy-real.exe`. Cadder's PATH-facing shim should be installed globally with Scoop using a command shape like `scoop shim add caddy "path_to_cadder_caddy.exe"`, while keeping the real Caddy command configurable and distinguishable from the shim.
---

author: @agent
created: 2026-06-10 10:44
---
Rebaselined from Windows packaging/PATH work to cross-platform Rust binary installation and shim shadowing.
---
<!-- COMMENTS:END -->
