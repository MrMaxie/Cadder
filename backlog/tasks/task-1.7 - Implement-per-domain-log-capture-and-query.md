---
id: TASK-1.7
title: Implement per-domain log capture and query
status: Done
assignee:
  - '@agent'
created_date: '2026-06-09 11:43'
updated_date: '2026-06-09 21:06'
labels: []
dependencies:
  - TASK-1.5
  - TASK-1.6
modified_files:
  - src/Cadder.Contracts/DomainContracts.cs
  - src/Cadder.Contracts/IpcContracts.cs
  - src/Cadder.Contracts/IpcPipeProtocol.cs
  - src/Cadder.Daemon/DaemonBoundaries.cs
  - src/Cadder.Daemon/BoundedCaddyLogSink.cs
  - src/Cadder.Daemon/CaddyLogRedactor.cs
  - src/Cadder.Daemon/CaddyRuntimeLogParser.cs
  - src/Cadder.Daemon/InMemoryCaddyLogStore.cs
  - src/Cadder.Daemon/CadderIpcEndpoint.cs
  - src/Cadder.Daemon/NamedPipeDaemonIpcServer.cs
  - src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs
  - src/Cadder.Daemon/CaddyConfigCoordinator.cs
  - src/Cadder.Daemon/CaddyJsonConfigComposer.cs
  - src/Cadder.Tray.WinUI/App.xaml.cs
  - tests/Cadder.Contracts.Tests/ContractShapeTests.cs
  - tests/Cadder.Daemon.Tests/CaddyLogStoreTests.cs
  - tests/Cadder.Daemon.Tests/CadderIpcEndpointTests.cs
  - tests/Cadder.Daemon.Tests/NamedPipeDaemonIpcServerTests.cs
  - tests/Cadder.Daemon.Tests/RealCaddyRuntimeAdapterTests.cs
  - tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs
  - docs/ARCHITECTURE.md
parent_task_id: TASK-1
priority: medium
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Capture and expose logs in a way the GUI can filter per domain. Logs should help the user understand which source instance and hostname produced a request or runtime error.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cadder stores or streams recent Caddy runtime logs with timestamps, severity, domain, source instance, and raw message fields.
- [x] #2 Each registered domain can query its own recent log lines without loading all logs into the GUI upfront.
- [x] #3 Log capture survives config reloads and clearly marks reload, validation, and runtime errors.
- [x] #4 Sensitive values such as tokens, environment secrets, and full command arguments are redacted before logs are exposed in diagnostics or GUI views.
- [x] #5 A bounded retention policy prevents unbounded disk or memory growth.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Goal
Implement daemon-side per-domain Caddy log capture and query so future GUI surfaces can request recent log lines lazily without embedding log payloads in `GuiStateSnapshot`.

## Scope
- Capture recent Cadder-owned Caddy runtime output and runtime-control operation output with structured timestamps, severity, stream identity, domain/source attribution when known, and redacted raw message text.
- Expose recent logs through explicit IPC query contracts keyed by the existing `LogStreamIdentity`, not by GUI snapshot payloads.
- Keep log storage bounded in memory with clear retention and cursor gap metadata.
- Preserve the TASK-1.10 boundary: implement backend capture/query contracts and minimal wiring only, not the full logs UI.

## Key Files And Modules
- `src/Cadder.Contracts/DomainContracts.cs`
- `src/Cadder.Contracts/IpcContracts.cs`
- `src/Cadder.Contracts/IpcPipeProtocol.cs`
- `src/Cadder.Daemon/DaemonBoundaries.cs`
- `src/Cadder.Daemon/CadderIpcEndpoint.cs`
- `src/Cadder.Daemon/NamedPipeDaemonIpcServer.cs`
- `src/Cadder.Daemon/ProcessRealCaddyRuntimeAdapter.cs`
- `src/Cadder.Daemon/CaddyConfigCoordinator.cs`
- `src/Cadder.Daemon/CaddyJsonConfigComposer.cs`
- `src/Cadder.Tray.WinUI/App.xaml.cs`
- `tests/Cadder.Contracts.Tests/ContractShapeTests.cs`
- `tests/Cadder.Daemon.Tests/CadderIpcEndpointTests.cs`
- `tests/Cadder.Daemon.Tests/NamedPipeDaemonIpcServerTests.cs`
- `tests/Cadder.Daemon.Tests/RealCaddyRuntimeAdapterTests.cs`
- `tests/Cadder.Daemon.Tests/CaddyConfigCoordinatorTests.cs`
- `docs/ARCHITECTURE.md`

