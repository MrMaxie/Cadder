---
id: TASK-1.13
title: Expand project README for Rust cross-platform usage
status: To Do
assignee: []
created_date: '2026-06-09 16:10'
updated_date: '2026-06-10 11:06'
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
Expand the README so a new user can understand and validate the Rust Cadder workspace, install or run the three binaries, configure real Caddy resolution, and understand the daemon/shim/TUI workflow. Image assets are optional documentation assets, not runtime requirements.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 README explains the `cadderd`, `caddy`, and `cadder-tui` binaries and their responsibilities.
- [ ] #2 README documents Cargo validation commands and the `xtask` check workflow.
- [ ] #3 README documents `CADDER_CADDY_REAL_COMMAND`, `CADDER_CADDY_SHIM_PATH`, and `CADDER_RUNTIME_DIR` at a user-facing level.
- [ ] #4 README avoids Windows-only tray, WinUI, MSIX, or .NET build instructions.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Rebaselined from README banner work to Rust cross-platform usage documentation after the project pivot away from WinUI.
---
<!-- COMMENTS:END -->
