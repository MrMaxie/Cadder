---
id: TASK-1.14
title: Use logo asset for tray and executable icons
status: To Do
assignee: []
created_date: '2026-06-09 16:10'
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
Make the existing `assets/logo.png` the source branding asset for Cadder's Windows identity surfaces. The tray icon, window or title-bar icon, packaged app logos, and generated executable or published file icons should all use the Cadder logo instead of scaffold or default Windows assets, while keeping the current WinUI tray host behavior intact.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The WinUI tray host uses icon assets derived from `assets/logo.png` for the notification-area tray icon and does not fall back to the default Windows application icon during normal builds.
- [ ] #2 The app window, title bar, taskbar, and other visible WinUI app identity surfaces display Cadder branding derived from `assets/logo.png`.
- [ ] #3 The project or packaging configuration assigns a branded icon to produced `Cadder.Tray.WinUI.exe` artifacts where supported, including file Explorer display and file properties after build or publish.
- [ ] #4 MSIX/package logo assets under `src/Cadder.Tray.WinUI/Assets` are regenerated or replaced from `assets/logo.png` at the required Windows asset sizes and remain referenced correctly from `Package.appxmanifest`.
- [ ] #5 A local build or publish verification confirms the icon files are included in output artifacts, and a manual or automated Windows visual check verifies the tray and executable icons render as expected.
<!-- AC:END -->
