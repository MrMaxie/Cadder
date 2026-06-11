---
id: TASK-1.15
title: Add GitHub CI/CD for Rust portable binaries and release artifacts
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 18:45'
updated_date: '2026-06-11 16:18'
labels: []
milestone: v1.0
dependencies:
  - TASK-1.11
  - TASK-1.16
references:
  - AGENTS.md
  - .github/workflows
  - Cargo.toml
  - xtask
documentation:
  - docs/ARCHITECTURE.md
modified_files:
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - .github/workflows/docs.yml
  - xtask/src/main.rs
  - xtask/Cargo.toml
  - Cargo.lock
  - docs/site/src/content/docs/guides/release-process.mdx
  - docs/ARCHITECTURE.md
  - README.md
parent_task_id: TASK-1
priority: medium
ordinal: 15000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add CI/CD for the Rust workspace and cross-platform portable binary artifacts. The pipeline should validate formatting, Clippy, tests, and release builds for `cadderd`, `caddy`, and `cadder-tui` on supported platforms, then publish portable archives without relying on OS installers or system PATH mutation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 CI runs `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, and `cargo test --workspace`.
- [x] #2 CI builds portable release artifacts for `cadderd`, `caddy`, and `cadder-tui` on Windows, Linux, and macOS or documents any platform intentionally deferred.
- [x] #3 Release packaging does not depend on WinUI, Windows App SDK, MSIX, NuGet, .NET SDK, shell profile edits, package-manager shims, or installer-specific state changes.
- [x] #4 CI uses fake Caddy fixtures for automated tests and does not require a machine-global real Caddy install.
- [x] #5 Release workflow publishes versioned portable archives to GitHub Releases for supported platforms or documents any platform intentionally deferred.
- [x] #6 GitHub Actions builds and publishes the generated documentation after the Astro Starlight documentation site exists.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add GitHub CI/CD for the Rust workspace, portable release archives, GitHub Releases publishing, and generated documentation publishing without introducing installers or machine-global state changes.

## Scope
- Add GitHub Actions workflows for workspace validation, release artifact publishing, and documentation publishing.
- Build and publish portable artifacts for `cadderd`, `caddy`, and `cadder-tui` on Windows, Linux, and macOS.
- Build macOS release artifacts for both processor families: `x86_64-apple-darwin` and `aarch64-apple-darwin`.
- Keep release packaging aligned with the existing portable model from TASK-1.11: no PATH mutation, shell profile edits, services, package-manager shims, installers, MSIX, NuGet, Windows App SDK, WinUI, or .NET SDK dependency.
- Use the existing Astro Starlight docs site from TASK-1.16 and publish generated output from source.
- Leave the full 85% coverage gate to TASK-1.17, while keeping this CI structure ready for that workflow to integrate later.

## Key Files
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/workflows/docs.yml`
- `xtask/src/main.rs`
- `Cargo.toml`
- `docs/site/package.json`
- `docs/site/bun.lock`
- `docs/site/src/content/docs/guides/release-process.mdx`
- `README.md` if workflow-facing contributor commands need a short update