## Implementation Steps
1. Extend shared contracts with log DTOs and query DTOs: `CaddyLogEntry`, severity, attribution kind, stream status, opaque cursor metadata, and `QueryCaddyLogsRequest` / `QueryCaddyLogsResponse`. Use the existing `LogStreamIdentity` as the canonical selector and attribution object to avoid duplicating stream identity fields.
2. Add daemon logging boundaries in `DaemonBoundaries.cs`: `ICaddyLogSink`, `ICaddyLogStore`, and a shared redaction boundary. Implement a bounded in-memory ring buffer with `TimeProvider`, stable monotonic sequence numbers, per-stream/global caps, age pruning, and response metadata such as `NextCursor`, `HasGap`, `HasMoreBefore`, and `TruncatedByRetention`.
3. Add a central `CaddyLogRedactor` and route both log entries and runtime/config diagnostics through it before they are stored or exposed. Full command arguments must not be returned in log or diagnostic surfaces; expose only safe summaries or placeholders.
4. Update `ProcessRealCaddyRuntimeAdapter` so the Cadder-owned long-lived `caddy run` process redirects stdout/stderr and drains both streams through background reader tasks. Give each owned runtime session a stable session identity, cancellation token source, and awaited cleanup/drain path on stop, restart, or process exit.
5. Feed process output into a bounded ingestion path so slow parsing/redaction/storage cannot block Caddy's stdout/stderr pipes. Record explicit dropped/overflow markers when retention or ingestion limits discard entries.
6. Capture validate/reload/version/start/stop operation stdout/stderr as structured runtime-control log entries, and keep existing structured diagnostics consistent with the same redacted messages.
7. Parse Caddy JSON log lines when possible to derive timestamp, severity, raw message, request host/domain, and operation context. For global runtime or unparsable lines, store entries with explicit unknown/null domain attribution rather than guessing.
8. Update the Cadder-owned composed Caddy JSON to enable controlled runtime/access logging to stdout/stderr in a shape Cadder can parse. Keep this limited to Cadder's generated output and avoid broad advanced Caddy JSON merging beyond the current TASK-1.5 composer scope.
9. Add `QueryCaddyLogsAsync` to `CadderIpcEndpoint` and dispatch it through `NamedPipeDaemonIpcServer`. The query should validate requested limits, filter by `LogStreamIdentity`, optional severity/time/cursor filters, and return stream lifecycle status so callers can distinguish empty, stale/removed, read-error, and retention-gap states.
10. Wire one shared log store/sink/redactor through the daemon composition root in `App.xaml.cs`, keeping `App.xaml.cs` as wiring only and daemon abstractions testable in isolation.
11. Update architecture documentation with the log capture pipeline, retention policy, redaction policy, domain attribution limits, IPC query contract, and TASK-1.10 UI boundary.

## Validation
- Add contract shape tests for new log DTOs, query DTOs, cursor metadata, stream status, and IPC message type constants.
- Add daemon unit tests for bounded retention, per-stream filtering, cursor gaps after wrap/prune, severity/time filters, and redaction of token-like values and full command arguments.
- Add runtime adapter tests with fake managed process stdout/stderr streams to verify reader lifecycle, session separation across restarts, stop/drain behavior, overflow markers, and validate/reload error capture.
- Add IPC endpoint and named-pipe tests for log query dispatch, empty result, unknown/stale stream status, cursor paging, and no logs embedded in `GuiStateSnapshot`.
- Add composer/coordinator tests that verify controlled logging config is present in Cadder-owned output and that `LogStreamIdentity` stays stable for canonical domains.
- Run `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`.
- Run `./build.ps1`.
- Run `dotnet format Cadder.slnx --verify-no-changes`.

