---
id: TASK-1.15
title: Add GitHub CI/CD for Rust binaries and release artifacts
status: To Do
assignee: []
created_date: '2026-06-09 18:45'
updated_date: '2026-06-10 11:06'
labels: []
dependencies:
  - TASK-1.11
references:
  - build.ps1
  - src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: low
ordinal: 15000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add CI/CD for the Rust workspace and cross-platform binary artifacts. The pipeline should validate formatting, Clippy, tests, and release builds for `cadderd`, `caddy`, and `cadder-tui` on supported platforms.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 CI runs `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, and `cargo test --workspace`.
- [ ] #2 CI builds release artifacts for `cadderd`, `caddy`, and `cadder-tui` on Windows, Linux, and macOS or documents any platform intentionally deferred.
- [ ] #3 Release packaging does not depend on WinUI, Windows App SDK, MSIX, NuGet, or .NET SDK.
- [ ] #4 CI uses fake Caddy fixtures for automated tests and does not require a machine-global real Caddy install.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Rebaselined from app/installer CI to Rust binary CI/CD after the project pivot away from .NET/WinUI.
---
<!-- COMMENTS:END -->
