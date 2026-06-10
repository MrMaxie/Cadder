---
id: TASK-1.16
title: Build Astro Starlight documentation site
status: To Do
assignee: []
created_date: '2026-06-10 11:29'
updated_date: '2026-06-10 12:04'
labels: []
dependencies: []
references:
  - AGENTS.md
documentation:
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: medium
ordinal: 16000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Create a durable English documentation site for Cadder using Astro and the Starlight theme. The site should make the cross-platform Rust daemon, shim, TUI, portable binary model, layered runtime configuration, per-system usage guidance, validation workflow, and release process understandable without relying on chat context or stale WinUI-era documents.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The repository contains an Astro Starlight documentation site under `docs/` or a clearly documented docs workspace location.
- [ ] #2 The documentation covers Cadder architecture, portable binaries, optional user-managed PATH/shim setup, the `cadderd`, `caddy`, and `cadder-tui` binaries, runtime directories, and supported configuration sources.
- [ ] #3 The documentation explains real Caddy resolution precedence: CLI, `cadder.toml` in CWD, `cadder.toml` next to the executable, environment variables, then `caddy` on PATH.
- [ ] #4 The documentation includes per-system usage guidance for Windows, macOS, and Linux that explains how to use Cadder well without implying the application mutates system PATH.
- [ ] #5 Existing durable architecture content from `docs/ARCHITECTURE.md` is preserved, migrated, or linked so no current architecture detail is lost.
- [ ] #6 The docs build can be run reproducibly from the repository using the project package manager and is documented for contributors.
- [ ] #7 The documentation avoids stale WinUI, MSIX, NuGet, or .NET build instructions unless explicitly marked as historical context.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->

## Comments

<!-- COMMENTS:BEGIN -->
created: 2026-06-10 12:04
---
TASK-1.11 planning added the requirement for per-system usage docs around portable binaries, optional user-managed PATH/shim setup, and layered real Caddy configuration. The Starlight site should cover that durable guidance.
---
<!-- COMMENTS:END -->
