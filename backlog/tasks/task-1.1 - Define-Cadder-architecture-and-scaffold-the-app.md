---
id: TASK-1.1
title: Define Cadder architecture and scaffold the app
status: To Do
assignee: []
created_date: '2026-06-09 11:41'
labels: []
dependencies: []
references:
  - .local/examples/gui/openclaw-windows-node.slnx
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj
parent_task_id: TASK-1
priority: high
ordinal: 2000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Create the initial Cadder codebase structure and settle the process boundaries before feature work begins. The implementation should name the roles explicitly: tray/daemon singleton, caddy.exe shim entrypoint, real Caddy runtime adapter, IPC contract, registration store, and GUI state projection.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The repository contains a buildable initial application scaffold with separate daemon/tray, shim, shared contracts, and test projects or modules.
- [ ] #2 The scaffold documents how the PATH-installed caddy.exe shim differs from the real Caddy binary and how Cadder resolves the real binary.
- [ ] #3 The initial domain model includes entrypoint instance, source working directory, source config path, registered domains, activation state, owner process identity, and log stream identity.
- [ ] #4 The app has a single command to build all scaffolded projects from a clean checkout.
<!-- AC:END -->
