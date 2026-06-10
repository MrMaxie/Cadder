---
id: TASK-1.14
title: Add cross-platform release metadata and terminal branding
status: To Do
assignee: []
created_date: '2026-06-09 16:10'
updated_date: '2026-06-10 10:44'
labels: []
dependencies:
  - TASK-1.11
references:
  - assets/logo.png
  - src/Cadder.Tray.WinUI/Assets/AppIcon.ico
  - src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj
  - src/Cadder.Tray.WinUI/Package.appxmanifest
  - src/Cadder.Tray.WinUI/DaemonTrayPresence.cs
  - src/Cadder.Tray.WinUI/MainWindow.xaml
  - src/Cadder.Tray.WinUI/MainWindow.xaml.cs
documentation:
  - docs/ARCHITECTURE.md
modified_files:
  - src/Cadder.Tray.WinUI/Assets/AppIcon.ico
  - src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj
  - src/Cadder.Tray.WinUI/Package.appxmanifest
  - src/Cadder.Tray.WinUI/DaemonTrayPresence.cs
  - src/Cadder.Tray.WinUI/MainWindow.xaml
  - src/Cadder.Tray.WinUI/MainWindow.xaml.cs
parent_task_id: TASK-1
priority: medium
ordinal: 14000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace the previous Windows tray/executable icon scope with cross-platform release metadata and lightweight terminal branding. Cadder should not depend on Windows tray icons or Windows App SDK assets in the final Rust implementation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Rust binaries expose consistent names, versions, descriptions, and help text through `--help` and `--version`.
- [ ] #2 README and architecture docs describe the `cadderd`, `caddy`, and `cadder-tui` binaries and the cross-platform install model.
- [ ] #3 Any retained image or logo assets are optional documentation/release assets and are not required for runtime behavior.
- [ ] #4 No Windows tray icon, WinUI asset, or Windows App SDK packaging requirement remains in the final build path.
<!-- AC:END -->

## Comments

<!-- COMMENTS:BEGIN -->
author: @agent
created: 2026-06-10 10:44
---
Rebaselined from Windows tray/executable icon work to cross-platform release metadata because the user explicitly redirected the project to Rust + Ratatui and no Windows-only GUI.
---
<!-- COMMENTS:END -->
