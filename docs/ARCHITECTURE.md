# Cadder Architecture

Cadder is a Windows tray daemon that lets project-local `caddy.exe` invocations register domains with one persistent Caddy runtime.

## Process Roles

- Tray/daemon singleton: owns Cadder state for the signed-in Windows user and is the only process that should mutate registrations or talk to the real Caddy runtime.
- PATH-facing `caddy.exe` shim: a small executable named `caddy.exe` that intentionally shadows Caddy on PATH. It discovers the caller context and forwards registration requests to the daemon.
- Real Caddy runtime adapter: resolves, starts, reloads, observes, and stops the Cadder-owned real Caddy process. It keeps binary identity, process identity, admin endpoint, version, health status, idle state, and structured runtime diagnostics separate from registration and config diagnostics.
- IPC contract: request/response DTOs shared by the shim, daemon, and tray host.
- Registration store: transient in-memory owner-aware state for entrypoint registrations.
- GUI state projection: daemon read model that the tray UI can render without reaching into storage or runtime internals.

## Singleton Daemon Lifecycle

The WinUI tray host is the Cadder daemon process. It is a per-user singleton: startup first registers a stable Windows App SDK `AppInstance` key for activation redirection, then acquires a per-user named mutex before creating any window or tray icon. If another process launches Cadder while the daemon is already running, the new activation is redirected to the registered instance and the second process exits before creating another daemon surface.

The named mutex is the deterministic ownership boundary for the daemon lifecycle. A clean explicit quit releases the mutex after stopping IPC, clearing transient registrations, and asking the Cadder-owned runtime boundary to stop. The runtime boundary stops only the process handle that Cadder started and still identifies as owned; it does not enumerate or kill unrelated `caddy.exe` or `caddy-real.exe` processes. If Windows reports an abandoned mutex, Cadder treats the lock as recoverable and rebuilds transient in-memory daemon state rather than permanently blocking startup.

Zero registrations, or zero active domains after per-domain toggles, are a normal running daemon state. The config coordinator enters an explicit idle config state and asks the runtime boundary to enter an idle runtime state instead of reloading an empty active configuration. The daemon stays alive and visible in the tray until the user chooses the explicit quit path.

## Shim Versus Real Caddy

The shim exists so tools that already run `caddy.exe` can opt into Cadder without changing their command shape. The shim is not the Caddy server. It must not recursively invoke itself when Cadder needs the real binary.

For TASK-1.3, the supported shim command set is intentionally narrow: `caddy run` with optional `--config` and `--adapter`, plus `--cadder-shim-info` for diagnostics. Unsupported Caddy commands fail with a Cadder-owned message that names this supported set. Delegation to a real Caddy binary remains deferred until Cadder has a resolver that can reliably exclude the shim itself.

When `caddy run` is invoked, the shim captures the caller working directory, resolves the default `Caddyfile` under that directory when `--config` is omitted, preserves the raw command-line arguments, records the optional adapter, and builds a transient entrypoint registration with a generated shim session nonce. The shim first attempts to connect to the per-user daemon IPC pipe. If IPC is unavailable, it starts the Cadder tray daemon executable and polls for IPC readiness with a bounded timeout before registering.

After registration succeeds, the shim keeps the pipe session open, sends periodic heartbeat messages, and waits for process lifetime signals. Heartbeat updates `LastHeartbeatUtc` for freshness and diagnostics; it is not the only cleanup mechanism. On a graceful exit path such as Ctrl+C, the shim sends an unregister request before exiting. If the shim process is terminated or the pipe is otherwise lost, the daemon removes only the registrations owned by that pipe session.

Runtime resolution first uses the configured command or path from `CADDER_CADDY_REAL_COMMAND`, then falls back to the development command `caddy-real`. Path-like commands are normalized directly; command names are searched through `PATH` and executable extensions. The resolver rejects candidates that normalize to known Cadder shim paths, including `CADDER_CADDY_SHIM_PATH` and the app-local `caddy.exe` shim path. Accepted binaries record a stable path plus a lightweight file identity based on file metadata.

TASK-1.11 still owns installation, PATH shadowing, and packaging. TASK-1.6 only enforces the safety rule inside the daemon runtime boundary: Cadder may shadow `caddy.exe`, but runtime operations must target a real Caddy binary that is not Cadder's shim.

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

Unsupported-command delegation remains TASK-1.11. Real Caddy binary resolution and owned runtime management now live behind the daemon runtime boundary.

## Effective Caddy Configuration

TASK-1.5 builds the runtime configuration from active entrypoint registrations. Cadder does not hand-parse Caddyfile syntax as the source of truth. For each registered source config, it runs the Caddy adapter command shape:

```powershell
caddy adapt --config <Caddyfile> --adapter caddyfile
```

The current local default command is `caddy-real`, matching the development setup where the real global Caddy executable has been renamed away from Cadder's PATH-facing shim. TASK-1.6 will replace this narrow command default with full real-binary resolution and runtime ownership.

