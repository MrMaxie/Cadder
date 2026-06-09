---
id: TASK-1.13
title: Expand project README with banner
status: To Do
assignee: []
created_date: '2026-06-09 16:10'
labels: []
dependencies: []
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
Replace the scaffold-level README with a complete project README for Cadder. The README should present the project clearly to future users and contributors, use the existing `assets/banner.png` artwork near the top, and keep all setup, build, usage, and architecture claims aligned with the current repository state.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 `README.md` renders `assets/banner.png` near the top with useful alt text and a repository-relative image path that works on GitHub and local markdown preview.
- [ ] #2 The README explains what Cadder does, why it exists, and the core Windows tray daemon plus PATH-facing `caddy.exe` shim workflow in terms that match the current architecture.
- [ ] #3 The README documents prerequisites, build commands, run or development workflow, and any supported configuration or packaging notes that are discoverable from the current repository.
- [ ] #4 The README links to durable supporting documentation such as `docs/ARCHITECTURE.md` and avoids documenting behavior that is not implemented or explicitly planned.
- [ ] #5 The finished README is reviewed in a markdown renderer or equivalent preview so headings, code blocks, links, and the banner image display correctly.
<!-- AC:END -->