## Implementation Steps
1. Add a CI workflow for `push` and `pull_request` with a runner matrix covering `ubuntu-latest`, `windows-latest`, and `macos-latest`.
2. In CI, run the required validation commands explicitly: `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, and `cargo test --workspace`. Keep `cargo run -p xtask -- check` as an optional aggregate confirmation if it does not add excessive duplicate runtime.
3. Ensure automated tests continue to use repository fake Caddy fixtures and do not require a machine-global real Caddy installation.
4. Add or extend `xtask` release packaging support so CI can create versioned portable archives from the existing `xtask dist --out <dir>` layout instead of duplicating package layout rules in YAML.
5. Define stable artifact names, for example `cadder-<version>-windows-x64.zip`, `cadder-<version>-linux-x64.tar.gz`, `cadder-<version>-macos-x64.tar.gz`, and `cadder-<version>-macos-arm64.tar.gz`, plus checksum files.
6. Add a release workflow triggered by version tags such as `v*`. It should build release layouts, run `xtask verify-dist`, archive each layout, generate checksums, upload workflow artifacts, and publish the archives to GitHub Releases.
7. Configure the release matrix so Windows and Linux build their native x64 artifacts, while macOS builds both `x86_64-apple-darwin` and `aarch64-apple-darwin`. Install Rust targets as needed with `rustup target add` and document any platform limitation if cross-target packaging cannot be completed cleanly.
8. Prefer first-party GitHub Actions and `gh release` for release publishing. Use minimal permissions: `contents: read` by default and `contents: write` only for the release publishing job.
9. Add a docs workflow for `push` to `main` that installs Bun, runs `bun install --frozen-lockfile`, `bun run check`, and `bun run build` in `docs/site`, then publishes `docs/site/dist` through GitHub Pages or uploads it as the Pages artifact.
10. Use minimal Pages permissions for docs publishing: `pages: write` and `id-token: write` for the deploy job, with `contents: read` for source checkout.
11. Update release-process documentation only where needed so it names the real workflow commands and artifact conventions without claiming installer behavior.
12. Keep generated docs output, release archives, checksums, and temporary packaging directories out of git unless a source file explicitly belongs in the repository.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- dist --out .local/verification/task-1.15/dist`
- `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.15/dist`
- Local archive/checksum command or `xtask` package command added for TASK-1.15
- `cd docs/site && bun install --frozen-lockfile`
- `cd docs/site && bun run check`
- `cd docs/site && bun run build`
- Local YAML validation where available
- `git status --short` to confirm only intended source/workflow/task changes remain

## Risks And Boundaries
- macOS arm64 packaging may need explicit Rust target installation and verification on a macOS runner. If a target cannot be built or verified in GitHub-hosted Actions, document the reason in the workflow/docs rather than silently omitting it.
- Cross-compiling from non-macOS runners is out of scope unless native GitHub-hosted macOS runners cannot satisfy the release matrix.
- Release artifacts must remain portable archives, not installers.
- GitHub Pages publishing may require repository Pages settings to use GitHub Actions as the source; document this as an operator prerequisite if needed.
- Coverage enforcement is intentionally left to TASK-1.17, which depends on this CI foundation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. User requested one adjustment: macOS release artifacts must be built for both x64 and arm64 processor families.

Implemented GitHub Actions CI, release, and documentation publishing for TASK-1.15. CI runs the required Rust format, Clippy, and workspace test commands on Windows, Linux, and macOS without installing a real machine-global Caddy. Release workflow builds native portable archives for windows-x64, linux-x64, macos-x64, and macos-arm64, uploads workflow artifacts, and publishes archives plus .sha256 files to GitHub Releases with gh release. Docs workflow builds the existing Astro Starlight site with Bun and deploys docs/site/dist through GitHub Pages. Extended xtask with target-aware dist/verify-dist and package support, including ZIP/TAR.GZ archive creation and checksums. Updated release documentation, architecture notes, and README. Validation passed: cargo fmt --check; cargo clippy --workspace --all-targets -- -D warnings; cargo test --workspace; cargo run -p xtask -- check; cargo run -p xtask -- dist --out .local/verification/task-1.15/dist; cargo run -p xtask -- verify-dist --dir .local/verification/task-1.15/dist; rustup target add x86_64-pc-windows-msvc; cargo run -p xtask -- package --out .local/verification/task-1.15-package/artifacts --version 0.1.0 --platform windows-x64 --target x86_64-pc-windows-msvc; cd docs/site && bun install --frozen-lockfile; bun run check; bun run build; Ruby YAML parse for all workflow files; cargo +stable-x86_64-pc-windows-msvc llvm-cov --workspace --json --summary-only --fail-under-lines 85 --output-path .local/coverage-summary.json passed with 85.46% line coverage. Local generated docs output and verification artifacts were removed after validation.

