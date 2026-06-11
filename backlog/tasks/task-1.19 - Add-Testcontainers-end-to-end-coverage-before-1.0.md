---
id: TASK-1.19
title: Add Testcontainers end-to-end coverage before 1.0
status: Done
assignee:
  - '@agent'
created_date: '2026-06-11 04:47'
updated_date: '2026-06-11 07:13'
labels:
  - testing
  - e2e
  - pre-1.0
milestone: v1.0
dependencies:
  - TASK-1.12
references:
  - 'https://rust.testcontainers.org/'
  - 'https://docs.rs/crate/testcontainers/latest'
documentation:
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - 'https://rust.testcontainers.org/quickstart/testcontainers/'
  - 'https://rust.testcontainers.org/features/files/'
  - 'https://rust.testcontainers.org/features/networking/'
  - docs/verification/testcontainers-e2e.md
  - docs/site/src/content/docs/guides/validation.mdx
  - docs/site/src/content/docs/guides/release-process.mdx
modified_files:
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - Cargo.lock
  - crates/cadder-daemon/Cargo.toml
  - crates/cadder-daemon/src/caddy.rs
  - crates/cadder-daemon/src/runtime.rs
  - crates/cadder-daemon/tests/ipc_lifecycle.rs
  - crates/cadder-daemon/tests/testcontainers_e2e.rs
  - docs/ARCHITECTURE.md
  - docs/site/src/content/docs/guides/release-process.mdx
  - docs/site/src/content/docs/guides/validation.mdx
  - docs/verification/testcontainers-e2e.md
parent_task_id: TASK-1
priority: high
ordinal: 19000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Cadder already has deterministic lifecycle coverage with fake Caddy fixtures, but the 1.0 release should also prove the daemon, PATH-facing shim, real Caddy integration, and observable state work in a disposable container-backed environment. Add Rust Testcontainers-based end-to-end tests that exercise compiled Cadder binaries with an isolated CADDER_RUNTIME_DIR and no dependency on a host-global Caddy installation. The suite should complement TASK-1.12 rather than replace the fast fake-Caddy tests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A dedicated end-to-end test suite uses Testcontainers for Rust to provision disposable container resources and exercises compiled Cadder binaries with an isolated CADDER_RUNTIME_DIR, without relying on a host-global Caddy installation.
- [x] #2 The suite verifies a happy path where at least two project `caddy run` shim sessions register with one daemon, real Caddy serves their configured HTTP routes through exposed container ports, and daemon/TUI-facing state reports the expected entrypoints, domains, diagnostics, and log availability.
- [x] #3 The suite verifies lifecycle behavior for shim exit or unregister, domain enable/disable, daemon shutdown, and conflict or invalid-config failure reporting in the container-backed environment.
- [x] #4 Testcontainers resources are cleaned up automatically; tests use unique runtime directories and avoid fixed host ports or names so repeated and parallel runs do not leak processes or containers.
- [x] #5 CI includes a Docker-enabled end-to-end job that gates release readiness before artifacts are accepted, while local validation clearly separates Docker-required e2e checks from unit and fake-Caddy checks when Docker is unavailable.
- [x] #6 Project documentation explains prerequisites and commands for running the Testcontainers e2e suite locally and in CI, including how this suite differs from the fake-Caddy lifecycle tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add a dedicated Docker/Testcontainers end-to-end suite that runs compiled Cadder binaries against real Caddy in a disposable container-backed environment, while keeping the existing fake-Caddy lifecycle tests as the fast deterministic default.

## Scope
- Add a Docker-required E2E suite that is separate from normal `cargo test --workspace` and existing fake-Caddy tests.
- Exercise compiled host `cadderd` and PATH-facing `caddy` shim binaries with a unique `CADDER_RUNTIME_DIR` per test run.
- Use Testcontainers to provision real Caddy inside a disposable container, with dynamic host port mapping and automatic cleanup.
- Avoid reliance on any host-global Caddy installation.
- Add a Docker-enabled CI gate before release artifacts are accepted, and document local/CI commands and prerequisites.
- Do not replace or weaken TASK-1.12 fake-Caddy tests.

