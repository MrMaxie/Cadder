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
- Change daemon state from a shim session and confirm Overview updates automatically within a few seconds; press `r` and confirm manual refresh still works.
- Press `Tab` or `Right` to open Entrypoints and confirm each active shim registration is listed with ID, state, source path, and domain count.
- Press `Tab` or `Right` to open Domains, then use `Shift+Tab` or `Left` to navigate backward and confirm view navigation wraps cleanly.
- Confirm domains remain associated with their source entrypoint in the first table column.
- Confirm the selected Domains row is visibly highlighted, active domains show `[x]`, and disabled domains show `[ ]` with a lower-contrast disabled style.
- Select a domain with arrow keys, press `Space`, and confirm the domain activation state toggles after the automatic or manual refresh.
- Press `Enter` or `l` on a domain row and confirm Logs opens for that exact domain, not another entrypoint or domain.
- In Logs, press `p` and confirm tailing pauses and resumes.
- With the domain log stream still open, switch to Settings, use `Up` and `Down` to choose `All`, `Info and higher`, `Warnings and errors`, or `Errors only`, then press `Enter` or `Space`; return to Logs and confirm the severity label and displayed page reset without mixing entries from different filters.
- Press `Enter` in Logs and confirm manual refresh remains responsive.
- Press `Tab` to open Diagnostics and confirm config/runtime diagnostics are shown when a conflict or runtime reload failure exists; otherwise confirm the empty diagnostics message is shown.
- Confirm the footer advertises `Tab`/`Shift+Tab`/`Left`/`Right`, manual refresh, Settings severity selection, log pause/refresh/export, daemon shutdown, and quit controls.
- Press `d` and confirm the daemon shutdown request returns a status message and does not freeze terminal input.
- Press `q` and confirm the terminal exits cleanly with normal echo/cursor behavior restored.

## Evidence To Record

- Platform and terminal backend.
- Runtime directory used.
- Fake Caddy or real Caddy command used.
- Any rendering issue, stuck key flow, incorrect status, or daemon error observed.
