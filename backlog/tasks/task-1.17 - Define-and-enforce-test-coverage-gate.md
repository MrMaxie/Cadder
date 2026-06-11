---
id: TASK-1.17
title: Define and enforce test coverage gate
status: Done
assignee:
  - '@agent'
created_date: '2026-06-10 11:29'
updated_date: '2026-06-11 05:34'
labels: []
milestone: m-1
dependencies:
  - TASK-1.15
references:
  - AGENTS.md
  - backlog/config.yml
documentation:
  - docs/ARCHITECTURE.md
modified_files:
  - xtask/src/main.rs
  - .github/workflows/ci.yml
  - README.md
  - docs/ARCHITECTURE.md
  - docs/site/src/content/docs/guides/validation.mdx
  - docs/site/src/content/docs/guides/release-process.mdx
parent_task_id: TASK-1
priority: medium
ordinal: 17000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Turn the project coverage expectation into an executable workflow. Cadder should have an explicit 85% coverage target, a documented way to measure it, and automation that keeps future changes honest without requiring a real machine-global Caddy installation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The project documents the coverage tool and command used to measure Rust workspace coverage.
- [x] #2 The documented workflow enforces at least 85% coverage or clearly fails when coverage cannot be measured.
- [x] #3 Coverage exclusions are limited to generated, platform-gated, or intentionally untestable code and are documented where the coverage command is defined.
- [x] #4 GitHub Actions runs the coverage workflow or records a clearly named follow-up dependency if the full CI workflow is not yet available.
- [x] #5 Backlog.md project Definition of Done defaults require tests or explicit verification and a coverage check for changed work.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Define a durable, executable Rust workspace coverage gate for Cadder with an explicit 85% line coverage threshold, local documentation, and GitHub Actions enforcement that does not require a machine-global real Caddy installation.

## Scope
- Use `cargo-llvm-cov` as the canonical Rust coverage tool.
- Pin the documented/CI coverage tool version to `cargo-llvm-cov 0.8.7`, matching the existing local coverage baseline artifact.
- Add a project-owned `xtask coverage` command so contributors and CI use the same coverage gate.
- Enforce total line coverage with `--fail-under-lines 85` and write a JSON summary report.
- Prefer the Windows MSVC toolchain for the canonical gate because prior GNU coverage attempts hit a `profiler_builtins` issue while MSVC coverage passed.
- Keep coverage exclusions empty initially; future exclusions must be limited to generated, platform-gated, or intentionally untestable code and documented beside the command.
- Extend GitHub Actions CI with a dedicated coverage job that installs the coverage tool and runs the gate without installing real Caddy.
- Update contributor and architecture documentation so the coverage workflow is discoverable.
- Verify Backlog.md Definition of Done defaults already require tests or explicit verification and coverage for changed work.

## Key Files
- `xtask/src/main.rs`
- `xtask/Cargo.toml` if implementation requires test-support dependency changes
- `.github/workflows/ci.yml`
- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/site/src/content/docs/guides/validation.mdx`
- `docs/site/src/content/docs/guides/release-process.mdx`
- `backlog/config.yml` for verification only unless the defaults no longer satisfy the acceptance criteria

## Implementation Steps
1. Add a `coverage` subcommand to `xtask` with constants for the 85% threshold and default report path, for example `target/llvm-cov/coverage-summary.json`.
2. Have `xtask coverage` run `cargo llvm-cov --workspace --json --summary-only --fail-under-lines 85 --output-path <report-path>`, and optionally expose `--output <path>` only if it keeps the command simple and testable.
3. Make the failure mode clear when `cargo-llvm-cov` is missing by preserving the underlying command error and documenting the installation path.
4. Add focused `xtask` unit tests for coverage option parsing/command construction so adding the gate does not erode the current narrow coverage margin.
5. Update `.github/workflows/ci.yml` with a separate coverage job on `windows-latest` using `stable-x86_64-pc-windows-msvc`, installing `cargo-llvm-cov 0.8.7`, and running `cargo run -p xtask -- coverage`.
6. Keep the existing Rust matrix for format, Clippy, and workspace tests unchanged unless the coverage job needs shared setup.
7. Update `README.md`, `docs/ARCHITECTURE.md`, and the Starlight validation/release-process docs with the tool, version, threshold, command, output path, CI behavior, and exclusion policy.
8. Replace the TASK-1.17 placeholder language in the validation docs with the real workflow.
9. Confirm `backlog/config.yml` already contains both required DoD defaults; if it does, do not edit it.
10. Run validation, then update task notes/acceptance criteria during execution through Backlog MCP only after each criterion is actually satisfied.

## Validation
- `cargo fmt --check`
- `cargo test -p xtask`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage`
- YAML parse for `.github/workflows/*.yml`
- `cd docs/site && bun install --frozen-lockfile`
- `cd docs/site && bun run check`
- `cd docs/site && bun run build`
- `git status --short`

