# Contributing to Cadder

Thanks for taking the time to improve Cadder.

Cadder is a Rust workspace for a per-user Caddy coordinator. The main parts are:

- `crates/cadder-protocol`: shared request and response contracts.
- `crates/cadder-daemon`: daemon state, IPC, Caddy process ownership, and runtime storage.
- `crates/cadderd`: daemon binary.
- `crates/cadder-shim`: PATH-facing `caddy` shim.
- `crates/cadder-tui`: terminal UI.
- `xtask`: validation, coverage, distribution, and packaging tasks.

## Development setup

Use Cargo from the repository root.

```sh
cargo fmt --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
cargo run -p xtask -- check
```

Documentation lives in `docs/site` and uses Bun:

```sh
cd docs/site
bun install --frozen-lockfile
bun run check
bun run build
```

## Pull requests

Keep pull requests focused. Include the reason for the change, the behavior that changed, and the commands you ran.

Automated tests should not depend on a globally installed real Caddy binary. Use repository fixtures or explicitly ignored integration tests when a real runtime is required.

## Releases

Cadder does not publish GitHub Releases before 1.0.0. The release workflow rejects `v0.*` tags before publishing.

To build a local release layout, run:

```sh
cargo run -p xtask -- dist --out target/cadder-dist
```

The layout contains `cadderd`, `cadder-tui`, the `caddy` shim, and `cadder.toml`. On Windows the binaries use the `.exe` suffix.

Verify an existing layout with:

```sh
cargo run -p xtask -- verify-dist --dir target/cadder-dist
```

Build a versioned portable archive and checksum with:

```sh
cargo run -p xtask -- package --out target/cadder-packages --version 1.0.0 --platform windows-x64 --target x86_64-pc-windows-msvc
```

The package command writes a platform archive next to a `.sha256` checksum file. Windows packages are `.zip`; Linux and macOS packages are `.tar.gz`.
