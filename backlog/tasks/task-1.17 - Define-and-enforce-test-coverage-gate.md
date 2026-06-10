---
id: TASK-1.17
title: Define and enforce test coverage gate
status: To Do
assignee: []
created_date: '2026-06-10 11:29'
updated_date: '2026-06-10 14:47'
labels: []
milestone: m-1
dependencies:
  - TASK-1.15
references:
  - AGENTS.md
  - backlog/config.yml
documentation:
  - docs/ARCHITECTURE.md
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
- [ ] #1 The project documents the coverage tool and command used to measure Rust workspace coverage.
- [ ] #2 The documented workflow enforces at least 85% coverage or clearly fails when coverage cannot be measured.
- [ ] #3 Coverage exclusions are limited to generated, platform-gated, or intentionally untestable code and are documented where the coverage command is defined.
- [ ] #4 GitHub Actions runs the coverage workflow or records a clearly named follow-up dependency if the full CI workflow is not yet available.
- [ ] #5 Backlog.md project Definition of Done defaults require tests or explicit verification and a coverage check for changed work.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
