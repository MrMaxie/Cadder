namespace Cadder.Tray.WinUI.Pages;

public sealed class LogsPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Logs", "Domain log capture status.");
    AddConnectionBanners(state, filterText);
    ContentPanel.Children.Add(CreateInfoBar(
        "PanelLogsUnavailableState",
        PanelStatusKind.Neutral,
        "Logs are not available yet",
        "Per-domain log capture and tailing are owned by TASK-1.7 and TASK-1.10. This panel will surface logs when that backend data exists."));
  }
}
