---
id: TASK-1.19
title: Add Testcontainers end-to-end coverage before 1.0
status: To Do
assignee: []
created_date: '2026-06-11 04:47'
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
modified_files:
  - Cargo.toml
  - Cargo.lock
  - crates/cadder-daemon/tests
  - .github/workflows/ci.yml
  - docs/verification
  - docs/site/src/content/docs/guides
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
- [ ] #1 A dedicated end-to-end test suite uses Testcontainers for Rust to provision disposable container resources and exercises compiled Cadder binaries with an isolated CADDER_RUNTIME_DIR, without relying on a host-global Caddy installation.
- [ ] #2 The suite verifies a happy path where at least two project `caddy run` shim sessions register with one daemon, real Caddy serves their configured HTTP routes through exposed container ports, and daemon/TUI-facing state reports the expected entrypoints, domains, diagnostics, and log availability.
- [ ] #3 The suite verifies lifecycle behavior for shim exit or unregister, domain enable/disable, daemon shutdown, and conflict or invalid-config failure reporting in the container-backed environment.
- [ ] #4 Testcontainers resources are cleaned up automatically; tests use unique runtime directories and avoid fixed host ports or names so repeated and parallel runs do not leak processes or containers.
- [ ] #5 CI includes a Docker-enabled end-to-end job that gates release readiness before artifacts are accepted, while local validation clearly separates Docker-required e2e checks from unit and fake-Caddy checks when Docker is unavailable.
- [ ] #6 Project documentation explains prerequisites and commands for running the Testcontainers e2e suite locally and in CI, including how this suite differs from the fake-Caddy lifecycle tests.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
