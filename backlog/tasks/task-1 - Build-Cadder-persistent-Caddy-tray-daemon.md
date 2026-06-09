---
id: TASK-1
title: Build Cadder persistent Caddy tray daemon
status: To Do
assignee: []
created_date: '2026-06-09 11:39'
labels: []
dependencies: []
references:
  - .local/examples/gui/docs/images/openclawwindows1.png
  - .local/examples/gui/docs/images/openclawwindows2.png
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/TrayMenuWindow.xaml
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/Windows/HubWindow.xaml
  - 'D:\Projects\Selleo\smarketing\apps\reverse-proxy\Caddyfile'
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build Cadder as a Windows tray-managed Caddy coordinator. Cadder owns a persistent single-instance daemon that stays alive until the user explicitly quits it. A PATH-installed caddy.exe shim starts or connects to the singleton daemon, registers the invoking project's Caddy configuration while the shim process is alive, and removes that registration when the shim exits. The GUI should borrow only tray and panel interaction patterns from the OpenClaw Windows Node reference, not its product logic.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Cadder can support zero, one, or many concurrently registered project entrypoints without closing the tray daemon.
- [ ] #2 The caddy.exe shim lifecycle controls only the registrations owned by that shim process; the daemon remains alive after shim exit.
- [ ] #3 The daemon can start, reload, and stop the real Caddy runtime without requiring each project to own a separate Caddy process.
- [ ] #4 The tray and panel GUI show registered entrypoints, grouped domains, activation state, and per-domain logs.
- [ ] #5 The task tree captures daemon/runtime, shim/IPC, Caddy config composition, GUI, diagnostics, packaging, and verification work.
<!-- AC:END -->
