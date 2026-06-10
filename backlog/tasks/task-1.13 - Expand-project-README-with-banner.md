---
id: TASK-1.13
title: Expand project README for Rust portable cross-platform usage
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 16:10'
updated_date: '2026-06-10 13:24'
labels: []
dependencies:
  - TASK-1.11
references:
  - README.md
  - assets/banner.png
documentation:
  - docs/ARCHITECTURE.md
modified_files:
  - README.md
parent_task_id: TASK-1
priority: medium
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Expand the README so a new user can understand and validate the Rust Cadder workspace, run or package the portable binaries, configure real Caddy resolution through layered configuration, and understand the daemon/shim/TUI workflow without assuming Cadder modifies PATH or installs global shims.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 README explains the `cadderd`, `caddy`, and `cadder-tui` binaries and their responsibilities.
- [x] #2 README documents Cargo validation commands, the `xtask` check workflow, and the portable dist/verification workflow introduced by TASK-1.11.
- [x] #3 README documents real Caddy resolution precedence: CLI, `cadder.toml` in CWD, `cadder.toml` next to the executable, environment variables, then `caddy` on PATH.
- [x] #4 README explains that PATH placement and shim naming are user-managed and optional, with per-platform examples that do not imply Cadder modifies system state.
- [x] #5 README avoids Windows-only tray, WinUI, MSIX, or .NET build instructions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Expand README.md so a new user can understand Cadder's cross-platform Rust workspace, binary roles, validation commands, portable distribution workflow, real Caddy resolution, and optional user-managed PATH setup.

## Scope
- Update only README.md.
- Keep the README aligned with TASK-1.11 and docs/ARCHITECTURE.md.
- Document current portable binary behavior; do not introduce installer, service, package-manager, WinUI, MSIX, tray, or .NET instructions.

## Implementation Steps
1. Review the existing README structure and keep the current banner/project introduction if present.
2. Expand the binary roles section for `cadderd`, `caddy`, and `cadder-tui`, including daemon ownership, shim registration/delegation, and TUI inspection/control responsibilities.
3. Expand build and validation docs with `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo test --workspace`, and `cargo run -p xtask -- check`.
4. Add a portable distribution section that documents `cargo run -p xtask -- dist --out <dir>`, output files (`cadderd`, `cadder-tui`, `caddy`, and `cadder.toml`), Windows `.exe` suffix behavior, `cargo run -p xtask -- verify-dist --dir <dir>`, and the fact that dist/verify do not modify PATH, shell profiles, services, package-manager shims, or system state.
5. Expand real Caddy resolution documentation with the exact precedence: CLI override, `cadder.toml` in the current working directory, `cadder.toml` next to the executable, environment variables including `CADDER_CADDY_REAL_COMMAND`, then safe real `caddy` on PATH.
6. Document `cadder.toml` examples and path behavior: absolute path, relative path with a path separator resolved relative to that config file, plain command resolved via PATH, and `caddy-real` supported only when explicitly configured.
7. Add optional user-managed PATH/shim examples per platform: Windows PowerShell example using a user-controlled directory, and macOS/Linux shell example using a user-controlled directory or symlink/copy. Make clear Cadder does not perform these changes.
8. Add a short first-run/user workflow: build or unpack portable layout, configure real Caddy if needed, optionally place shim on PATH, run `caddy run` from a project, and inspect with `cadder-tui`.
9. Re-read README against all TASK-1.13 acceptance criteria and remove any text implying Windows-only tray, WinUI, MSIX, .NET, automatic PATH mutation, or global install ownership.

## Validation
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- dist --out .local/verification/task-1.13/dist` if a fresh portable layout is needed for verification
- `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.13/dist` when a fresh layout is created
- `git diff --check`
- `git status --short`

## Scope Boundaries
- Do not edit files other than README.md unless new evidence requires user approval.
- Do not add installers, PATH mutation, OS services, package-manager recipes, WinUI, MSIX, tray, or .NET instructions.
- Do not change acceptance criteria while implementing without user approval.

## Risks And Notes
- README already contains a compact TASK-1.11 summary, so the implementation should expand and organize it without duplicating the same facts in multiple places.
- PATH examples must read as optional user-managed setup, not as Cadder-owned installation behavior.
- Real Caddy precedence must match code and architecture docs exactly.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Task prepared for execution with README.md as the intended write scope.

Implemented the README-only expansion for TASK-1.13. README.md now includes the banner asset, binary role descriptions for cadderd/caddy/cadder-tui, Cargo and xtask validation commands, portable dist and verify-dist workflow, layered real Caddy resolution precedence, cadder.toml path behavior, optional user-managed PATH/shim examples for Windows PowerShell and macOS/Linux shells, and a first-run workflow. Verified that the text avoids Windows-only tray, WinUI, MSIX, and .NET build instructions and does not imply Cadder modifies PATH or system state. Validation passed: git diff --check; cargo run -p xtask -- check; cargo run -p xtask -- dist --out .local/verification/task-1.13/dist; cargo run -p xtask -- verify-dist --dir .local/verification/task-1.13/dist. Temporary .local verification artifacts created for the dist check were removed after verification.

Closeout fresh-eyes review found one wording issue in the optional PATH examples: headings said "current shell only" even though the copy/symlink persists in the chosen user directory. Updated those headings to "user directory plus current shell PATH" and reran `git diff --check`, which passed.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Rebaselined from README banner work to Rust cross-platform usage documentation after the project pivot away from WinUI.
---

created: 2026-06-10 12:04
---
TASK-1.11 planning rebaselined usage documentation from PATH installation to portable binaries, user-managed optional PATH/shim setup, and layered real Caddy configuration. README scope should follow that model.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
README.md was expanded into a cross-platform Rust usage guide for Cadder. It now shows the banner asset, explains the `cadderd`, `caddy`, and `cadder-tui` binaries, documents Cargo and `xtask` validation, describes portable `dist` and `verify-dist` workflows, and captures the layered real Caddy resolution order with `cadder.toml` path behavior.

The README also clarifies that PATH placement and shim naming are optional user-managed choices. It includes Windows PowerShell and macOS/Linux examples that operate in user-controlled directories and do not imply Cadder edits PATH, shell profiles, services, package-manager shims, or system state. Windows-only tray, WinUI, MSIX, and .NET build instructions were not added.

Validation passed: `git diff --check`, `cargo run -p xtask -- check`, `cargo run -p xtask -- dist --out .local/verification/task-1.13/dist`, and `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.13/dist`. Temporary `.local` verification artifacts created for the dist check were removed after verification. After the final wording clarification, `git diff --check` was rerun and passed.
<!-- SECTION:FINAL_SUMMARY:END -->
