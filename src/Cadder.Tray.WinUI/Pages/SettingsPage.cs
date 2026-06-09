namespace Cadder.Tray.WinUI.Pages;

public sealed class SettingsPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Settings", "Panel settings and current task scope.");
    AddConnectionBanners(state, filterText);

    var stack = CreateCardStack();
    stack.Children.Add(CreateTitleStack("No durable settings in this task", "TASK-1.9 only adds the panel surface."));
    stack.Children.Add(CreateMetadataGrid(
        ("Daemon status", state.Summary.DaemonStatus),
        ("Runtime status", state.Summary.RuntimeStatus),
        ("Config status", state.Summary.ConfigStatus)));
    ContentPanel.Children.Add(CreateCard("PanelSettingsScopeCard", stack));
  }
}
