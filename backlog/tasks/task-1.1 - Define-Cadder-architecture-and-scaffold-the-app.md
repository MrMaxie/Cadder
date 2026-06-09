---
id: TASK-1.1
title: Define Cadder architecture and scaffold the app
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:41'
updated_date: '2026-06-09 12:31'
labels: []
dependencies: []
references:
  - .local/examples/gui/openclaw-windows-node.slnx
  - .local/examples/gui/src/OpenClaw.Tray.WinUI/OpenClaw.Tray.WinUI.csproj
modified_files:
  - .editorconfig
  - .gitattributes
  - .gitignore
  - Cadder.slnx
  - Directory.Build.props
  - NuGet.Config
  - README.md
  - build.ps1
  - docs/ARCHITECTURE.md
  - global.json
  - src/Cadder.CaddyShim/Cadder.CaddyShim.csproj
  - src/Cadder.CaddyShim/Program.cs
  - src/Cadder.CaddyShim/ShimEntrypoint.cs
  - src/Cadder.Contracts/Cadder.Contracts.csproj
  - src/Cadder.Contracts/CadderRoles.cs
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Contracts/IpcContracts.cs
  - src/Cadder.Daemon/Cadder.Daemon.csproj
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Tray.WinUI/.gitignore
  - src/Cadder.Tray.WinUI/App.xaml
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - src/Cadder.Tray.WinUI/Assets/AppIcon.ico
  - src/Cadder.Tray.WinUI/Assets/LockScreenLogo.scale-200.png
  - src/Cadder.Tray.WinUI/Assets/SplashScreen.scale-200.png
  - src/Cadder.Tray.WinUI/Assets/Square150x150Logo.scale-200.png
  - src/Cadder.Tray.WinUI/Assets/Square44x44Logo.scale-200.png
  - >-
    src/Cadder.Tray.WinUI/Assets/Square44x44Logo.targetsize-24_altform-unplated.png
  - >-
    src/Cadder.Tray.WinUI/Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png
  - src/Cadder.Tray.WinUI/Assets/StoreLogo.png
  - src/Cadder.Tray.WinUI/Assets/Wide310x150Logo.scale-200.png
  - src/Cadder.Tray.WinUI/Cadder.Tray.WinUI.csproj
  - src/Cadder.Tray.WinUI/MainPage.xaml
  - src/Cadder.Tray.WinUI/MainPage.xaml.cs
  - src/Cadder.Tray.WinUI/MainWindow.xaml
  - src/Cadder.Tray.WinUI/MainWindow.xaml.cs
  - src/Cadder.Tray.WinUI/Package.appxmanifest
  - src/Cadder.Tray.WinUI/Properties/launchSettings.json
  - src/Cadder.Tray.WinUI/app.manifest
  - tests/Cadder.Contracts.Tests/Cadder.Contracts.Tests.csproj
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/Cadder.Daemon.Tests.csproj
  - tests/Cadder.Daemon.Tests/DaemonBoundaryTests.cs
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
- [x] #1 The repository contains a buildable initial application scaffold with separate daemon/tray, shim, shared contracts, and test projects or modules.
- [x] #2 The scaffold documents how the PATH-installed caddy.exe shim differs from the real Caddy binary and how Cadder resolves the real binary.
- [x] #3 The initial domain model includes entrypoint instance, source working directory, source config path, registered domains, activation state, owner process identity, and log stream identity.
- [x] #4 The app has a single command to build all scaffolded projects from a clean checkout.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Create the initial buildable Cadder scaffold and make the process boundaries explicit before feature implementation begins.

## Scope
- Build a .NET 10 solution with separate contracts, daemon/core, tray/daemon host, caddy.exe shim, and test projects.
- Establish contract types for entrypoint registrations, domain state, owner identity, and log stream identity.
- Document the role split between Cadder's PATH-facing shim and the real Caddy runtime.
- Provide one clean-checkout build command.

