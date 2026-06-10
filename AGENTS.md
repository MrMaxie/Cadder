# Cadder Agent Guide

## Project

Cadder is a cross-platform Rust Caddy coordinator. It provides a per-user daemon (`cadderd`), a PATH-facing `caddy` shim, and a minimal Ratatui terminal UI (`cadder-tui`). Treat `docs/ARCHITECTURE.md` and Backlog.md tasks as the source of truth for process boundaries, task scope, and planned work.

## Local Workspace Rules

- If `.local` exists, keep `.local` listed in `.git/info/exclude`; do not add it to `.gitignore` unless explicitly requested.
- Read this file, applicable nested `AGENTS.md` files, and relevant `.local` workflow files before editing.
- Do not edit Backlog.md task markdown directly. Use Backlog MCP tools when available.
- Keep project-facing text, source comments, docs, commits, and task notes in English.
- Keep chat with the user in Polish unless they ask otherwise.

## Repo Layout

- `crates/cadder-protocol`: shared DTOs, IPC envelopes, and request/response contracts.
- `crates/cadder-daemon`: daemon state, local IPC, lockfiles, Caddy integration, runtime process management, and log storage.
- `crates/cadderd`: daemon binary.
- `crates/cadder-shim`: package that builds the PATH-facing `caddy` shim binary.
- `crates/cadder-tui`: Ratatui/Crossterm terminal UI.
- `xtask`: validation task runner.
- `docs/ARCHITECTURE.md`: durable architecture notes.

## Build And Validation

Use Cargo from the repository root.

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
cargo run -p xtask -- check
```

- Run focused tests after narrow edits, then run the full relevant validation before closeout or commit.
- Use `cargo add`, `cargo remove`, or another Cargo command for dependency changes; do not hand-edit dependency entries.
- Keep automated tests independent of a locally installed real Caddy unless explicitly marked ignored. Prefer fake Caddy fixtures for lifecycle/config/runtime tests.

## Engineering Notes

- Cadder is cross-platform by default. OS-specific code must sit behind a small abstraction and keep Windows, Linux, and macOS behavior explicit.
- Runtime state is per user and rooted in `directories::ProjectDirs`, with `CADDER_RUNTIME_DIR` available for tests and custom deployments.
- IPC is versioned newline-delimited JSON over a per-user local socket via `interprocess`.
- The `caddy` shim must never recursively execute itself when Cadder needs real Caddy. Real Caddy resolution checks `CADDER_CADDY_REAL_COMMAND` before PATH.
- The daemon owns only the real Caddy process it starts. It must not enumerate or kill unrelated Caddy processes.

## Skill Routing

- Use `$rust-pro` for Rust production code, async, process management, ownership-heavy design, contracts, and runtime boundaries.
- Use `$backlogmd-task-*` skills for Backlog.md intake, execution, review, and closeout.
- Use `$commit-work` when staging or committing changes.
- Use `$agents-md-maintainer` when updating agent instructions.

## Commit Rules

- Use Conventional Commits in English, without scoped prefixes unless project instructions change.
- Review staged content with `git diff --cached` before committing.
- Keep unrelated task records or user-created files out of commits unless the user explicitly says they are intentional and should be committed.