## Key Files And Modules
- `Cargo.toml`
- `Cargo.lock`
- `crates/cadder-daemon/Cargo.toml`
- `crates/cadder-daemon/tests/testcontainers_e2e.rs`
- `crates/cadder-daemon/tests/ipc_lifecycle.rs` for comparison only; keep fake-Caddy coverage separate
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml` if tag publishing can bypass the Docker E2E gate
- `docs/verification`
- `docs/site/src/content/docs/guides/validation.mdx`
- `docs/site/src/content/docs/guides/release-process.mdx`

## Docker And CI Assumptions Verified During Planning
- Local Docker is available through Docker Desktop 4.77.0 with Docker Engine 29.5.3 and the `desktop-linux` context.
- Testcontainers for Rust is appropriate for provisioning disposable Docker resources, copying/mounting files, executing commands in containers, and using dynamically mapped host ports.
- GitHub-hosted Ubuntu runner images include Docker tooling, making an Ubuntu Docker E2E job the most practical CI target.
- The existing CI workflow currently has a cross-platform Rust matrix and a Windows coverage job, but no Docker-enabled E2E job.

## Implementation Steps
1. Add required test-only dependencies through Cargo CLI, likely `testcontainers`, `reqwest`, and a small process-control helper only if the final harness needs one.
2. Add a dedicated `crates/cadder-daemon/tests/testcontainers_e2e.rs` integration test target. Gate it so Docker-required tests do not run as part of the default `cargo test --workspace` path, for example with an explicit feature and/or ignored tests.
3. Build or locate compiled `target/debug` Cadder binaries for the harness: `cadderd` and the `caddy` shim. Allow explicit overrides through environment variables for CI or nonstandard target directories.
4. Start an official Caddy container through Testcontainers with container port `80` exposed to a random host port. Do not bind fixed host ports or stable container names.
5. Create unique temporary project directories and runtime directories per test. Generate at least two Caddyfiles with distinct `.localhost` hostnames and deterministic response bodies.
6. Provide a test-only real-Caddy proxy command used as `CADDER_CADDY_REAL_COMMAND`. The proxy should translate host paths to the container-mounted workspace path and delegate `adapt`, `run`, and `reload` operations to real Caddy inside the Testcontainers container, so Cadder itself still runs as host processes and remains inspectable through local IPC.
7. Spawn two long-lived host shim sessions using `caddy run --config <Caddyfile> --adapter caddyfile`, sharing one `CADDER_RUNTIME_DIR` and one daemon. Wait for daemon readiness and assert state through `CadderClient`/IPC.
8. Verify the happy path: two registrations, expected entrypoints/domains, applied config, running runtime state, no unexpected diagnostics, runtime-control log availability, and real HTTP responses through the mapped container port with matching `Host` headers.
9. Verify lifecycle behavior in the container-backed environment: one shim exit or unregister removes only that registration, domain disable/enable changes served routes and state, daemon shutdown stops the real Caddy runtime, duplicate domains report conflict diagnostics, and an invalid Caddyfile reports adapt/config failure.
10. Ensure cleanup is RAII-driven: child processes are terminated, daemon shutdown is requested, temp directories are unique, Testcontainers owns container lifecycle, and tests can run repeatedly without leaked ports/processes/containers.
11. Add a Docker E2E CI job on Ubuntu. Keep it separate from the cross-platform fake-Caddy matrix and make release readiness depend on it. If tag releases can bypass PR/main CI, update `release.yml` so package/publish waits for or reruns the Docker E2E gate before publishing artifacts.
12. Update contributor documentation to describe Docker prerequisites, local commands, CI behavior, expected skip/failure behavior when Docker is unavailable, and the distinction between fast fake-Caddy lifecycle tests and Docker/Testcontainers real-Caddy E2E tests.

## Validation
- `cargo fmt --check`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- Focused fake-Caddy regression: `cargo test -p cadder-daemon --test ipc_lifecycle`
- Docker E2E command, for example `cargo test -p cadder-daemon --features docker-e2e --test testcontainers_e2e -- --ignored`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage`
- Documentation checks from `docs/site`: `bun install --frozen-lockfile`, `bun run check`, `bun run build`
- `git status --short` before closeout

## Risks And Boundaries
- Path translation between host temp directories and container-mounted paths is the highest-risk part of the harness. Keep it explicit and covered by harness assertions.
- Current generated effective config listens on `:80` and `:443`; real Caddy should stay inside the container to avoid host privilege and port-collision issues.
- Host process signal behavior differs by platform. Keep Docker E2E CI on Ubuntu unless cross-platform Docker semantics are deliberately added later.
- Do not make Docker E2E part of default `cargo test --workspace`; local validation must clearly separate Docker-required checks from unit and fake-Caddy checks.
- Do not weaken coverage requirements or exclude broad code paths to compensate for Docker E2E cost.
- Do not introduce machine-global Caddy installation requirements in CI or local docs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. Task moved to In Progress and assigned to @agent.

