---
id: TASK-1
title: Build Cadder cross-platform Rust Caddy coordinator
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:39'
updated_date: '2026-06-10 11:14'
labels: []
dependencies: []
references:
  - 'https://docs.rs/ratatui/latest/ratatui/'
  - 'https://github.com/ratatui/awesome-ratatui#-widgets'
  - 'https://docs.rs/crossterm/latest/crossterm/'
  - 'https://docs.rs/interprocess/latest/interprocess/'
  - 'https://docs.rs/directories/latest/directories/struct.ProjectDirs.html'
  - 'https://caddyserver.com/docs/command-line'
  - 'https://caddyserver.com/docs/api'
modified_files:
  - Cargo.toml
  - Cargo.lock
  - crates/cadder-protocol/src/lib.rs
  - crates/cadder-daemon/src/lib.rs
  - crates/cadder-daemon/src/caddy.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadder-daemon/src/logs.rs
  - crates/cadder-daemon/src/paths.rs
  - crates/cadder-daemon/src/runtime.rs
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-daemon/tests/ipc_lifecycle.rs
  - crates/cadderd/src/main.rs
  - crates/cadder-shim/src/main.rs
  - crates/cadder-tui/src/main.rs
  - crates/cadder-tui/src/model.rs
  - xtask/src/main.rs
  - README.md
  - docs/ARCHITECTURE.md
  - AGENTS.md
  - .gitignore
  - .gitattributes
  - .editorconfig
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build Cadder as a cross-platform Rust Caddy coordinator. Cadder owns a persistent per-user daemon (`cadderd`) that stays alive until explicit shutdown. A PATH-installed `caddy` shim starts or connects to the daemon, registers the invoking project's Caddy configuration while the shim process is alive, and removes that registration when the shim exits. A minimal Ratatui terminal UI (`cadder-tui`) replaces the previous Windows tray and panel surfaces. The final implementation must not retain WinUI, Windows App SDK, .NET project files, or Windows-only daemon assumptions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cadder can support zero, one, or many concurrently registered project entrypoints without closing the per-user daemon.
- [x] #2 The `caddy` shim lifecycle controls only the registrations owned by that shim process; the daemon remains alive after shim exit.
- [x] #3 The daemon can resolve, start, reload, observe, and stop the real Caddy runtime without requiring each project to own a separate Caddy process.
- [x] #4 The Rust Ratatui TUI shows registered entrypoints, grouped domains, activation state, diagnostics, and per-domain logs without any Windows-only GUI dependency.
- [x] #5 The task tree captures Rust daemon/runtime, shim/IPC, Caddy config composition, TUI, diagnostics, packaging, and cross-platform verification work.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
# Rust Cross-Platform Rewrite Plan

## Summary
- Replace the current .NET/WinUI scaffold with a fresh Rust Cargo workspace.
- Deliver separate binaries: `cadderd`, `caddy`, and `cadder-tui`.
- Keep existing .NET code only as behavioral reference during implementation; remove it from the final build path.
- Use cross-platform abstractions for daemon lockfiles, per-user runtime paths, IPC, process lifecycle, and terminal UI.

## Implementation Steps
1. Create the Rust workspace skeleton and shared crates: protocol/domain types, daemon runtime, shim, TUI, and `xtask`.
2. Port the core contracts and tests: registrations, domains, activation state, runtime/config/log DTOs, IPC envelopes, canonicalization, and serialization.
3. Implement cross-platform runtime paths, singleton lockfile behavior, local-socket JSON IPC, daemon state store, registration lifecycle, heartbeat cleanup, and state/log query endpoints.
4. Implement Caddy integration: real-binary resolution, Caddyfile adaptation through real Caddy, host inspection, active config composition, conflict diagnostics, owned process start/reload/idle/stop, and bounded redacted log storage.
5. Implement the PATH-facing `caddy` shim: parse supported `caddy run`, start/connect to daemon, register/heartbeat/unregister, and recursion-safe delegation for unsupported commands.
6. Implement `cadder-tui` with Ratatui/Crossterm: overview, entrypoints, domains, logs, diagnostics, search/filter, toggles, pause/resume log tail, and graceful quit.
7. Replace project docs/build instructions with Cargo/xtask validation and remove Windows-only/.NET files from the final tree.

