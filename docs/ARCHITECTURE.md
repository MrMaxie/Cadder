# Cadder Architecture

Cadder is a Windows tray daemon that lets project-local `caddy.exe` invocations register domains with one persistent Caddy runtime.

## Process Roles

- Tray/daemon singleton: owns Cadder state for the signed-in Windows user and is the only process that should mutate registrations or talk to the real Caddy runtime.
- PATH-facing `caddy.exe` shim: a small executable named `caddy.exe` that intentionally shadows Caddy on PATH. It discovers the caller context and forwards registration requests to the daemon.
- Real Caddy runtime adapter: resolves, starts, reloads, observes, and eventually stops the real Caddy binary. TASK-1.1 only defines the boundary.
- IPC contract: request/response DTOs shared by the shim, daemon, and tray host.
- Registration store: transient in-memory owner-aware state for entrypoint registrations.
- GUI state projection: daemon read model that the tray UI can render without reaching into storage or runtime internals.

## Singleton Daemon Lifecycle

The WinUI tray host is the Cadder daemon process. It is a per-user singleton: startup first registers a stable Windows App SDK `AppInstance` key for activation redirection, then acquires a per-user named mutex before creating any window or tray icon. If another process launches Cadder while the daemon is already running, the new activation is redirected to the registered instance and the second process exits before creating another daemon surface.

The named mutex is the deterministic ownership boundary for the daemon lifecycle. A clean explicit quit releases the mutex after stopping IPC, clearing transient registrations, and asking the Cadder-owned runtime boundary to stop. If Windows reports an abandoned mutex, Cadder treats the lock as recoverable and rebuilds transient in-memory daemon state rather than permanently blocking startup.

Zero registrations are a normal running state. The daemon must stay alive and visible in the tray until the user chooses the explicit quit path. Later owner-cleanup tasks may remove registrations, but registration count alone must not terminate the daemon.

## Shim Versus Real Caddy

The shim exists so tools that already run `caddy.exe` can opt into Cadder without changing their command shape. The shim is not the Caddy server. It must not recursively invoke itself when Cadder needs the real binary.

For TASK-1.3, the supported shim command set is intentionally narrow: `caddy run` with optional `--config` and `--adapter`, plus `--cadder-shim-info` for diagnostics. Unsupported Caddy commands fail with a Cadder-owned message that names this supported set. Delegation to a real Caddy binary remains deferred until Cadder has a resolver that can reliably exclude the shim itself.

When `caddy run` is invoked, the shim captures the caller working directory, resolves the default `Caddyfile` under that directory when `--config` is omitted, preserves the raw command-line arguments, records the optional adapter, and builds a transient entrypoint registration with a generated shim session nonce. The shim first attempts to connect to the per-user daemon IPC pipe. If IPC is unavailable, it starts the Cadder tray daemon executable and polls for IPC readiness with a bounded timeout before registering.

After registration succeeds, the shim keeps the pipe session open, sends periodic heartbeat messages, and waits for process lifetime signals. Heartbeat updates `LastHeartbeatUtc` for freshness and diagnostics; it is not the only cleanup mechanism. On a graceful exit path such as Ctrl+C, the shim sends an unregister request before exiting. If the shim process is terminated or the pipe is otherwise lost, the daemon removes only the registrations owned by that pipe session.

Future resolver work must exclude Cadder's own shim identity before selecting a real Caddy binary. That exclusion should compare normalized paths and, where Windows APIs make it practical, file identity. A single raw string comparison is not enough because PATH entries, symlinks, casing, and short names can describe the same executable in different ways.

The exact resolver precedence is intentionally deferred to TASK-1.6 and TASK-1.11. This scaffold only records the safety rule: Cadder may shadow `caddy.exe`, but runtime operations must target a real Caddy binary that is not the shim.

## IPC Boundary

Cadder uses a per-user named pipe protocol shared by the shim, daemon, and future GUI consumers. Messages are line-delimited JSON envelopes with an explicit message type and a JSON payload. TASK-1.4 supports these request types:

- register entrypoint;
- unregister entrypoint;
- update entrypoint;
- list entrypoints;
- toggle entrypoint activation;
- heartbeat entrypoint;
- query GUI state;
- subscribe to GUI state changes.

The daemon endpoint writes registrations to an in-memory transient store and projects the current state for GUI queries. Register, update, toggle, heartbeat, and unregister operations are guarded by stable registration ID plus shim session nonce. The named pipe transport tracks pipe-owned registrations separately and does not rewrite `RegistrationId`; GUI and later runtime operations see stable IDs such as `shim-{nonce}` rather than transport-specific IDs.

GUI subscriptions are long-lived pipe reads. The server sends an initial `GuiStateChangedEvent` with `Snapshot`, then sends `RegistrationsChanged` events after registration mutations or heartbeat updates. This gives GUI code a push model without tight polling loops. Query remains available for one-shot state reads.

Caddyfile domain extraction and effective Caddy config composition remain TASK-1.5. Real Caddy binary resolution, runtime management, and unsupported-command delegation remain TASK-1.6 and TASK-1.11.

## Initial Domain Model

`Cadder.Contracts` carries JSON-friendly DTOs for:

- entrypoint instance identity, including a shim session nonce;
- raw and canonical source working directory;
- raw and canonical source config path;
- shim run metadata, including adapter, raw arguments, and preserved command line;
- registered domains with raw and canonical names;
- registration and domain activation state;
- owner process identity using PID plus process start time and shim nonce;
- registration lifecycle timestamps, including created time and last heartbeat;
- log stream identity for per-domain and entrypoint streams;
- GUI state snapshots and IPC request/response shapes.

Owner cleanup uses two daemon-side signals. Pipe disconnect cleanup unregisters only registrations created through that pipe session. The process watcher scans registrations and probes owner liveness by PID plus recorded process start time; a reused PID with a different start time is treated as a dead original owner, while lookup/access failures that do not prove death are treated conservatively as unknown and are not removed.

Path canonicalization, symlink handling, Caddyfile parsing, domain normalization, durable persistence, and real Caddy runtime state are intentionally out of scope for this task.

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
