---
id: TASK-1.14
title: Add cross-platform release metadata and terminal branding
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 16:10'
updated_date: '2026-06-10 13:51'
labels: []
dependencies:
  - TASK-1.11
references:
  - assets/logo.png
  - assets/banner.png
documentation:
  - README.md
  - docs/ARCHITECTURE.md
modified_files:
  - crates/cadderd/Cargo.toml
  - crates/cadderd/src/main.rs
  - crates/cadder-shim/Cargo.toml
  - crates/cadder-shim/src/main.rs
  - crates/cadder-tui/Cargo.toml
  - crates/cadder-tui/src/main.rs
  - README.md
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: medium
ordinal: 14000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace the previous Windows tray/executable icon scope with cross-platform release metadata and lightweight terminal branding. Cadder should not depend on Windows tray icons or Windows App SDK assets in the final Rust implementation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Rust binaries expose consistent names, versions, descriptions, and help text through `--help` and `--version`.
- [x] #2 README and architecture docs describe the `cadderd`, `caddy`, and `cadder-tui` binaries and the cross-platform install model.
- [x] #3 Any retained image or logo assets are optional documentation/release assets and are not required for runtime behavior.
- [x] #4 No Windows tray icon, WinUI asset, or Windows App SDK packaging requirement remains in the final build path.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add consistent cross-platform release metadata and lightweight terminal branding for the three Rust binaries while keeping image assets optional and removing any Windows tray/WinUI packaging assumptions from the final build path.

## Scope
- Cover `cadderd`, the PATH-facing `caddy` shim, and `cadder-tui`.
- Improve CLI metadata exposed through `--help` and `--version`.
- Keep the runtime model portable and terminal-first.
- Clarify documentation around optional visual assets and the cross-platform install model.
- Do not reintroduce Windows tray icons, WinUI assets, Windows App SDK packaging, installers, PATH mutation, or release publishing in this task.

## Current Findings
- `cadderd --version`, `caddy --version`, and `cadder-tui --version` all return `0.1.0` with the expected binary names.
- All three `--help` commands work, but public options have little or no descriptive help text.
- The `caddy` shim help currently exposes only raw trailing `[CADDY_ARGS]...` without explaining managed `caddy run` behavior versus delegation to the real Caddy binary.
- README and architecture docs already describe the three binaries and portable installation model, but they should explicitly state that retained `assets/logo.png` and `assets/banner.png` are optional documentation/release assets, not runtime requirements.
- The old `src/Cadder.Tray.WinUI` tree is absent, and no WinUI/App SDK references were found outside Backlog task history.

## Key Files And Modules
- `Cargo.toml`
- `crates/cadderd/Cargo.toml`
- `crates/cadderd/src/main.rs`
- `crates/cadder-shim/Cargo.toml`
- `crates/cadder-shim/src/main.rs`
- `crates/cadder-tui/Cargo.toml`
- `crates/cadder-tui/src/main.rs`
- `README.md`
- `docs/ARCHITECTURE.md`
- `assets/logo.png`
- `assets/banner.png`

## Implementation Steps
1. Normalize release metadata in Cargo manifests. Prefer workspace package metadata where it fits, and add clear package descriptions for the daemon, shim, and TUI packages.
2. Update the `clap` command metadata for `cadderd`, `caddy`, and `cadder-tui` so each binary has a consistent name, version, short description, and help text style.
3. Add descriptive help strings for public daemon and TUI options: `--runtime-dir`, `--real-caddy-command`, `--daemon-path`, and `--no-start`.
4. Improve the `caddy` shim help text for trailing Caddy arguments so users can understand that `caddy run` is managed by Cadder while other commands are delegated to the safely resolved real Caddy binary.
5. Keep hidden Cadder-internal options hidden, but verify that public help still explains the supported operating model clearly enough without exposing internal flags.
6. Add focused CLI metadata tests without introducing a heavy test framework. Prefer `clap::CommandFactory`-style tests inside the binary crates to assert names, versions, descriptions, and public help content.
7. Update README and architecture documentation only where needed to clarify that retained logo/banner assets are optional documentation or release assets and are not required by runtime behavior.
8. Verify that no final build path references Windows tray icons, WinUI assets, or Windows App SDK packaging.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- Manual CLI smoke checks:
  - `cargo run -q -p cadderd -- --help`
  - `cargo run -q -p cadderd -- --version`
  - `cargo run -q -p cadder-shim --bin caddy -- --help`
  - `cargo run -q -p cadder-shim --bin caddy -- --version`
  - `cargo run -q -p cadder-tui -- --help`
  - `cargo run -q -p cadder-tui -- --version`
