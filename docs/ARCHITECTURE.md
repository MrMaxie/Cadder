# Cadder Architecture

Cadder is a cross-platform Rust daemon, shim, and terminal UI for routing project-local `caddy run` invocations into one persistent per-user Caddy runtime.

## Process Roles

- `cadderd`: per-user daemon. It owns registrations, local IPC, Caddyfile adaptation, effective Caddy config composition, the Cadder-owned real Caddy process, runtime diagnostics, and bounded log storage.
- `caddy`: PATH-facing shim. It intentionally shadows Caddy for managed `caddy run` commands, starts or connects to `cadderd`, registers the caller's config, heartbeats while alive, and unregisters on exit.
- `cadder-tui`: Ratatui/Crossterm UI. It connects to the daemon and shows overview, entrypoints, grouped domains, per-domain logs, diagnostics, search/filtering, and activation toggles.
- Real Caddy runtime: resolved external binary. Cadder never embeds Caddy and must not recursively execute its own shim.

## Cross-Platform Runtime Model

Cadder uses per-user runtime paths from `directories::ProjectDirs`, with `CADDER_RUNTIME_DIR` as an override for tests and custom deployments. The daemon owns:

- a lockfile guarded by `fs4`, preventing multiple daemons for the same runtime directory;
- a local IPC socket name derived from the runtime directory;
- an effective generated Caddy JSON config file;
- daemon metadata and bounded in-memory state.

The daemon is started on demand by `caddy` and `cadder-tui` unless callers opt out. OS services are intentionally deferred; v1 is a user daemon model.

## IPC Boundary

IPC is versioned newline-delimited JSON over a per-user local socket via `interprocess`. Each message has an envelope:

```json
{ "protocolVersion": 1, "type": "query-state-request", "payload": { "requestId": "..." } }
```

Supported public messages include:

- register, unregister, and heartbeat entrypoint;
- query current state;
- subscribe to state changes;
- set entrypoint enabled;
- set domain enabled;
- query Caddy logs;
- request daemon shutdown.

Shim registrations are tied to the IPC session that created them. If the shim process exits without an unregister request, pipe disconnect cleanup removes only registrations owned by that session.

## Caddy Integration

Real Caddy resolution is layered and recursion-safe. The effective command is selected in this order:

1. CLI override.
2. `cadder.toml` in the current working directory.
3. `cadder.toml` next to the executable.
4. Environment variables, including `CADDER_CADDY_REAL_COMMAND`.
5. `caddy` on PATH as the final fallback.

The TOML schema is:

```toml
[caddy]
real_command = "/absolute/path/to/caddy"
```

Cadder no longer treats `caddy-real` as a built-in default. Users may still configure `caddy-real` explicitly through CLI, TOML, or environment variables. PATH fallback excludes the current executable and the known shim path from `CADDER_CADDY_SHIM_PATH`; when the shim starts the daemon, it passes its own path through that variable.

For each registration, Cadder runs:

```sh
caddy adapt --config <Caddyfile> --adapter <adapter>
```

The adapted JSON is inspected for HTTP host matchers. The adapter resolves real Caddy against the registration's source working directory, so project-local `cadder.toml` files are honored for shim-driven `caddy run` registrations from arbitrary directories. Active domains are composed into a generated effective Caddy JSON document. Domain conflicts are reported before runtime reload. When no active domains remain, the daemon enters idle config/runtime state instead of reloading an empty active config.

Runtime operations start the owned real Caddy process with the generated config and reload it on subsequent config changes. Captured stdout/stderr and control events are stored in a bounded log store with redaction for token-like values.

## Portable Packaging

`cargo run -p xtask -- dist --out <dir>` builds release binaries and copies the current platform's executable names into a portable layout:

- `cadderd`
- `cadder-tui`
- `caddy`
- `cadder.toml`

On Windows the binaries include the `.exe` suffix. `cargo run -p xtask -- verify-dist --dir <dir>` checks the expected files and runs `caddy --cadder-shim-info` from the layout. The packaging workflow does not modify PATH, shell profiles, package-manager shims, OS services, or other system state.

The current runtime model remains a single per-user daemon that owns the backend and serves the TUI/dashboard state. A future detached backend with multiple independent dashboards is intentionally outside the current implementation.

## Workspace Layout

- `crates/cadder-protocol`: shared DTOs, activation/runtime/log states, IPC envelopes, and request/response contracts.
- `crates/cadder-daemon`: runtime paths, daemon lock, local IPC server/client, registration state, Caddy integration, process runtime, and log store.
- `crates/cadderd`: daemon binary.
- `crates/cadder-shim`: package containing the PATH-facing `caddy` binary.
- `crates/cadder-tui`: terminal UI.
- `xtask`: repository validation task runner.

## Validation

Use Cargo from the repository root:

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
cargo run -p xtask -- check
```

Focused tests are appropriate while iterating. Full validation should pass before closeout.
