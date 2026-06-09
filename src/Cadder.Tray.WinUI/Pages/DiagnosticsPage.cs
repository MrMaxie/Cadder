using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI.Pages;

public sealed class DiagnosticsPage : PanelPageBase
{
  protected override void Render(PanelState state, string filterText)
  {
    AddPageHeader("Diagnostics", "Runtime and config diagnostics currently visible to the tray daemon.");
    AddConnectionBanners(state, filterText);

    var diagnostics = state.Diagnostics
        .Where(diagnostic => MatchesFilter(
            string.Join(' ', diagnostic.Scope, diagnostic.Code, diagnostic.Message, diagnostic.DomainKey, string.Join(' ', diagnostic.SourceConfigPaths)),
            filterText))
        .ToArray();

    if (state.ConnectionState == PanelConnectionState.Ready && diagnostics.Length == 0)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelDiagnosticsEmptyState",
          PanelStatusKind.Success,
          string.IsNullOrWhiteSpace(filterText) ? "No diagnostics" : "No matching diagnostics",
          string.IsNullOrWhiteSpace(filterText)
              ? "No runtime or config diagnostics are currently reported."
              : "No diagnostic code, message, domain, or source path matched the current filter."));
      return;
    }

    foreach (var diagnostic in diagnostics)
    {
      ContentPanel.Children.Add(CreateDiagnosticCard(diagnostic));
    }
  }

  private UIElement CreateDiagnosticCard(PanelDiagnosticRow diagnostic)
  {
    var stack = CreateCardStack();
    var header = new Grid
    {
      ColumnSpacing = 12
    };
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    header.Children.Add(CreateTitleStack(diagnostic.Code, diagnostic.Message));
    var badge = CreateBadge(diagnostic.Scope, diagnostic.Severity);
    Grid.SetColumn(badge, 1);
    header.Children.Add(badge);
    stack.Children.Add(header);
    stack.Children.Add(CreateMetadataGrid(
        ("Scope", diagnostic.Scope),
        ("Domain", diagnostic.DomainKey ?? "-"),
        ("Source paths", diagnostic.SourceConfigPaths.Count == 0 ? "-" : string.Join(Environment.NewLine, diagnostic.SourceConfigPaths))));

    var card = CreateCard(diagnostic.AutomationId, stack);
    AutomationProperties.SetName(card, $"{diagnostic.Scope} diagnostic {diagnostic.Code}");
    return card;
  }
}