- Search verification that WinUI/App SDK/tray-icon references are absent from implementation and packaging paths.
- `git status --short` before closeout.

## Risks And Notes
- The shim binary intentionally reports the command name `caddy`; its description and help text should make clear that this is the Cadder shim without breaking expected `caddy` command ergonomics.
- Help text should remain concise and terminal-friendly; avoid long marketing copy in CLI output.
- Documentation changes should stay scoped because README and architecture already cover most of the portable install model from TASK-1.11.
- Adding snapshot-style tests could create brittle formatting churn; focused assertions against command metadata and selected help substrings should be more stable.

## Scope Boundaries
- Do not implement release artifact publishing or CI release workflows; those remain outside this task.
- Do not add GUI, tray, WinUI, Windows App SDK, or platform-specific icon requirements.
- Do not require logo/banner assets at runtime.
- Do not modify PATH, shell profiles, package-manager shims, or OS service registration.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Task was executed under the recorded plan.

Implemented cross-platform release metadata for the Rust binaries. `cadderd`, the PATH-facing `caddy` shim, and `cadder-tui` now inherit workspace package release fields, declare package descriptions, expose consistent Clap command names and versions, and include public help text for user-facing options or trailing Caddy arguments. Added focused command metadata/help tests in each binary crate.

Fresh-eyes review found that the help-output tests did not explicitly distinguish short `-h` help from full `--help` output. Fixed this by adding short-help package-description assertions and long-help behavior assertions for all three binaries.

Updated README and architecture documentation to describe `--help`/`--version`, the three binaries, the portable install/runtime model, and that `assets/logo.png`/`assets/banner.png` are documentation or release artwork only, not runtime requirements.

Validation passed after the final changes: `cargo fmt --check`; `cargo test -p cadderd -p cadder-shim -p cadder-tui`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo run -p xtask -- check`; manual `--help` and `--version` smoke checks for all three binaries; `cargo run -p xtask -- dist --out .local/verification/task-1.14/dist`; `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.14/dist`; `git diff --check`. Search verification found no active WinUI, Windows App SDK, AppIcon, Package.appxmanifest, Cadder.Tray, or tray-icon references in README, docs, Cargo manifests, crates, xtask, or assets. The temporary `.local/verification/task-1.14` dist directory was removed.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from Windows tray/executable icon work to cross-platform release metadata because the user explicitly redirected the project to Rust + Ratatui and no Windows-only GUI.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented cross-platform release metadata and terminal-oriented CLI branding for the Rust Cadder binaries.

What changed:
- `cadderd`, `caddy`, and `cadder-tui` now use workspace release metadata where appropriate and define package descriptions for release identity.
- Clap command definitions now expose stable command names, versions, descriptions, long help, and clearer public option/argument help text.
- Added focused tests for command metadata, short `-h` package descriptions, and long `--help` behavior across all three binaries.
- README and architecture docs now clarify the three binaries, `--help`/`--version`, the portable runtime/install model, and that retained logo/banner assets are documentation or release artwork only.

Validation:
- `cargo fmt --check`
- `cargo test -p cadderd -p cadder-shim -p cadder-tui`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- Manual `--help` and `--version` smoke checks for `cadderd`, `caddy`, and `cadder-tui`
- `cargo run -p xtask -- dist --out .local/verification/task-1.14/dist`
- `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.14/dist`
- `git diff --check`
- Search verification for removed Windows tray/WinUI/App SDK build-path references

Risks or follow-ups:
- No known follow-up for this task. Retained image assets remain optional documentation/release assets and are not used by runtime commands.
<!-- SECTION:FINAL_SUMMARY:END -->