Implemented a feature-gated `docker-e2e` integration test target for `crates/cadder-daemon` using `testcontainers 0.27.3` and the official `caddy:2.10.0-alpine` image. The suite starts compiled host `cadderd` and `caddy` shim binaries against a disposable real-Caddy container, uses a unique temporary `CADDER_RUNTIME_DIR`, dynamic host port mapping, and a generated proxy command that translates host temp paths to the container workspace for `adapt`, `run`, `reload`, and `stop`.

The Docker E2E scenario verifies two shim sessions, real HTTP routing for two `.localhost` hosts, daemon-facing registration/runtime/diagnostic state, runtime-control logs, domain disable/enable with stable negative HTTP assertions, Unix SIGINT unregister cleanup for the beta shim in CI, fallback process-exit cleanup, duplicate-domain conflict diagnostics, invalid Caddyfile `adapt-failed` diagnostics, and daemon runtime shutdown stopping real Caddy. Production fixes made while implementing the suite: persisted per-registration adapt diagnostics so they survive the follow-up apply pass, filtered persisted diagnostics out when an entrypoint is disabled, and requested graceful `caddy stop --address localhost:2019` before killing the managed runtime child.

Added a Docker E2E job to CI and made the release packaging job depend on a Docker E2E preflight. Updated architecture and Starlight validation/release docs, plus `docs/verification/testcontainers-e2e.md`, with prerequisites, commands, CI behavior, and the distinction from fake-Caddy lifecycle tests.

Verification run locally on Windows with Docker Desktop: `cargo add -p cadder-daemon --dev testcontainers@0.27.3`; `cargo fmt --check`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo test -p cadder-daemon --test ipc_lifecycle`; `cargo build -p cadderd -p cadder-shim`; `cargo test -p cadder-daemon --features docker-e2e --test testcontainers_e2e -- --ignored --test-threads=1`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage`; `bun install --frozen-lockfile`; `bun run check`; `bun run build`. Final coverage report: 85.6905503634476% line coverage, above the 85% threshold.

Closeout verification on 2026-06-11: `cargo fmt --check`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo test --workspace`, `cargo build -p cadderd -p cadder-shim`, `cargo test -p cadder-daemon --features docker-e2e --test testcontainers_e2e -- --ignored --test-threads=1`, `cargo run -p xtask -- check`, `cargo run -p xtask -- coverage`, `bun install --frozen-lockfile`, `bun run check`, `bun run build`, and `git diff --check` all passed. Coverage report remains above threshold at 85.6905503634476% line coverage.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented Docker/Testcontainers end-to-end coverage for the pre-1.0 release gate. The new `docker-e2e` test target runs compiled host `cadderd` and PATH-facing `caddy` shim binaries against real Caddy in an official disposable container with a unique `CADDER_RUNTIME_DIR`, dynamic mapped ports, and no host-global Caddy dependency.

The suite verifies two shim registrations, real HTTP routing, daemon-facing state, runtime-control logs, domain disable/enable, shim unregister cleanup, duplicate-domain conflict diagnostics, invalid Caddyfile adapt failures, and daemon runtime shutdown. Supporting daemon changes keep per-registration adapt diagnostics stable across apply passes, hide disabled-entrypoint diagnostics from effective config, and request a graceful `caddy stop` before killing the managed runtime child.

CI now has a Docker-enabled Ubuntu E2E job, and release packaging depends on a Docker E2E preflight before artifacts can be published. Architecture, validation, release, and verification docs describe local prerequisites, commands, CI behavior, and how this suite complements the fast fake-Caddy lifecycle tests.

Validation passed locally on Windows with Docker Desktop: `cargo fmt --check`; `cargo clippy --workspace --all-targets -- -D warnings`; `cargo test --workspace`; `cargo build -p cadderd -p cadder-shim`; `cargo test -p cadder-daemon --features docker-e2e --test testcontainers_e2e -- --ignored --test-threads=1`; `cargo run -p xtask -- check`; `cargo run -p xtask -- coverage`; `bun install --frozen-lockfile`; `bun run check`; `bun run build`; `git diff --check`. Final line coverage is 85.6905503634476%, above the 85% project threshold. Residual risk is limited to CI Docker environment differences, which are covered by the new Ubuntu Docker E2E gate.
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