## Assumptions
- TASK-1.7 uses bounded in-memory retention only; durable log persistence across daemon restart is out of scope unless explicitly added later.
- Runtime-global, validation, reload, and unparsable log lines may have null/unknown domain attribution, but must still carry stream/source/operation context where available.
- Domain-scoped queries are per current user daemon context. If stronger authorization or cross-user isolation becomes relevant, handle it as a follow-up because the current IPC pipe is already per-user.
- TASK-1.10 owns the full log UI, lazy tailing UX, pause/auto-scroll behavior, copy actions, and severity filter controls.

## Risks And Boundaries
- Redirecting long-lived Caddy stdout/stderr can deadlock if readers block; use bounded asynchronous ingestion and cheap reader loops.
- Heuristic domain attribution from log lines is imperfect; the implementation must be explicit about unknown attribution instead of inventing a domain.
- Redaction must be shared with diagnostics, not only log query responses, or the same sensitive value could leak through GUI snapshots.
- Avoid broad Caddy JSON merge work. If controlled logging requires composer behavior beyond the current generated `apps.http.servers.srv0` scope, pause and ask whether to expand the task or create a follow-up.
- Do not implement the final logs page UI in this task beyond any tiny contract/wiring needed to keep the existing placeholder honest.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Plan approved by the user and recorded before implementation. Planning review incorporated code-mapper findings and adversarial review corrections: use `LogStreamIdentity` as the canonical query selector, include cursor/retention gap metadata, centralize redaction across logs and diagnostics, and model runtime reader lifecycle with bounded asynchronous ingestion.

Started implementation. Verified repository instructions, `.local` exclusion, Backlog MCP availability, task status/assignee, approved plan, architecture notes, and current contract/runtime/IPC/test structure before code edits. Subagent execution was skipped because the available multi-agent tool requires an explicit user request to spawn agents.

Implemented log contracts, IPC query dispatch, bounded in-memory log store, shared redaction, runtime stdout/stderr capture, runtime-control operation logging, controlled generated Caddy JSON logging, WinUI composition wiring, and architecture documentation. Focused validation currently passes: contracts tests, daemon tests, and WinUI project build.

Acceptance criteria verified through implementation and tests. Validation passed: `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`, `./build.ps1`, and `dotnet format Cadder.slnx --verify-no-changes`. `git diff --check` is clean.

Closeout verified: all acceptance criteria are checked, there are no task-specific Definition of Done items, the recorded implementation plan still reflects the delivered solution, documentation was updated, and validation was run after the final code/format changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
## Summary
- Added shared Caddy log contracts and lazy IPC query support keyed by `LogStreamIdentity`, including cursor, severity/time filters, stream status, and retention gap metadata.
- Implemented daemon-side bounded log capture with shared redaction, in-memory retention caps, runtime stdout/stderr parsing, runtime-control operation logs, and generated Caddy JSON logging to stdout in JSON format.
- Wired the shared log store/sink/redactor through the WinUI daemon composition root and updated architecture documentation for the capture/query pipeline and TASK-1.10 UI boundary.

## Impact
- GUI and future diagnostics can query per-domain logs without loading logs into `GuiStateSnapshot`.
- Runtime, validation, reload, start/stop, and unparsable log lines are preserved with explicit attribution and redacted before exposure.
- Log retention is bounded by memory limits and reports cursor gaps when older entries have been pruned.

## Validation
- `dotnet test tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- `dotnet test tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- `dotnet build src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- `dotnet test Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- `./build.ps1`
- `dotnet format Cadder.slnx --verify-no-changes`
- `git diff --check`

## Risks And Follow-ups
- Domain attribution depends on Caddy JSON log host fields; unknown/unparsable lines intentionally stay on runtime streams instead of guessing a domain.
- Full logs UI behavior remains scoped to TASK-1.10.
<!-- SECTION:FINAL_SUMMARY:END -->
