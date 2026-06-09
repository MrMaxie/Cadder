# Cadder Agent Guide

## Project

Cadder is a Windows tray daemon that routes project-local `caddy.exe` invocations into one persistent real Caddy runtime. Treat `docs/ARCHITECTURE.md` and Backlog.md tasks as the source of truth for process boundaries, task scope, and planned work.

## Local Workspace Rules

- If `.local` exists, keep `.local` listed in `.git/info/exclude`; do not add it to `.gitignore` unless explicitly requested.
- Read this file, applicable nested `AGENTS.md` files, and relevant `.local` workflow files before editing.
- Do not edit Backlog.md task markdown directly. Use Backlog MCP tools when available.
- Keep project-facing text, source comments, docs, commits, and task notes in English.
- Keep chat with the user in Polish unless they ask otherwise.

## Repo Layout

- `src/Cadder.Contracts`: shared DTOs, IPC contracts, and process role names.
- `src/Cadder.Daemon`: daemon lifecycle, registration store, IPC endpoint, Caddy config composition, and real Caddy runtime boundary.
- `src/Cadder.CaddyShim`: PATH-facing `caddy.exe` shim.
- `src/Cadder.Tray.WinUI`: WinUI 3 tray/daemon host.
- `tests/Cadder.Contracts.Tests`: contract serialization and shape tests.
- `tests/Cadder.Daemon.Tests`: daemon, shim, IPC, config, and runtime tests.
- `docs/ARCHITECTURE.md`: durable architecture notes.

## Build And Validation

Use PowerShell from the repository root.

```powershell
dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
.\build.ps1
dotnet format Cadder.slnx --verify-no-changes
```

- Run focused tests after narrow edits, then run the full relevant validation before closeout or commit.
- Avoid running multiple `dotnet restore/build/test` commands in parallel against the same projects; this can corrupt or race `project.assets.json` and output DLL writes.
- For WinUI changes, build `src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj` with `-p:Platform=x64 -p:RuntimeIdentifier=win-x64`.

## Skill Routing

Actively use available skills and helpers when they can improve correctness or reduce uncertainty.

- Use `$dotnet-csharp` for C# production code, async, process management, contracts, and runtime boundaries.
- Use `$dotnet-testing` for xUnit, fake/spy design, integration boundaries, and validation strategy.
- Use `$winui-app` for WinUI 3 app structure, XAML, lifecycle, packaging, and launch behavior.
- Use `$winui-ui-testing` for WinUI UI verification. Prefer `winapp ui` scripted tests and screenshots. Do not default to legacy pywinauto UIA for WinUI unless `winapp ui` is unavailable or explicitly unsuitable.
- Use `$windows-desktop-e2e` only for non-WinUI desktop automation or as a fallback after `winui-ui-testing` has been tried.
- Use `$backlogmd-task-*` skills for Backlog.md intake, execution, review, and closeout.
- Use `$commit-work` when staging or committing changes.
- Use `$agents-md-maintainer` when updating agent instructions.

## WinUI UI Testing

- Prefer running the built app from its output directory and target it by PID or HWND with `winapp ui`.
- Add stable `AutomationProperties.AutomationId` values for controls that tests need to assert or invoke.
- Capture screenshots with `winapp ui screenshot` and inspect them visually for clipping, overlap, missing content, and layout issues.
- If `winapp ui inspect` cannot see child controls, record the limitation clearly and keep the screenshot evidence; do not claim child-level automation passed.

## Commit Rules

- Use Conventional Commits in English, without scoped prefixes unless project instructions change.
- Review staged content with `git diff --cached` before committing.
- Keep unrelated task records or user-created files out of commits unless the user explicitly says they are intentional and should be committed.