## Risks And Boundaries
- The current recorded line coverage baseline is close to the threshold: `.local/coverage-summary.json` reports 85.46% line coverage with `cargo-llvm-cov 0.8.7`, so even small untested additions can fail the gate.
- Do not add artificial coverage exclusions to make the threshold pass. Prefer tests for existing behavior.
- Do not require or install a real machine-global Caddy in local coverage or CI; existing tests should continue to use fake Caddy fixtures.
- Do not fold coverage into `xtask check` unless explicitly approved later; keep coverage as a dedicated gate because it requires an extra tool and is slower.
- Do not change Backlog acceptance criteria or task scope while recording this plan.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation.

Implemented the coverage gate with `cargo-llvm-cov 0.8.7`, an 85% total line threshold, and `target/llvm-cov/coverage-summary.json` as the default JSON summary output. `xtask coverage` now creates the report directory, supports `--output <path>`, and uses `stable-x86_64-pc-windows-msvc` on Windows to avoid the known GNU `profiler_builtins` coverage failure. Updated CI to run a dedicated Windows MSVC coverage job and updated README, architecture, and Starlight validation/release docs with the tool version, command, threshold, output path, failure mode, and exclusion policy. Verified `backlog/config.yml` already contains the required DoD defaults. Validation passed: `cargo fmt --check`; `cargo test -p xtask`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage` with 4064/4757 covered lines (85.43%); YAML parse for `.github/workflows/*.yml`; `bun install --frozen-lockfile`; `bun run check`; `bun run build`.

Fresh closeout review on 2026-06-11 found no substantive issues in the coverage gate implementation. Re-ran validation after review: `cargo fmt --check`; `cargo test -p xtask`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage` with 4064/4757 covered lines (85.43%); Python YAML parse for `.github/workflows/*.yml`; `bun install --frozen-lockfile`; `bun run check`; `bun run build`. `bun run build` still emits the existing Astro sitemap warning because `site` is not configured, but the build exits successfully.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
## Summary
- Added `xtask coverage` as the project-owned Rust workspace coverage gate using `cargo-llvm-cov 0.8.7`, an 85% total line threshold, and `target/llvm-cov/coverage-summary.json` as the default JSON summary output.
- Added focused `xtask` tests for coverage option parsing, command construction, and report directory creation.
- Added a dedicated GitHub Actions coverage job on Windows MSVC and documented the local/CI workflow, threshold, output path, failure behavior, and zero-exclusion policy in README, architecture, and Starlight docs.
- Confirmed Backlog.md Definition of Done defaults already require tests or explicit verification and a coverage check for changed work.

## Validation
- `cargo fmt --check`
- `cargo test -p xtask`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage` with 4064/4757 covered lines (85.43%)
- Python YAML parse for `.github/workflows/*.yml`
- `bun install --frozen-lockfile`
- `bun run check`
- `bun run build`

## Risks And Follow-ups
- Coverage currently passes with a narrow margin, so future behavior changes should add tests before the gate regresses.
- TASK-1.19 tracks Docker/Testcontainers end-to-end coverage before the 1.0 release.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
