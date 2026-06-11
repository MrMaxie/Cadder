# Cadder Architecture

Cadder is a cross-platform Rust daemon, shim, and terminal UI for routing project-local `caddy run` invocations into one persistent per-user Caddy runtime.

## Process Roles

- `cadderd`: per-user daemon. It owns registrations, local IPC, Caddyfile adaptation, effective Caddy config composition, the Cadder-owned real Caddy process, runtime diagnostics, and bounded log storage.
- `caddy`: PATH-facing shim. It intentionally shadows Caddy for managed `caddy run` commands, starts or connects to `cadderd`, registers the caller's config, heartbeats while alive, and unregisters on exit.
- `cadder-tui`: Ratatui/Crossterm UI. It connects to the daemon and shows overview, entrypoints, grouped domains, per-domain logs, diagnostics, search/filtering, and activation toggles.
- Real Caddy runtime: resolved external binary. Cadder never embeds Caddy and must not recursively execute its own shim.

All three Cadder binaries expose `--help` and `--version` through their Clap command definitions. The release-facing command names are `cadderd`, `caddy`, and `cadder-tui`.

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

## Domain Logs TUI

`cadder-tui` exposes per-domain logs from the Domains view. Pressing `Enter` or `l` on a domain row opens a log-focused view bound to that domain's `LogStreamIdentity`; the view keeps the selected stream even if registrations change later, so it never silently falls back to an entrypoint or unrelated domain.

The TUI loads a bounded page through `query-logs-request`, stores the returned cursor, and tails by issuing follow-up requests with that cursor. Auto-tail is enabled by default and can be paused with `p`; while paused, the TUI keeps keyboard handling responsive and avoids automatic refreshes until the user resumes or manually refreshes with `Enter`. Severity shortcuts reset the cursor before reloading so entries from different filters are not mixed.

Log refresh, state refresh, activation toggles, and shutdown requests are dispatched as short background IPC tasks. The TUI accepts at most one active log refresh for the current stream and surfaces IPC failures as a read-error state rather than blocking terminal input.

The log store reports stream status and retention metadata in `query-logs-response`, including active, empty, stale, removed, read-error, gap, more-before, and truncated-by-retention states. Diagnostic exports are timestamped text files in the caller's current working directory and contain only the daemon-redacted `LogEntry.raw_message` content plus stream metadata.

## Portable Packaging

`cargo run -p xtask -- dist --out <dir>` builds release binaries and copies the current platform's executable names into a portable layout:

- `cadderd`
- `cadder-tui`
- `caddy`
- `cadder.toml`

On Windows the binaries include the `.exe` suffix. `cargo run -p xtask -- verify-dist --dir <dir>` checks the expected files and runs `caddy --cadder-shim-info` from the layout. `cargo run -p xtask -- package --out <dir> --version <version> --platform <platform> --target <triple>` builds the target-specific layout, wraps it in a versioned portable archive, and writes a neighboring `.sha256` checksum file. Windows artifacts are `.zip` archives; Linux and macOS artifacts are `.tar.gz` archives.

The packaging workflow does not modify PATH, shell profiles, package-manager shims, OS services, or other system state.

Image assets under `assets/` are documentation and release artwork only. They are not copied into the runtime layout and are not required by the daemon, shim, TUI, or verification commands.

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

Cadder uses `cargo-llvm-cov 0.8.7` as the canonical Rust workspace coverage tool. On Windows, the canonical gate uses `stable-x86_64-pc-windows-msvc` because GNU coverage can fail without the profiler runtime. The executable gate is:

```sh
cargo run -p xtask -- coverage
```

`xtask coverage` runs `cargo +stable-x86_64-pc-windows-msvc llvm-cov --workspace --json --summary-only --fail-under-lines 85 --output-path target/llvm-cov/coverage-summary.json` on Windows and `cargo llvm-cov --workspace --json --summary-only --fail-under-lines 85 --output-path target/llvm-cov/coverage-summary.json` elsewhere. The command fails when total line coverage is below 85% or when coverage cannot be measured. It does not require a machine-global real Caddy installation; tests use repository fixtures.

No project-specific coverage exclusions are currently configured. Future exclusions must be limited to generated, platform-gated, or intentionally untestable code and documented next to the `xtask coverage` command definition.
