---
id: TASK-1.21
title: Add Windows IIS domain handoff controls to cadder-tui
status: Done
assignee:
  - Codex
created_date: '2026-06-11 07:47'
updated_date: '2026-06-11 14:27'
labels:
  - windows
  - iis
  - tui
  - pre-1.0
milestone: v1.0
dependencies:
  - TASK-1.20
references:
  - crates/cadder-protocol/src/lib.rs
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadder-daemon/src/caddy.rs
  - crates/cadder-tui/src/model.rs
  - crates/cadder-tui/src/main.rs
documentation:
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - docs/site/src/content/docs/guides/windows.mdx
  - docs/site/src/content/docs/cookbooks/windows/iis.mdx
  - >-
    https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/sites/site/bindings/binding
  - >-
    https://learn.microsoft.com/en-us/iis/configuration/system.applicationhost/sites/site/bindings/
  - >-
    https://learn.microsoft.com/en-us/powershell/module/webadministration/get-webbinding?view=windowsserver2025-ps
  - >-
    https://learn.microsoft.com/en-us/powershell/module/webadministration/new-webbinding?view=windowsserver2025-ps
  - >-
    https://learn.microsoft.com/en-us/powershell/module/webadministration/remove-webbinding?view=windowsserver2025-ps
  - >-
    https://learn.microsoft.com/en-us/iis/get-started/getting-started-with-iis/getting-started-with-appcmdexe
  - >-
    https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http
modified_files:
  - crates/cadder-protocol/src/lib.rs
  - crates/cadder-daemon/src/iis.rs
  - crates/cadder-daemon/src/ipc.rs
  - crates/cadder-daemon/src/lib.rs
  - crates/cadder-daemon/src/state.rs
  - crates/cadder-daemon/src/caddy.rs
  - crates/cadder-tui/src/model.rs
  - crates/cadder-tui/src/main.rs
  - docs/ARCHITECTURE.md
  - docs/verification/tui-smoke.md
  - docs/site/astro.config.mjs
  - docs/site/src/content/docs/guides/windows.mdx
  - docs/site/src/content/docs/cookbooks/windows/iis.mdx
parent_task_id: TASK-1
priority: high
ordinal: 21000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a Windows-only IIS handoff surface that lets a user move selected IIS host bindings under Cadder control from the TUI and restore them back to IIS. The feature must be absent on non-Windows platforms rather than shown as a disabled placeholder. The implementation should preserve original IIS binding metadata, expose privilege and safety errors clearly, and avoid requiring real IIS in default automated tests by using a platform abstraction and fake provider coverage.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 On Windows, `cadder-tui` exposes an IIS handoff view that lists discoverable IIS site bindings with site name, protocol, IP address, port, host header/domain, and current handoff state.
- [x] #2 On non-Windows platforms, the IIS handoff view and related commands are not present in the TUI, and the workspace continues to build and test without Windows-only dependencies leaking into other targets.
- [x] #3 The daemon/protocol exposes platform-gated IIS handoff state and commands through a small abstraction that can report IIS unavailable, insufficient privileges, unsupported binding shape, conflicts, and successful handoff/restore outcomes without panics.
- [x] #4 Turning a supported IIS domain `on` for Cadder preserves enough original IIS binding metadata to restore it later, releases or disables the IIS-owned binding safely, and creates or activates the Cadder-owned route for the same domain only when a safe handoff plan exists.
- [x] #5 Turning a handed-off domain `off` removes or deactivates the Cadder-owned route and restores the original IIS binding exactly enough for IIS to own the domain again, with rollback or clear failure reporting when any step fails.
- [x] #6 The UI prevents destructive ambiguity: duplicate host bindings, HTTPS certificate bindings, unsupported ports, missing upstream/route information, or privilege limitations are shown inline and cannot be silently overwritten.
- [x] #7 Automated tests cover IIS provider parsing/state mapping, handoff and restore success/failure flows, non-Windows absence behavior, and TUI model/navigation behavior using fake IIS data rather than requiring a machine-local IIS installation.
- [x] #8 Windows documentation and the TUI smoke checklist explain the IIS handoff workflow, required privileges, supported binding types, rollback expectations, and the fact that the feature is Windows-only.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Add a Windows-only IIS handoff surface that lets users move supported local IIS host bindings behind Cadder from `cadder-tui` and restore them back to IIS. Keep the feature absent on non-Windows targets, preserve enough IIS metadata for restore/rollback, and cover behavior with fake-provider tests so default automation does not require a local IIS installation.

## Scope
- Add a dedicated IIS protocol, daemon, and TUI path instead of reusing generic domain activation toggles.
- Keep Windows-specific code behind narrow `cfg(windows)` boundaries.
- Use fake IIS provider coverage for default automated tests.
- Preserve HTTPS certificate metadata when HTTPS bindings are discovered.
- Do not broaden into OS service installation, CI/release pipeline changes, or mixed-elevation prompting.

