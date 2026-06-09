using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI.Pages;

public sealed class DomainsPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Domains", "Domains grouped by entrypoint instance.");
    AddConnectionBanners(state, filterText);

    var renderedAny = false;
    foreach (var group in state.DomainGroups)
    {
      var groupMatches = MatchesFilter(group.SearchText, filterText);
      var domains = group.Domains
          .Where(domain => groupMatches || MatchesFilter(domain.SearchText, filterText))
          .ToArray();
      if (domains.Length == 0)
      {
        continue;
      }

      renderedAny = true;
      ContentPanel.Children.Add(CreateDomainGroup(group, domains));
    }

    if (state.ConnectionState == PanelConnectionState.Ready && !renderedAny)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelDomainsEmptyState",
          PanelStatusKind.Neutral,
          string.IsNullOrWhiteSpace(filterText) ? "No domains registered" : "No matching domains",
          string.IsNullOrWhiteSpace(filterText)
              ? "Cadder has no adapted host matchers to show yet."
              : "No hostname, source path, or config path matched the current filter."));
    }
  }

  private Expander CreateDomainGroup(PanelDomainGroup group, IReadOnlyList<PanelDomainRow> domains)
  {
    var header = new StackPanel
    {
      Spacing = 2
    };
    header.Children.Add(CreateBodyText(group.ProjectName, strong: true));
    header.Children.Add(CreateCaptionText($"{group.SourcePath} · {domains.Count} domains"));

    var domainStack = new StackPanel
    {
      Spacing = 8,
      Padding = new Thickness(0, 12, 0, 0)
    };
    foreach (var domain in domains)
    {
      domainStack.Children.Add(CreateDomainRow(domain));
    }

    var expander = new Expander
    {
      Header = header,
      Content = domainStack,
      IsExpanded = true,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    AutomationProperties.SetAutomationId(expander, group.AutomationId);
    AutomationProperties.SetName(expander, $"{group.ProjectName} domains");
    return expander;
  }

  private UIElement CreateDomainRow(PanelDomainRow domain)
  {
    var stack = CreateCardStack();
    var header = new Grid
    {
      ColumnSpacing = 12
    };
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    header.Children.Add(CreateTitleStack(domain.Hostname, domain.EnabledState));
    var badge = CreateBadge(domain.ConflictState, domain.IsConflicted ? PanelStatusKind.Error : PanelStatusKind.Success);
    Grid.SetColumn(badge, 1);
    header.Children.Add(badge);
    stack.Children.Add(header);
    stack.Children.Add(CreateMetadataGrid(
        ("Hostname", domain.Hostname),
        ("Upstream target", domain.UpstreamTarget),
        ("Enabled state", domain.EnabledState),
        ("Conflict state", domain.ConflictState),
        ("Last error", domain.LastError)));

    var card = CreateCard(domain.AutomationId, stack);
    AutomationProperties.SetName(card, $"{domain.Hostname} domain");
    return card;
  }
}