## Validation
- Run `cargo fmt --check`.
- Run `cargo clippy --workspace --all-targets -- -D warnings`.
- Run `cargo test --workspace`.
- Run `cargo run -p xtask -- check`.
- Run focused integration tests with fake Caddy fixtures for registration, config adaptation, runtime reload, log capture, and shim lifecycle.

## Notes
- Rust 1.95.0 and Cargo 1.95.0 are available locally.
- Local `caddy` and `caddy-real` currently report Caddy 2.10.0, but automated tests should rely on fake fixtures unless explicitly marked ignored for real Caddy.
- Project-facing text remains English; chat remains Polish.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the Rust cross-platform rewrite skeleton and removed the previous .NET/WinUI build path. Added Cargo workspace crates for protocol contracts, daemon/runtime/IPC, `cadderd`, PATH-facing `caddy` shim, Ratatui `cadder-tui`, and `xtask` validation. Implemented local-socket newline-delimited JSON IPC, persistent shim sessions with pipe-disconnect cleanup, per-user runtime paths, fs4 lockfile singleton, Caddy adapt/host extraction/config composition, fake-Caddy lifecycle tests, bounded redacted logs, daemon shutdown, and minimal TUI views/actions for overview, entrypoints, domains, logs, diagnostics, filtering, toggles, log severity filter/export, and quit/shutdown flows. Removed `src/`, `tests/`, `.slnx`, .NET SDK/NuGet/MSBuild files, WinUI assets, and PowerShell-only build script. Final validation passed: `cargo run -p xtask -- check`. CLI smoke passed for `cadderd --help`, `cadder-tui --help`, and `caddy --cadder-shim-info`.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:43
---
Rebaseline approved by user: replace the prior Windows tray/WinUI direction with a fresh Rust cross-platform rewrite. Existing .NET/WinUI code is behavioral reference only and must not remain in the final architecture. Target binaries are `cadderd`, `caddy`, and `cadder-tui`; per-user daemon starts on demand through local IPC and a lockfile; UI is minimal Ratatui/Crossterm.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the Cadder cross-platform Rust rewrite. The repository now builds as a Cargo workspace with shared protocol contracts, daemon/runtime logic, a per-user `cadderd` daemon, a PATH-facing `caddy` shim, a Ratatui/Crossterm `cadder-tui`, and an `xtask` validation runner.

The implementation replaces the previous .NET/WinUI build path. It removes the old C# source tree, WinUI assets, `.slnx`, MSBuild/NuGet/.NET SDK files, and PowerShell-only build script. The new daemon uses per-user runtime paths, an `fs4` lockfile, `interprocess` local-socket newline-delimited JSON IPC, persistent shim-owned sessions with disconnect cleanup, real Caddy resolution, Caddyfile adaptation, active config composition, conflict diagnostics, bounded redacted log storage, and process runtime start/reload/idle/stop behavior. The TUI exposes overview, entrypoints, domains, logs, diagnostics, search/filtering, toggles, log severity filtering/export, pause/resume tailing, quit, and daemon shutdown.

Validation passed with `cargo run -p xtask -- check`, covering `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, and `cargo test --workspace`. Additional CLI smoke checks passed for `cadderd --help`, `cadder-tui --help`, and `caddy --cadder-shim-info`.

Follow-ups remain tracked separately in Backlog for deeper logs TUI work, packaging/install UX, broader lifecycle/TUI verification, README expansion, release metadata, and CI/CD.
<!-- SECTION:FINAL_SUMMARY:END -->
