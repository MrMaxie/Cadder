---
id: TASK-1.15
title: Add GitHub CI/CD for app and installer builds
status: To Do
assignee: []
created_date: '2026-06-09 18:45'
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
Add a GitHub-based CI/CD workflow that validates Cadder on pull requests and produces build artifacts for the Windows application together with its installer or package output. The workflow should align with the existing Windows x64 build requirements and consume the installer/package shape defined by TASK-1.11 rather than inventing a separate distribution path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Pull requests run a GitHub workflow that restores and builds the solution using the repository's Windows x64 build requirements.
- [ ] #2 The workflow runs the relevant automated tests for Cadder and reports failures through GitHub checks.
- [ ] #3 A release or manually triggered workflow produces downloadable artifacts for the Cadder application and the installer/package output.
- [ ] #4 The CI/CD workflow documents required runner prerequisites, signing or certificate assumptions, and any secrets or manual release inputs without committing private credentials.
- [ ] #5 The workflow is kept consistent with build.ps1 and the packaging behavior from TASK-1.11 so local and CI builds do not diverge.
<!-- AC:END -->
