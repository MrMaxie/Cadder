# Cadder Debugging Guide

This file records recurring debugging lessons for agents working in Cadder. Read it before investigating WinUI, UI Automation, Windows input, process lifetime, or flaky validation problems.

## Debugging Loop Discipline

- Reproduce the failure with one exact command before changing code. Preserve the command, exit code, and the important stdout/stderr lines in notes or the final summary.
- Change one variable at a time. If a probe fails, write down what it ruled out before trying the next probe.
- After two similar failed attempts, add instrumentation or inspect the lower-level boundary instead of making another speculative UI or lifecycle change.
- After three similar failed attempts, stop the loop and record the blocker, evidence, and next diagnostic step. Do not keep reshaping unrelated code to appease a flaky tool.
- When a process exits unexpectedly, inspect process exit code, Windows Event Log/WER evidence when available, and any app logs before assuming the UI layout is at fault.
- Keep verification artifacts under `.local/verification/<task-id>/`. Remove temporary scripts, helper processes, and app instances before closeout.
- If a fallback verification path is used, say exactly what was and was not verified. Do not claim UIA, browser, or child-control automation passed when only screenshots or process liveness passed.

## WinUI And UI Automation

- Prefer `winapp run <output-folder> --detach --json` from the built app output directory. Target the app by PID or HWND, not by a broad process-title search.
- Use stable `AutomationProperties.AutomationId` values for controls that tests must locate or invoke. AutomationId is useful for reliable lookup, but it is not globally unique across the whole automation tree; scope searches to the app window or an appropriate container.
- Avoid broad descendant UIA searches when a narrower container or known AutomationId is available. Deep UIA searches can traverse very large trees and can destabilize fragile apps or tools.
- UIA traversal can execute provider code in the target process. If `winapp ui inspect` or `wait-for` crashes the app, treat that as an interop/provider crash until proven otherwise; capture evidence instead of blindly rewriting unrelated UI structure.
- Reduce unnecessary UI tree churn during polling. Do not rebuild page content on every timer tick when only timestamps or age labels changed unless the visual update is required.
- Prefer control patterns through UIA first (`Invoke`, `SelectionItem`, `ExpandCollapse`, `Value`) when they are available. Use screenshots as evidence for layout only, not as proof that automation paths work.

## Windows Input And Clicking

- Do not simulate user mouse or keyboard input with `PostMessage` or `SendMessage`. Posted messages are not real input messages, do not update the input manager's hidden state, and can be processed out of order relative to real input.
- Windows mouse messages are synthesized from input state when the target retrieves messages. Posting `WM_MOUSE*` messages bypasses that path and is not a reliable click.
- If UIA control patterns are unavailable and a real click is required, use `SendInput` with `INPUT`/`MOUSEINPUT` rather than posted messages.
- For `SendInput`, verify the target window is foreground or intentionally clickable, compute coordinates from the actual window/client bounds, account for DPI scaling, and use button down/up events as a pair.
- For absolute mouse coordinates, normalize to the documented `0..65535` range. Use `MOUSEEVENTF_VIRTUALDESK` when coordinates should map to the full virtual desktop instead of only the primary monitor.
- Check integrity levels. `SendInput` is subject to UIPI and can inject input only into applications at an equal or lower integrity level; UIPI blocking is not reliably distinguished by the return value or `GetLastError`.
- Prefer this order for interaction attempts:
  1. UIA pattern by stable AutomationId.
  2. `winapp ui click` or equivalent tool-backed real mouse simulation.
  3. A small local `SendInput` helper with logged target bounds and coordinates.
  4. Screenshot-only fallback with an explicit statement that interaction automation was not verified.

## References

- Microsoft Learn: `SendInput` function - https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- Microsoft Learn: `MOUSEINPUT` structure - https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput
- The Old New Thing: why mouse move messages are synthesized from input state - https://devblogs.microsoft.com/oldnewthing/?p=42343
- The Old New Thing: posted messages are processed ahead of input messages - https://devblogs.microsoft.com/oldnewthing/?p=4203
- The Old New Thing: do not simulate keyboard input with `PostMessage` - https://devblogs.microsoft.com/oldnewthing/?p=35513
- Microsoft Learn: use the `AutomationId` property - https://learn.microsoft.com/pl-pl/dotnet/framework/ui-automation/use-the-automationid-property
