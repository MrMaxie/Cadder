# Cadder TUI Smoke Checklist

Use this checklist for manual terminal smoke verification of `cadder-tui` on supported terminal backends after lifecycle or TUI-facing changes. Automated tests cover the model and IPC contracts; this checklist verifies the real terminal loop, keyboard handling, and rendering.

## Prerequisites

- Build the workspace with `cargo build --workspace`.
- Start `cadderd` with a test runtime directory and a fake or local real Caddy command.
- Register at least two shim sessions whose adapted Caddyfiles expose multiple domains.
- Keep one domain disabled or conflicting when diagnostics need to be visible.
- For Windows IIS handoff smoke, use an elevated shell and a disposable IIS site or binding that can be removed and restored safely. Run `cadderd` and any shim process used during that smoke from the same elevated context. Do not use production IIS bindings. Follow the [Windows IIS handoff cookbook](../site/src/content/docs/cookbooks/windows/iis.mdx) for the operator flow.

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
- On Windows, press `Tab` or `Right` to open IIS Handoff and confirm the view lists IIS site bindings with site, protocol, IP address, port, host header, state, and safety details. On Linux and macOS, confirm the IIS Handoff view is absent.
- In IIS Handoff on Windows, confirm unsupported bindings are visible inline and cannot be handed off: unsupported protocols, unsupported ports, duplicate host bindings, active Cadder-domain conflicts, missing route hosts, and insufficient privilege errors should show a safety reason instead of silently changing IIS.
- For a disposable `http:80` or `https:443` IIS binding with a concrete host header, press `Space` and confirm the binding moves to `HandedOff`, Caddy remains the front door, and the status line reports the loopback IIS backend port.
- For a disposable wildcard or empty-host IIS binding, press `/`, enter the intended route host or full URL, press `Enter`, then press `Space`; confirm the handed-off row shows the route host and the target URL is served through Caddy to IIS. Application-level `4xx` or `5xx` responses can be valid smoke results when the target application is intentionally misconfigured; verify routing by checking that the response comes through Caddy and reaches IIS, for example `Via: 1.1 Caddy` with `Server: Microsoft-IIS/...`.
- Press `Space` on the handed-off IIS row again and confirm the original IIS binding is restored, the loopback backend binding is removed, and restore metadata is cleared after success. If other Cadder routes still need `:80` or `:443`, confirm restore is rejected rather than silently breaking those routes.
- If testing a failure path, force Cadder route apply to fail after IIS removal and confirm the UI reports whether rollback restored the IIS binding or rollback failed. If rollback fails, confirm the row remains recoverable instead of losing restore metadata. Do not proceed until the disposable binding is back under the expected owner.
- Press `Enter` or `l` on a domain row and confirm Logs opens for that exact domain, not another entrypoint or domain.
- In Logs, press `p` and confirm tailing pauses and resumes.
- With the domain log stream still open, switch to Settings, use `Up` and `Down` to choose `All`, `Info and higher`, `Warnings and errors`, or `Errors only`, then press `Enter` or `Space`; return to Logs and confirm the severity label and displayed page reset without mixing entries from different filters.
- Press `Enter` in Logs and confirm manual refresh remains responsive.
- Press `Tab` to open Diagnostics and confirm config/runtime diagnostics are shown when a conflict or runtime reload failure exists; otherwise confirm the empty diagnostics message is shown.
- Confirm the footer advertises `Tab`/`Shift+Tab`/`Left`/`Right`, manual refresh, Settings severity selection, log pause/refresh/export, daemon shutdown, and quit controls.
- On Windows, confirm the footer advertises IIS refresh and handoff/restore controls.
- Press `d` and confirm the daemon shutdown request returns a status message and does not freeze terminal input.
- Press `q` and confirm the terminal exits cleanly with normal echo/cursor behavior restored.

## Evidence To Record

- Platform and terminal backend.
- Runtime directory used.
- Fake Caddy or real Caddy command used.
- Whether IIS handoff was skipped, tested with a disposable binding, or tested with fake-provider data.
- Any rendering issue, stuck key flow, incorrect status, or daemon error observed.
