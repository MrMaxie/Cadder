---
id: TASK-1.13
title: Expand project README for Rust portable cross-platform usage
status: To Do
assignee: []
created_date: '2026-06-09 16:10'
updated_date: '2026-06-10 12:04'
labels: []
dependencies:
  - TASK-1.11
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
Expand the README so a new user can understand and validate the Rust Cadder workspace, run or package the portable binaries, configure real Caddy resolution through layered configuration, and understand the daemon/shim/TUI workflow without assuming Cadder modifies PATH or installs global shims.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 README explains the `cadderd`, `caddy`, and `cadder-tui` binaries and their responsibilities.
- [ ] #2 README documents Cargo validation commands, the `xtask` check workflow, and the portable dist/verification workflow introduced by TASK-1.11.
- [ ] #3 README documents real Caddy resolution precedence: CLI, `cadder.toml` in CWD, `cadder.toml` next to the executable, environment variables, then `caddy` on PATH.
- [ ] #4 README explains that PATH placement and shim naming are user-managed and optional, with per-platform examples that do not imply Cadder modifies system state.
- [ ] #5 README avoids Windows-only tray, WinUI, MSIX, or .NET build instructions.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 11:06
---
Rebaselined from README banner work to Rust cross-platform usage documentation after the project pivot away from WinUI.
---

created: 2026-06-10 12:04
---
TASK-1.11 planning rebaselined usage documentation from PATH installation to portable binaries, user-managed optional PATH/shim setup, and layered real Caddy configuration. README scope should follow that model.
---
<!-- COMMENTS:END -->