Fresh-eyes closeout review found and fixed a duplicate `required_path_option_reads_value_after_option` unit test in `xtask/src/main.rs` that would have caused a duplicate symbol compile failure. Re-ran validation after the fix: `cargo fmt --check`; `cargo test -p xtask`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo run -p xtask -- check`; `cargo run -p xtask -- dist --out .local/verification/task-1.15-closeout/dist`; `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.15-closeout/dist`; `cargo run -p xtask -- package --out .local/verification/task-1.15-closeout/artifacts --version 0.1.0 --platform windows-x64 --target x86_64-pc-windows-msvc`; Ruby YAML parse for `.github/workflows/*.yml`; `cd docs/site && bun install --frozen-lockfile`; `bun run check`; `bun run build`. The docs build emitted the existing sitemap warning because `site` is not configured in `astro.config.mjs`. I attempted to rerun the 85% coverage gate with `cargo +stable-x86_64-pc-windows-msvc llvm-cov ...`, but `cargo-llvm-cov` is not installed for the available toolchains in this session. Temporary verification artifacts, `docs/site/dist`, `docs/site/.astro`, and `docs/site/node_modules` were removed after validation.
<!-- SECTION:NOTES:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Rebaselined from app/installer CI to Rust binary CI/CD after the project pivot away from .NET/WinUI.
---

author: agent
created: 2026-06-10 11:29
---
Expanded from base Rust CI/CD to include the new AGENTS.md requirements for GitHub Releases artifact publishing and generated documentation publishing. Added TASK-1.16 as a dependency because documentation publishing needs the Astro Starlight site first.
---

created: 2026-06-10 12:04
---
TASK-1.11 planning clarified that Cadder should publish portable executables/archives, not installers or package-manager-specific PATH changes. CI/CD release artifacts should preserve that boundary.
---
<!-- COMMENTS:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented GitHub Actions CI/CD for Cadder's Rust portable binary release model.

What changed:
- Added CI, release, and documentation publishing workflows under `.github/workflows`.
- Extended `xtask` with target-aware `dist`, `verify-dist`, and `package` support, including ZIP/TAR.GZ archives and SHA-256 checksum files.
- Documented release artifact naming, GitHub Release publishing, Pages deployment, and the no-installer/no-global-state packaging boundary in README, architecture docs, and the Starlight release process guide.
- Fixed a duplicate `xtask` unit test found during closeout review.

Impact:
- CI validates formatting, Clippy, and workspace tests on Windows, Linux, and macOS.
- Release tags can publish portable archives for windows-x64, linux-x64, macos-x64, and macos-arm64 without WinUI, MSIX, NuGet, .NET SDK, PATH mutation, shell profile edits, or installer state.
- Docs are built from the Astro Starlight source and deployed via GitHub Pages.

Validation:
- `cargo fmt --check`
- `cargo test -p xtask`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- dist --out .local/verification/task-1.15-closeout/dist`
- `cargo run -p xtask -- verify-dist --dir .local/verification/task-1.15-closeout/dist`
- `cargo run -p xtask -- package --out .local/verification/task-1.15-closeout/artifacts --version 0.1.0 --platform windows-x64 --target x86_64-pc-windows-msvc`
- Ruby YAML parse for `.github/workflows/*.yml`
- `cd docs/site && bun install --frozen-lockfile`
- `cd docs/site && bun run check`
- `cd docs/site && bun run build`

Risks and follow-ups:
- Local closeout could not rerun the coverage gate because `cargo-llvm-cov` is not installed for the available toolchains in this session. Earlier task validation recorded an 85.46% line coverage pass.
- The docs build currently warns that sitemap generation is skipped because `astro.config.mjs` has no `site` option; this does not block TASK-1.15 but should be handled when production docs URL configuration is finalized.
<!-- SECTION:FINAL_SUMMARY:END -->
