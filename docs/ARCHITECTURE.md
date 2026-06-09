# Cadder Architecture

Cadder is a Windows tray daemon that lets project-local `caddy.exe` invocations register domains with one persistent Caddy runtime.

## Process Roles

- Tray/daemon singleton: owns Cadder state for the signed-in Windows user and is the only process that should mutate registrations or talk to the real Caddy runtime.
- PATH-facing `caddy.exe` shim: a small executable named `caddy.exe` that intentionally shadows Caddy on PATH. It discovers the caller context and forwards registration requests to the daemon.
- Real Caddy runtime adapter: resolves, starts, reloads, observes, and eventually stops the real Caddy binary. TASK-1.1 only defines the boundary.
- IPC contract: request/response DTOs shared by the shim, daemon, and tray host.
- Registration store: durable owner-aware state for entrypoint registrations.
- GUI state projection: daemon read model that the tray UI can render without reaching into storage or runtime internals.

## Shim Versus Real Caddy

The shim exists so tools that already run `caddy.exe` can opt into Cadder without changing their command shape. The shim is not the Caddy server. It must not recursively invoke itself when Cadder needs the real binary.

Future resolver work must exclude Cadder's own shim identity before selecting a real Caddy binary. That exclusion should compare normalized paths and, where Windows APIs make it practical, file identity. A single raw string comparison is not enough because PATH entries, symlinks, casing, and short names can describe the same executable in different ways.

The exact resolver precedence is intentionally deferred to TASK-1.6 and TASK-1.11. This scaffold only records the safety rule: Cadder may shadow `caddy.exe`, but runtime operations must target a real Caddy binary that is not the shim.

## Initial Domain Model

`Cadder.Contracts` carries JSON-friendly DTOs for:

- entrypoint instance identity, including a shim session nonce;
- raw and canonical source working directory;
- raw and canonical source config path;
- registered domains with raw and canonical names;
- registration and domain activation state;
- owner process identity using PID plus process start time and shim nonce;
- log stream identity for per-domain and entrypoint streams;
- GUI state snapshots and IPC request/response shapes.

Path canonicalization, symlink handling, Caddyfile parsing, domain normalization, and owner cleanup are intentionally out of scope for this scaffold.

## Project Layout

- `src/Cadder.Contracts`: shared DTOs, process role names, and IPC contracts.
- `src/Cadder.Daemon`: daemon boundary interfaces, registration store contract, runtime adapter boundary, IPC endpoint boundary, and GUI state projector.
- `src/Cadder.Tray.WinUI`: minimal WinUI tray/daemon host scaffold.
- `src/Cadder.CaddyShim`: console shim project whose output assembly name is `caddy`.
- `tests/Cadder.Contracts.Tests`: contract shape tests.
- `tests/Cadder.Daemon.Tests`: daemon boundary and scaffold metadata tests.

## Build

Run the full scaffold build from the repository root:

```powershell
.\build.ps1
```

The script checks Windows and .NET 10 prerequisites, restores the solution, and builds all projects with `Platform=x64` and `RuntimeIdentifier=win-x64`.
