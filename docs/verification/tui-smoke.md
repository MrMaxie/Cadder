# Cadder TUI Smoke Checklist

Use this checklist for manual terminal smoke verification of `cadder-tui` on supported terminal backends after lifecycle or TUI-facing changes. Automated tests cover the model and IPC contracts; this checklist verifies the real terminal loop, keyboard handling, and rendering.

## Prerequisites

- Build the workspace with `cargo build --workspace`.
- Start `cadderd` with a test runtime directory and a fake or local real Caddy command.
- Register at least two shim sessions whose adapted Caddyfiles expose multiple domains.
- Keep one domain disabled or conflicting when diagnostics need to be visible.

## Terminal Backends

Run the checklist on each backend available for the platform under test:

- Windows Terminal on Windows.
- A VT-compatible terminal on Linux.
- Terminal.app or iTerm2 on macOS.

## Checklist

- Launch `cargo run -p cadder-tui -- --runtime-dir <runtime-dir> --no-start` and confirm the Overview view renders without layout corruption.
- Confirm Overview shows runtime status, config status, entrypoint count, domain count, and active domain count.
- Press `Tab` to open Entrypoints and confirm each active shim registration is listed with ID, state, source path, and domain count.
- Press `Tab` to open Domains and confirm domains remain associated with their source entrypoint in the first table column.
- Select a domain with arrow keys, press `Space`, and confirm the domain activation state toggles after refresh.
- Press `Enter` or `l` on a domain row and confirm Logs opens for that exact domain, not another entrypoint or domain.
- In Logs, press `p` and confirm tailing pauses and resumes; press `i`, `w`, `e`, and `0` and confirm severity filter changes reset the displayed page instead of mixing entries.
- Press `Enter` in Logs and confirm manual refresh remains responsive.
- Press `Tab` to open Diagnostics and confirm config/runtime diagnostics are shown when a conflict or runtime reload failure exists; otherwise confirm the empty diagnostics message is shown.
- Press `d` and confirm the daemon shutdown request returns a status message and does not freeze terminal input.
- Press `q` and confirm the terminal exits cleanly with normal echo/cursor behavior restored.

## Evidence To Record

- Platform and terminal backend.
- Runtime directory used.
- Fake Caddy or real Caddy command used.
- Any rendering issue, stuck key flow, incorrect status, or daemon error observed.