The adapted JSON is inspected for HTTP `host` matchers. Those host values become `RegisteredDomains` on the entrypoint registration with raw and canonical lowercase forms, so GUI and diagnostics can associate every domain with its source instance and config path. Adapter failures, missing config files, invalid adapted JSON, unsupported adapted shapes, validation failures, and reload failures are represented as structured `CaddyConfigDiagnostic` values in the GUI state snapshot.

The effective runtime config is composed as Caddy JSON. Cadder appends enabled routes from active registrations in registration-id order and keeps each registration's routes contiguous in that generated output. Per-domain activation is applied to host matchers: disabling one registered domain removes that host from the generated route match while preserving other enabled domains from the same entrypoint instance.

Conflict detection runs before runtime validation or reload. Domains are keyed by canonical host name, and conflicts are reported only when the same active domain appears in more than one active entrypoint instance. Diagnostics include the conflicting domain and the source config paths.

Validation and reload are atomic from Cadder's perspective. The coordinator calls the runtime boundary to validate the composed JSON first, ensures the Cadder-owned runtime process is running when there is active config to serve, then reloads only after validation and startup succeed. Failed adaptation, conflict detection, validation, startup, or reload attempts leave the last known good config hash and last successful reload timestamp intact.

Runtime failures are structured separately from config diagnostics. `RealCaddyRuntimeState` carries status, binary identity, Cadder-owned process identity, admin endpoint, version, and runtime diagnostics. GUI state snapshots include both runtime diagnostics and config diagnostics so the tray and panel can show whether Cadder is idle, unresolved, running, or unhealthy without crashing the daemon.

## Runtime Log Capture And Query

Cadder captures recent Caddy logs inside the daemon and exposes them through explicit IPC log queries rather than embedding log payloads in `GuiStateSnapshot`. The canonical selector is `LogStreamIdentity`: registered domains use their existing `domain-{canonical-host}` stream with the `caddy` channel, entrypoints keep their entrypoint stream, and runtime-wide control output uses the `runtime-control` stream.

The Cadder-owned long-lived `caddy run` process redirects stdout and stderr. Reader tasks drain both streams and write parsed log entries into a bounded ingestion path so process pipes are not held up by GUI consumers or slower diagnostics handling. Caddy JSON log lines are parsed when possible for timestamp, severity, request host/domain, and runtime session context. Lines that cannot be parsed remain available as raw redacted messages on runtime stdout/stderr streams with unknown domain attribution.

Runtime-control operations also write structured log entries. Version, validation, reload, start, stop, runtime-exit, and reader-overflow events are marked with an operation name and severity so callers can distinguish runtime errors from ordinary access logs and from config reload lifecycle events.

The in-memory log store is bounded by global count, per-stream count, and age. It assigns monotonic sequence numbers, returns opaque `seq:{number}` cursors, and reports gap metadata such as `HasGap`, `HasMoreBefore`, and `TruncatedByRetention` when retention means a caller's cursor can no longer be satisfied exactly. Durable log persistence across daemon restart is intentionally out of scope for TASK-1.7.

Redaction is shared by runtime diagnostics, config diagnostics, GUI state projection, and log storage. Token-like assignments, authorization headers, bearer values, and full shim command lines/argument arrays are replaced before they are exposed through diagnostics, GUI snapshots, or log query responses.

The log IPC surface is lazy: `QueryCaddyLogsRequest` filters by `LogStreamIdentity`, optional cursor, severity, and time range, and `QueryCaddyLogsResponse` returns only the requested page plus stream lifecycle status. TASK-1.10 owns the full logs UI, tailing controls, filtering controls, copy actions, and pause/auto-scroll behavior.

## Initial Domain Model

`Cadder.Contracts` carries JSON-friendly DTOs for:

- entrypoint instance identity, including a shim session nonce;
- raw and canonical source working directory;
- raw and canonical source config path;
- shim run metadata, including adapter, raw arguments, and preserved command line;
- registered domains with raw and canonical names extracted from adapted Caddy JSON host matchers;
- registration and domain activation state;
- owner process identity using PID plus process start time and shim nonce;
- registration lifecycle timestamps, including created time and last heartbeat;
- log stream identity for per-domain and entrypoint streams;
- GUI state snapshots, including current runtime state, Caddy config apply status and diagnostics, and IPC request/response shapes.

Owner cleanup uses two daemon-side signals. Pipe disconnect cleanup unregisters only registrations created through that pipe session. The process watcher scans registrations and probes owner liveness by PID plus recorded process start time; a reused PID with a different start time is treated as a dead original owner, while lookup/access failures that do not prove death are treated conservatively as unknown and are not removed.

Full installer/PATH setup, durable persistence, deep Windows file identity beyond current normalized-path and metadata checks, and advanced Caddy JSON merging beyond host-routed HTTP servers are intentionally out of scope for this task.

## Project Layout

- `src/Cadder.Contracts`: shared DTOs, process role names, and IPC contracts.
- `src/Cadder.Daemon`: daemon boundary interfaces, registration store contract, Caddy adapt/config coordination, runtime adapter boundary, IPC endpoint boundary, and GUI state projector.
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
