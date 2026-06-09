using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI.Pages;

public sealed class InstancesPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Instances", "Entrypoint caddy.exe processes registered with the daemon.");
    AddConnectionBanners(state, filterText);

    var instances = state.Instances
        .Where(instance => MatchesFilter(instance.SearchText, filterText))
        .ToArray();
    if (state.ConnectionState == PanelConnectionState.Ready && instances.Length == 0)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelInstancesEmptyState",
          PanelStatusKind.Neutral,
          string.IsNullOrWhiteSpace(filterText) ? "No entrypoint processes" : "No matching entrypoint processes",
          string.IsNullOrWhiteSpace(filterText)
              ? "Start a project-local caddy.exe run command to register an entrypoint."
              : "No project path, config path, or domain matched the current filter."));
      return;
    }

    foreach (var instance in instances)
    {
      ContentPanel.Children.Add(CreateInstanceCard(instance));
    }
  }

  private UIElement CreateInstanceCard(PanelInstanceRow instance)
  {
    var stack = CreateCardStack();
    var header = new Grid
    {
      ColumnSpacing = 12
    };
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    header.Children.Add(CreateTitleStack(instance.ProjectName, instance.SourcePath));
    var badgeKind = instance.IsStaleOwner
        ? PanelStatusKind.Warning
        : instance.Diagnostics.Any(static diagnostic => diagnostic.Severity == PanelStatusKind.Error)
            ? PanelStatusKind.Error
            : PanelStatusKind.Success;
    var badge = CreateBadge(instance.ActivationState.ToString(), badgeKind);
    Grid.SetColumn(badge, 1);
    header.Children.Add(badge);
    stack.Children.Add(header);
    stack.Children.Add(CreateMetadataGrid(
        ("Project path", instance.SourcePath),
        ("Config path", instance.ConfigPath),
        ("Process status", instance.ProcessStatus),
        ("Owner executable", instance.OwnerExecutablePath),
        ("Age", instance.AgeLabel),
        ("Heartbeat", instance.LastHeartbeatLabel),
        ("Domains", $"{instance.ActiveDomainCount.ToString(CultureInfo.InvariantCulture)}/{instance.DomainCount.ToString(CultureInfo.InvariantCulture)} enabled"),
        ("Activation", instance.ActivationSummary)));

    if (instance.IsStaleOwner)
    {
      stack.Children.Add(CreateInfoBar(
          instance.AutomationId + "StaleOwner",
          PanelStatusKind.Warning,
          "Stale owner",
          "This registration has not sent a recent heartbeat."));
    }

    foreach (var diagnostic in instance.Diagnostics.Take(2))
    {
      stack.Children.Add(CreateInfoBar(
          diagnostic.AutomationId,
          diagnostic.Severity,
          diagnostic.Code,
          diagnostic.Message));
    }

    var card = CreateCard(instance.AutomationId, stack);
    AutomationProperties.SetName(card, $"{instance.ProjectName} entrypoint");
    return card;
  }
}
