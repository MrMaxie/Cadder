using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cadder.Tray.WinUI.Pages;

public abstract class PanelPageBase : UserControl
{
  protected PanelPageBase()
  {
    ContentPanel = new StackPanel
    {
      Padding = new Thickness(24),
      Spacing = 16,
      MaxWidth = 1040,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };

    var host = new Grid
    {
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    host.Children.Add(ContentPanel);

    Content = new ScrollViewer
    {
      VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
      HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
      Content = host
    };

    Loaded += OnLoaded;
    Unloaded += OnUnloaded;
  }

  protected StackPanel ContentPanel { get; }

  protected PanelStateStore? Store { get; private set; }

  protected abstract void Render(PanelState state, string filterText);

  protected void AddPageHeader(string title, string subtitle)
  {
    var stack = new StackPanel
    {
      Spacing = 4
    };
    stack.Children.Add(new TextBlock
    {
      Text = title,
      Style = TextStyle("TitleTextBlockStyle")
    });
    stack.Children.Add(new TextBlock
    {
      Text = subtitle,
      Style = TextStyle("CaptionTextBlockStyle"),
      Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
      TextWrapping = TextWrapping.WrapWholeWords
    });
    ContentPanel.Children.Add(stack);
  }

  protected void AddConnectionBanners(PanelState state, string filterText)
  {
    if (state.ConnectionState == PanelConnectionState.Loading)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelLoadingStateBanner",
          PanelStatusKind.Neutral,
          "Loading daemon state",
          state.ConnectionMessage));
    }
    else if (state.ConnectionState == PanelConnectionState.Disconnected)
    {
      ContentPanel.Children.Add(CreateInfoBar(
          "PanelDisconnectedStateBanner",
          PanelStatusKind.Error,
          "Daemon state unavailable",
          state.ConnectionMessage));
    }

    if (!string.IsNullOrWhiteSpace(filterText))
    {
      var clearButton = new Button
      {
        Content = "Clear",
        MinWidth = 72
      };
      AutomationProperties.SetAutomationId(clearButton, "PanelClearFilterButton");
      AutomationProperties.SetName(clearButton, "Clear filter");
      clearButton.Click += (_, _) => Store?.SetFilter(string.Empty);

      ContentPanel.Children.Add(CreateInfoBar(
          "PanelFilterStateBanner",
          PanelStatusKind.Neutral,
          "Filter active",
          $"Showing entries matching \"{filterText}\".",
          clearButton));
    }
  }

  protected ContentControl CreateCard(string automationId, UIElement content)
  {
    var surface = new Border
    {
      Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", "ControlFillColorSecondaryBrush"),
      BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush"),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(8),
      Padding = new Thickness(24),
      HorizontalAlignment = HorizontalAlignment.Stretch,
      Child = content
    };
    var card = new ContentControl
    {
      Content = surface,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    AutomationProperties.SetAutomationId(card, automationId);
    return card;
  }

  protected UIElement CreateInfoBar(
      string automationId,
      PanelStatusKind kind,
      string title,
      string message,
      Button? actionButton = null)
  {
    var content = new Grid
    {
      ColumnSpacing = 12
    };
    content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    if (actionButton is not null)
    {
      content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    var text = new StackPanel
    {
      Spacing = 2
    };
    text.Children.Add(new TextBlock
    {
      Text = title,
      Style = TextStyle("BodyStrongTextBlockStyle"),
      TextWrapping = TextWrapping.WrapWholeWords
    });
    text.Children.Add(new TextBlock
    {
      Text = message,
      Style = TextStyle("CaptionTextBlockStyle"),
      Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
      TextWrapping = TextWrapping.WrapWholeWords
    });
    content.Children.Add(text);

    if (actionButton is not null)
    {
      Grid.SetColumn(actionButton, 1);
      content.Children.Add(actionButton);
    }

    var surface = new Border
    {
      Background = ResourceBrush(BadgeBackgroundBrush(kind), "ControlFillColorSecondaryBrush"),
      BorderBrush = ResourceBrush(BadgeForegroundBrush(kind), "DividerStrokeColorDefaultBrush"),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(8),
      Padding = new Thickness(16, 12, 16, 12),
      HorizontalAlignment = HorizontalAlignment.Stretch,
      Child = content
    };
    var banner = new ContentControl
    {
      Content = surface,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    AutomationProperties.SetAutomationId(banner, automationId);
    AutomationProperties.SetName(banner, title);
    return banner;
  }

  protected Grid CreateMetadataGrid(params (string Label, string Value)[] rows)
  {
    var grid = new Grid
    {
      ColumnSpacing = 16,
      RowSpacing = 8
    };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    for (var index = 0; index < rows.Length; index++)
    {
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      AddMetadataRow(grid, index, rows[index].Label, rows[index].Value);
    }

    return grid;
  }

  protected StackPanel CreateCardStack()
  {
    return new StackPanel
    {
      Spacing = 12,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
  }

  protected StackPanel CreateTitleStack(string title, string subtitle)
  {
    var stack = new StackPanel
    {
      Spacing = 3
    };
    stack.Children.Add(new TextBlock
    {
      Text = title,
      Style = TextStyle("SubtitleTextBlockStyle"),
      TextWrapping = TextWrapping.WrapWholeWords
    });
    stack.Children.Add(new TextBlock
    {
      Text = subtitle,
      Style = TextStyle("CaptionTextBlockStyle"),
      Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
      TextWrapping = TextWrapping.WrapWholeWords
    });
    return stack;
  }

  protected TextBlock CreateBodyText(string text, bool strong = false)
  {
    return new TextBlock
    {
      Text = text,
      Style = strong ? TextStyle("BodyStrongTextBlockStyle") : null,
      TextWrapping = TextWrapping.WrapWholeWords
    };
  }

  protected TextBlock CreateCaptionText(string text)
  {
    return new TextBlock
    {
      Text = text,
      Style = TextStyle("CaptionTextBlockStyle"),
      Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
      TextWrapping = TextWrapping.WrapWholeWords
    };
  }

  protected Border CreateBadge(string text, PanelStatusKind kind)
  {
    var badge = new Border
    {
      Background = ResourceBrush(BadgeBackgroundBrush(kind), "ControlFillColorSecondaryBrush"),
      CornerRadius = new CornerRadius(4),
      Padding = new Thickness(8, 2, 8, 2),
      Child = new TextBlock
      {
        Text = text,
        Style = TextStyle("CaptionTextBlockStyle"),
        Foreground = ResourceBrush(BadgeForegroundBrush(kind), "TextFillColorPrimaryBrush")
      }
    };
    AutomationProperties.SetAccessibilityView(badge, AccessibilityView.Raw);
    return badge;
  }

  protected bool MatchesFilter(string searchText, string filterText)
  {
    return string.IsNullOrWhiteSpace(filterText)
        || searchText.Contains(filterText, StringComparison.OrdinalIgnoreCase);
  }

  protected Style TextStyle(string key)
  {
    return (Style)Application.Current.Resources[key];
  }

  protected Brush ResourceBrush(string key, string? fallbackKey = null)
  {
    if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
    {
      return brush;
    }

    if (fallbackKey is not null
        && Application.Current.Resources.TryGetValue(fallbackKey, out var fallback)
        && fallback is Brush fallbackBrush)
    {
      return fallbackBrush;
    }

    return (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
  }

  private void RenderCurrentState()
  {
    var state = Store?.CurrentState ?? PanelState.Loading(DateTimeOffset.UtcNow);
    var filterText = Store?.FilterText ?? string.Empty;
    ContentPanel.Children.Clear();
    Render(state, filterText);
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    AttachStore(Store ?? PanelStateStoreLocator.Current);
  }

  private void OnUnloaded(object sender, RoutedEventArgs e)
  {
    DetachStore();
  }

  private void AttachStore(PanelStateStore? store)
  {
    if (ReferenceEquals(Store, store))
    {
      RenderCurrentState();
      return;
    }

    DetachStore();
    Store = store;
    if (Store is not null)
    {
      Store.Changed += OnStoreChanged;
    }

    RenderCurrentState();
  }

  private void DetachStore()
  {
    if (Store is not null)
    {
      Store.Changed -= OnStoreChanged;
    }

    Store = null;
  }

  private void OnStoreChanged(object? sender, EventArgs e)
  {
    DispatcherQueue.TryEnqueue(RenderCurrentState);
  }

  private void AddMetadataRow(Grid grid, int row, string label, string value)
  {
    var labelBlock = new TextBlock
    {
      Text = label,
      Style = TextStyle("CaptionTextBlockStyle"),
      Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
      TextWrapping = TextWrapping.WrapWholeWords
    };
    Grid.SetRow(labelBlock, row);
    grid.Children.Add(labelBlock);

    var valueBlock = new TextBlock
    {
      Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
      TextWrapping = TextWrapping.WrapWholeWords
    };
    Grid.SetRow(valueBlock, row);
    Grid.SetColumn(valueBlock, 1);
    grid.Children.Add(valueBlock);
  }

  private static string BadgeBackgroundBrush(PanelStatusKind kind)
  {
    return kind switch
    {
      PanelStatusKind.Success => "SystemFillColorSuccessBackgroundBrush",
      PanelStatusKind.Warning => "SystemFillColorCautionBackgroundBrush",
      PanelStatusKind.Error => "SystemFillColorCriticalBackgroundBrush",
      _ => "ControlFillColorSecondaryBrush"
    };
  }

  private static string BadgeForegroundBrush(PanelStatusKind kind)
  {
    return kind switch
    {
      PanelStatusKind.Success => "SystemFillColorSuccessBrush",
      PanelStatusKind.Warning => "SystemFillColorCautionBrush",
      PanelStatusKind.Error => "SystemFillColorCriticalBrush",
      _ => "TextFillColorSecondaryBrush"
    };
  }
}