## Key Files And Modules
- `crates/cadder-protocol/src/lib.rs`
- `crates/cadder-daemon/src/iis.rs`
- `crates/cadder-daemon/src/state.rs`
- `crates/cadder-daemon/src/ipc.rs`
- `crates/cadder-daemon/src/caddy.rs`
- `crates/cadder-daemon/src/lib.rs`
- `crates/cadder-tui/src/model.rs`
- `crates/cadder-tui/src/main.rs`
- `docs/ARCHITECTURE.md`
- `docs/verification/tui-smoke.md`
- `docs/site/src/content/docs/guides/windows.mdx`
- `docs/site/src/content/docs/cookbooks/windows/iis.mdx`

## Implementation Steps
1. Add protocol DTOs and message types for IIS discovery and handoff actions, including typed unavailable, privilege, unsupported shape, conflict, rollback, restore, and busy outcomes.
2. Add an `IisProvider` abstraction with system and fake implementations. Keep PowerShell/WebAdministration usage inside the provider module.
3. Discover IIS bindings explicitly through `query-iis-bindings` rather than during normal `query-state` refresh.
4. Persist restore metadata before mutating IIS. The record must include the original binding identity, protocol, IP address, port, host header, selected route host, loopback backend binding, and HTTPS certificate metadata when available.
5. Enable handoff as a transaction: preflight shape and conflicts, persist metadata, create a loopback IIS backend binding, remove the public IIS binding, add the Caddy reverse-proxy route, apply Caddy, and compensate on failure.
6. Restore handoff as the inverse transaction: remove the Caddy IIS route, restore the original IIS binding and certificate metadata, remove the loopback backend binding, and clear metadata only after cleanup succeeds.
7. Serialize IIS handoff mutations in the daemon so concurrent clients cannot interleave IIS/store/Caddy changes.
8. Add the Windows-only TUI view with explicit IIS route-host input for wildcard or empty-host bindings. Keep route-host input separate from the global search/filter state.
9. Update Caddy composition so generated config has both an HTTP front door on `:80` and an HTTPS front door on `:443` with TLS policy for active Cadder and IIS hosts.
10. Update architecture notes, smoke verification, and a dedicated Windows IIS cookbook page.

## Validation
- `cargo fmt --check`
- `cargo test -p cadder-protocol`
- `cargo test -p cadder-daemon`
- `cargo test -p cadder-tui`
- `cargo clippy --workspace --all-targets -- -D warnings`
- `cargo test --workspace`
- `cargo run -p xtask -- check`
- `cargo run -p xtask -- coverage`

## Assumptions
- Default automated tests must not depend on machine-local IIS or elevated Windows privileges.
- Real IIS smoke uses disposable local bindings only, and private hostnames or application paths must stay out of tracked task notes.
- Mixed-elevation prompting is follow-up scope.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the Windows IIS handoff protocol, daemon provider/store, IPC handling, Caddy route composition, and Windows-only TUI view. The daemon discovers IIS bindings outside `query-state`, persists restore metadata before mutation, serializes IIS handoff mutations, keeps rollback metadata when recovery fails, and restores HTTPS certificate metadata when the provider reports it.

Review follow-up fixed several issues before commit: generated Caddy config now exposes separate `:80` and `:443` front doors instead of HTTPS-only output; IIS route-host input no longer reuses the global TUI search/filter field; restore no longer reports success if loopback backend cleanup fails; concurrent IIS mutations return a busy issue; and private real-smoke hostnames, application paths, site names, and artifact names were removed from tracked task text.

Documentation was moved into a dedicated Windows IIS cookbook page and the general Windows guide now links to it. The architecture notes and TUI smoke checklist describe the proxy model, elevated same-context requirement, route-host input, rollback expectations, and restore constraints.

Validation completed before commit: `cargo fmt --check`, `cargo test -p cadder-protocol`, focused IIS daemon/TUI tests, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo test -p cadder-daemon`, `cargo test -p cadder-tui`, `cargo test --workspace`, `cargo run -p xtask -- check`, `bun run check`, `bun run build`, and `cargo run -p xtask -- coverage`. Coverage remained above the project threshold at 85.49698400209807% lines covered (6520/7626).
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented and reviewed the Windows IIS handoff model for `cadder-tui`.

Cadder can discover supported local IIS bindings, move a selected binding behind Cadder's Caddy front door, preserve restore metadata, proxy to a deterministic loopback IIS backend, hydrate persisted IIS routes on daemon startup, and restore the original binding later. HTTPS restore metadata now includes certificate details when the Windows provider can discover them. IIS handoff operations are serialized in the daemon and report typed safety, busy, rollback, and restore failures instead of silently interleaving or losing recovery state.

The TUI exposes the IIS Handoff view only on Windows. Wildcard or empty-host IIS rows use a dedicated route-host input instead of the global search filter. Documentation now lives under Cookbooks > Windows > IIS, with the general Windows guide linking to it.

Final validation passed: `cargo fmt --check`, `cargo test -p cadder-protocol`, focused IIS daemon/TUI tests, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo test -p cadder-daemon`, `cargo test -p cadder-tui`, `cargo test --workspace`, `cargo run -p xtask -- check`, `bun run check`, `bun run build`, and `cargo run -p xtask -- coverage`. Coverage remained above threshold at 85.49698400209807% lines covered (6520/7626).
<!-- SECTION:FINAL_SUMMARY:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Tests or explicit verification were run for the changed behavior
- [x] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
