using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI.Pages;

public sealed class OverviewPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Overview", "Full daemon, runtime, config, and registration state.");
    AddConnectionBanners(state, filterText);

    var overview = CreateCardStack();
    var titleRow = new Grid
    {
      ColumnSpacing = 12
    };
    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    titleRow.Children.Add(CreateTitleStack(state.Summary.DaemonStatus, state.Summary.DaemonDetail));
    var statusBadge = CreateBadge(state.Summary.DaemonStatus, state.Summary.StatusKind);
    Grid.SetColumn(statusBadge, 1);
    titleRow.Children.Add(statusBadge);
    overview.Children.Add(titleRow);
    overview.Children.Add(CreateMetadataGrid(
        ("Runtime", state.Summary.RuntimeStatus),
        ("Config", state.Summary.ConfigStatus),
        ("Entrypoints", $"{state.Summary.ActiveEntrypointCount.ToString(CultureInfo.InvariantCulture)}/{state.Summary.EntrypointCount.ToString(CultureInfo.InvariantCulture)} active"),
        ("Domains", $"{state.Summary.ActiveDomainCount.ToString(CultureInfo.InvariantCulture)}/{state.Summary.DomainCount.ToString(CultureInfo.InvariantCulture)} enabled"),
        ("Captured", state.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)),
        ("Diagnostic", state.Summary.FirstDiagnostic)));
    ContentPanel.Children.Add(CreateCard("PanelOverviewCard", overview));

    if (state.ConnectionState == PanelConnectionState.Ready && state.Instances.Count == 0)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelEmptyStateBanner",
          PanelStatusKind.Neutral,
          "No entrypoints registered",
          "Cadder is running and waiting for project-local caddy.exe invocations."));
    }

    var runtime = CreateCardStack();
    runtime.Children.Add(CreateTitleStack("Runtime", "Real Caddy process and admin endpoint."));
    runtime.Children.Add(CreateMetadataGrid(
        ("Status", state.Summary.RuntimeStatus),
        ("Diagnostic", FirstRuntimeDiagnostic(state))));
    ContentPanel.Children.Add(CreateCard("PanelRuntimeOverviewCard", runtime));

    var config = CreateCardStack();
    config.Children.Add(CreateTitleStack("Config", "Effective Caddy config application state."));
    config.Children.Add(CreateMetadataGrid(
        ("Status", state.Summary.ConfigStatus),
        ("Diagnostics", state.Diagnostics.Count.ToString(CultureInfo.InvariantCulture))));
    if (state.Diagnostics.Any(static diagnostic => diagnostic.Scope is "Config" or "Domain"))
    {
      foreach (var diagnostic in state.Diagnostics.Where(static diagnostic => diagnostic.Scope is "Config" or "Domain").Take(3))
      {
        config.Children.Add(CreateInfoBar(
            diagnostic.AutomationId,
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message));
      }
    }

    ContentPanel.Children.Add(CreateCard("PanelConfigOverviewCard", config));
  }

  private static string FirstRuntimeDiagnostic(PanelState state)
  {
    return state.Diagnostics.FirstOrDefault(static diagnostic => diagnostic.Scope == "Runtime")?.Message ?? "-";
  }
}
