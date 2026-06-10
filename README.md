# Cadder

Cadder is a cross-platform Rust coordinator for project-local Caddy development. A PATH-facing `caddy` shim registers each `caddy run` invocation with one persistent per-user daemon, and `cadder-tui` provides a minimal terminal UI for state, domains, logs, and diagnostics.

## Binaries

- `cadderd`: owns registrations, IPC, effective Caddy config composition, the real Caddy process, and bounded log storage.
- `caddy`: PATH shim for managed `caddy run` flows. Unsupported commands delegate to real Caddy after recursion-safe resolution.
- `cadder-tui`: Ratatui/Crossterm terminal UI for overview, entrypoints, domains, logs, diagnostics, filtering, and toggles.

## Build And Validate

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
cargo run -p xtask -- check
```

## Portable Distribution

Build a portable layout for the current platform:

```sh
cargo run -p xtask -- dist --out .local/dist/cadder
```

The output contains `cadderd`, `cadder-tui`, the `caddy` shim, and a sample `cadder.toml`. The command only builds and copies files; it does not edit PATH, shell profiles, package-manager shims, services, or other system state. Users may place the shim on PATH themselves under any name.

Verify an existing portable layout:

```sh
cargo run -p xtask -- verify-dist --dir .local/dist/cadder
```

## Real Caddy Resolution

Cadder resolves the real Caddy command in this order:

1. CLI override: `--real-caddy-command` for `cadderd`/`cadder-tui` daemon launch, or `--cadder-real-caddy-command` for the shim.
2. `[caddy].real_command` in `cadder.toml` in the current working directory.
3. `[caddy].real_command` in `cadder.toml` next to the executable.
4. Environment variables, including `CADDER_CADDY_REAL_COMMAND`.
5. A real `caddy` executable on PATH.

Example `cadder.toml`:

```toml
[caddy]
real_command = "/absolute/path/to/caddy"
```

Relative paths containing a path separator in `cadder.toml`, such as `./tools/caddy`, are resolved relative to that `cadder.toml`. A plain command such as `caddy` is resolved through PATH. `caddy-real` is supported only when configured explicitly through CLI, TOML, or environment variables.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the process boundaries and IPC shape.