## Key Files And Modules
- `Cadder.slnx`
- `global.json`
- `Directory.Build.props`
- `NuGet.Config`
- `build.ps1`
- `src/Cadder.Contracts/`
- `src/Cadder.Daemon/`
- `src/Cadder.Tray.WinUI/`
- `src/Cadder.CaddyShim/`
- `tests/Cadder.Contracts.Tests/`
- `tests/Cadder.Daemon.Tests/`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Add repository-level .NET scaffolding: `Cadder.slnx`, pinned `global.json` for SDK `10.0.204`, root build properties, NuGet source config, and standard editor/git hygiene files.
2. Create the contracts, daemon/core, shim, WinUI tray host, and test projects using the appropriate .NET CLI/project tooling. Use the installed Microsoft WinUI template for `Cadder.Tray.WinUI` and check in the generated project so clean-checkout builds do not require the template.
3. Configure deterministic Windows build settings for the WinUI project and solution: x64 default, `RuntimeIdentifier=win-x64`, explicit Windows target/minimum versions, and package versions pinned to known-good values where needed (`Microsoft.WindowsAppSDK 2.1.3`, `Microsoft.Windows.SDK.BuildTools 10.0.28000.1839`).
4. Configure `Cadder.CaddyShim` as a console project whose output assembly/executable name is `caddy`, while keeping the project name distinct from the real Caddy binary.
5. Add `Cadder.Contracts` domain/IPC DTOs with explicit JSON-friendly shapes. Include entrypoint instance identity, raw and canonical source working directory, raw and canonical source config path, registered domains, activation state, owner process identity, and log stream identity.
6. Define `OwnerProcessIdentity` as more than a PID: include PID plus process start identity and/or a shim session nonce so future owner cleanup cannot confuse PID reuse with the original owner.
7. Define path and domain value contracts so later tasks can distinguish raw user input from canonical values. Do not implement full Windows path resolution, symlink handling, Caddyfile parsing, or domain normalization behavior in this task.
8. Add minimal daemon/core boundary types only where they clarify future process boundaries: daemon host boundary, registration store contract, real Caddy runtime adapter boundary, IPC request/response contract, and GUI state snapshot/read model. Avoid broad fake abstractions that would lock in TASK-1.2 through TASK-1.9 implementation details.
9. Add `docs/ARCHITECTURE.md` describing the named roles: tray/daemon singleton, PATH-facing `caddy.exe` shim, real Caddy runtime adapter, IPC contract, registration store, and GUI state projection.
10. In the architecture doc, document the safety rule for real Caddy resolution: the shim intentionally shadows `caddy.exe`, but Cadder must resolve a real Caddy binary without recursively invoking its own shim. Do not finalize source precedence in this task; defer exact resolver policy to TASK-1.6/TASK-1.11. Mention that future resolver logic must exclude self/shim identity by normalized path and, where possible, file identity rather than a single string path comparison.
11. Add focused tests proving the scaffold contract shape: models can represent the required fields, owner identity is not PID-only, raw/canonical source fields exist, domain activation and log stream identity are first-class, daemon snapshot/read-model shape exists, and shim project metadata produces `caddy.exe`.
12. Add `build.ps1` as the single clean-checkout build entrypoint. It should check Windows and .NET 10 prerequisites, run restore/build with `Platform=x64` and `RuntimeIdentifier=win-x64`, and avoid OpenClaw-specific Node/npm/GitVersion requirements.

## Validation
- Run `./build.ps1` from the repository root.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -r win-x64`.
- Run `git status --short` before finishing and remove only temporary artifacts created for the implementation.

## Scope Boundaries
- Do not implement real single-instance enforcement.
- Do not implement daemon process spawning or quit behavior.
- Do not implement live IPC transport.
- Do not implement shim registration lifecycle.
- Do not parse or compose Caddyfiles.
- Do not start, reload, observe, or stop the real Caddy runtime.
- Do not build the tray popup, main panel, domain toggles, or log UI beyond a minimal buildable host.

## Risks And Notes
- WinUI builds require deterministic x64/RID restore; `build.ps1` must restore with `RuntimeIdentifier=win-x64` before building.
- The WinUI template is installed in the current environment, but normal clean-checkout builds should rely on checked-in project files, not on template availability.
- Real Caddy resolver precedence is intentionally not finalized in this task to avoid preempting TASK-1.6 and TASK-1.11.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by user and recorded before implementation. WinUI template Microsoft.WindowsAppSDK.WinUI.CSharp.Templates@0.0.6-alpha was installed locally and validated with a temporary x64/win-x64 build probe.

Implemented the .NET 10 scaffold with Cadder.slnx, contracts, daemon boundaries, WinUI tray host, caddy.exe shim, architecture documentation, and focused xUnit tests. build.ps1 restores the solution and builds each scaffolded project with Platform=x64 and RuntimeIdentifier=win-x64 because dotnet build rejects solution-level RuntimeIdentifier with NETSDK1134. Validation passed: .\build.ps1; dotnet test Cadder.slnx -p:Platform=x64 -r win-x64; dotnet format Cadder.slnx --verify-no-changes.

Addressed final review risks: Cadder.CaddyShim now sets UseAppHost=true, build.ps1 verifies that the shim build emits caddy.exe, and build.ps1 fails if its project list drifts from Cadder.slnx.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the initial Cadder .NET 10 scaffold: solution/build configuration, contracts, daemon boundaries, WinUI tray host, PATH-facing caddy.exe shim, architecture documentation, and focused xUnit tests. The domain contracts now model entrypoint identity, raw/canonical source paths, registered domains, activation state, owner process identity beyond PID-only matching, and log stream identity. build.ps1 is the clean-checkout build entrypoint; it restores the solution, checks that its project list matches Cadder.slnx, builds each project with Platform=x64 and RuntimeIdentifier=win-x64, and verifies that the shim emits caddy.exe. Validation passed with .\build.ps1, dotnet test Cadder.slnx -p:Platform=x64 -r win-x64, and dotnet format Cadder.slnx --verify-no-changes. build.ps1 uses a project-level build loop for RuntimeIdentifier=win-x64 because dotnet build rejects solution-level RID for .slnx.
<!-- SECTION:FINAL_SUMMARY:END -->
