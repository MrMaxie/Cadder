---
id: TASK-1.21
title: Add Windows IIS domain handoff controls to cadder-tui
status: To Do
assignee: []
created_date: '2026-06-11 07:47'
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
- [ ] #1 On Windows, `cadder-tui` exposes an IIS handoff view that lists discoverable IIS site bindings with site name, protocol, IP address, port, host header/domain, and current handoff state.
- [ ] #2 On non-Windows platforms, the IIS handoff view and related commands are not present in the TUI, and the workspace continues to build and test without Windows-only dependencies leaking into other targets.
- [ ] #3 The daemon/protocol exposes platform-gated IIS handoff state and commands through a small abstraction that can report IIS unavailable, insufficient privileges, unsupported binding shape, conflicts, and successful handoff/restore outcomes without panics.
- [ ] #4 Turning a supported IIS domain `on` for Cadder preserves enough original IIS binding metadata to restore it later, releases or disables the IIS-owned binding safely, and creates or activates the Cadder-owned route for the same domain only when a safe handoff plan exists.
- [ ] #5 Turning a handed-off domain `off` removes or deactivates the Cadder-owned route and restores the original IIS binding exactly enough for IIS to own the domain again, with rollback or clear failure reporting when any step fails.
- [ ] #6 The UI prevents destructive ambiguity: duplicate host bindings, HTTPS certificate bindings, unsupported ports, missing upstream/route information, or privilege limitations are shown inline and cannot be silently overwritten.
- [ ] #7 Automated tests cover IIS provider parsing/state mapping, handoff and restore success/failure flows, non-Windows absence behavior, and TUI model/navigation behavior using fake IIS data rather than requiring a machine-local IIS installation.
- [ ] #8 Windows documentation and the TUI smoke checklist explain the IIS handoff workflow, required privileges, supported binding types, rollback expectations, and the fact that the feature is Windows-only.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Tests or explicit verification were run for the changed behavior
- [ ] #2 Coverage was measured and remains at or above the project threshold
<!-- DOD:END -->
