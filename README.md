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

Real Caddy is resolved from `CADDER_CADDY_REAL_COMMAND` first, then from PATH while excluding the Cadder shim path. Tests use fake Caddy fixtures by default.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the process boundaries and IPC shape.
