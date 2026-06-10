---
id: TASK-1.16
title: Build Astro Starlight documentation site
status: In Progress
assignee:
  - '@agent'
created_date: '2026-06-10 11:29'
updated_date: '2026-06-10 14:44'
labels: []
dependencies: []
references:
  - AGENTS.md
documentation:
  - docs/ARCHITECTURE.md
modified_files:
  - README.md
  - docs/site/.gitignore
  - docs/site/astro.config.mjs
  - docs/site/bun.lock
  - docs/site/package.json
  - docs/site/README.md
  - docs/site/tsconfig.json
  - docs/site/public/favicon.svg
  - docs/site/src/content.config.ts
  - docs/site/src/env.d.ts
  - docs/site/src/content/docs/index.mdx
  - docs/site/src/content/docs/guides/getting-started.mdx
  - docs/site/src/content/docs/guides/linux.mdx
  - docs/site/src/content/docs/guides/macos.mdx
  - docs/site/src/content/docs/guides/path-and-shim.mdx
  - docs/site/src/content/docs/guides/portable-binaries.mdx
  - docs/site/src/content/docs/guides/release-process.mdx
  - docs/site/src/content/docs/guides/tui-diagnostics.mdx
  - docs/site/src/content/docs/guides/validation.mdx
  - docs/site/src/content/docs/guides/windows.mdx
  - docs/site/src/content/docs/reference/architecture.mdx
  - docs/site/src/content/docs/reference/real-caddy-resolution.mdx
  - docs/site/src/content/docs/reference/runtime-configuration.mdx
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
- [x] #1 The repository contains an Astro Starlight documentation site under `docs/` or a clearly documented docs workspace location.
- [x] #2 The documentation covers Cadder architecture, portable binaries, optional user-managed PATH/shim setup, the `cadderd`, `caddy`, and `cadder-tui` binaries, runtime directories, and supported configuration sources.
- [x] #3 The documentation explains real Caddy resolution precedence: CLI, `cadder.toml` in CWD, `cadder.toml` next to the executable, environment variables, then `caddy` on PATH.
- [x] #4 The documentation includes per-system usage guidance for Windows, macOS, and Linux that explains how to use Cadder well without implying the application mutates system PATH.
- [x] #5 Existing durable architecture content from `docs/ARCHITECTURE.md` is preserved, migrated, or linked so no current architecture detail is lost.
- [x] #6 The docs build can be run reproducibly from the repository using the project package manager and is documented for contributors.
- [x] #7 The documentation avoids stale WinUI, MSIX, NuGet, or .NET build instructions unless explicitly marked as historical context.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add a durable Astro Starlight documentation site for Cadder. The repository should contain only documentation source files and reproducible build configuration. Generated site output must not be committed; GitHub Actions should build generated documentation from source on `main`.

## Scope
- Create an Astro Starlight docs workspace under `docs/site/`.
- Use Bun as the package manager.
- Commit source/config files and `bun.lock`.
- Do not commit generated output such as `dist/`, `.astro/`, build cache, or generated static HTML.
- Preserve or link current durable architecture content from `docs/ARCHITECTURE.md`.
- Do not add stale WinUI, MSIX, NuGet, .NET, installer, or automatic PATH mutation guidance.
- Treat GitHub Actions publishing as the CI contract for TASK-1.15 unless the user explicitly expands TASK-1.16 to add the workflow now.

## Key Files
- `docs/site/package.json`
- `docs/site/bun.lock`
- `docs/site/astro.config.mjs`
- `docs/site/src/content.config.ts`
- `docs/site/src/content/docs/**`
- `docs/site/README.md`
- `docs/ARCHITECTURE.md`
- `README.md`
- `.gitignore` if extra generated docs paths need exclusion

## Implementation Steps
1. Scaffold `docs/site` as an Astro Starlight site using Bun.
2. Add Bun scripts:
   - `bun run dev`
   - `bun run check`
   - `bun run build`
   - `bun run preview`
3. Ensure generated output stays out of git. Verify `docs/site/dist/`, `docs/site/.astro/`, and cache/build artifacts are ignored.
4. Build Starlight content around:
   - Overview
   - Getting Started
   - Architecture
   - Portable Binaries
   - Real Caddy Resolution
   - PATH and Shim Strategy
   - Windows Usage
   - macOS Usage
   - Linux Usage
   - Runtime Directories and Configuration
   - TUI and Diagnostics
   - Validation
   - Release Process
5. Document real Caddy resolution exactly: CLI override, `cadder.toml` in CWD, `cadder.toml` next to executable, environment variables, then safe `caddy` on PATH.
6. Document that Cadder does not mutate PATH, shell profiles, package-manager shims, services, or installer state.
7. Document contributor workflow with Bun from `docs/site`.
8. Add a CI handoff section: GitHub Actions should build docs on `push` to `main` from source using Bun, publish/upload generated output, and never write generated docs back to the repository.
9. Cross-check docs against `README.md`, `docs/ARCHITECTURE.md`, `xtask`, and the Rust CLI/config source files.

## GitHub Actions Contract
When implemented in TASK-1.15, docs publishing should:
- run on `push` to `main`;
- install Bun;
- run `bun install --frozen-lockfile` in `docs/site`;
- run `bun run check`;
- run `bun run build`;
- publish or upload `docs/site/dist`;
- never commit generated output.

## Validation
- `cd docs/site && bun install --frozen-lockfile`
- `cd docs/site && bun run check`
- `cd docs/site && bun run build`
- `cargo run -p xtask -- check`
- `git status --short` to confirm no generated output is tracked

## Risks
- Starlight source can drift from Rust CLI behavior if examples duplicate too much detail.
- Release-process docs must avoid claiming CI publishing exists before TASK-1.15 implements it.
- Generated docs output must stay out of the repo, including local preview/build artifacts.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. User requested Bun instead of npm and clarified that generated documentation output must not be committed; GitHub Actions should build generated docs from source on main.

Implemented the Astro Starlight documentation site under `docs/site` using Bun with exact dependency versions and source-only commits. Added pages for overview, getting started, architecture, portable binaries, real Caddy resolution, PATH/shim strategy, Windows/macOS/Linux usage, runtime configuration, TUI diagnostics, validation, and release-process CI handoff. Added `docs/site/.gitignore` entries for generated `dist`, `.astro`, and `node_modules`, updated root `README.md` with the docs workspace commands, and removed generated local artifacts after validation. Verification run: `cd docs/site && bun install --frozen-lockfile`, `bun run check`, `bun run build`; Playwright smoke check against `http://127.0.0.1:4321/` verified key pages and crawled 13 local links with HTTP 200; `cargo run -p xtask -- check` passed. Coverage was not measured because the repository coverage gate is tracked separately by TASK-1.17, so DoD coverage remains unchecked.

Closeout attempt: coverage was measured with local `cargo-llvm-cov v0.8.7` installed under `.local/cargo-tools` and run via `cargo +stable-x86_64-pc-windows-msvc llvm-cov --workspace --summary-only`. The GNU toolchain attempt failed with `can't find crate for profiler_builtins`; the MSVC run succeeded but reported total line coverage `62.48%`, below the project threshold of 85%. TASK-1.16 remains In Progress because Definition of Done #2 is not satisfied.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->

## Comments

<!-- COMMENTS:BEGIN -->
created: 2026-06-10 12:04
---
TASK-1.11 planning added the requirement for per-system usage docs around portable binaries, optional user-managed PATH/shim setup, and layered real Caddy configuration. The Starlight site should cover that durable guidance.
---
<!-- COMMENTS:END -->
