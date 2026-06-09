using System.ComponentModel;
using System.Runtime.InteropServices;
using Cadder.Contracts;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;

namespace Cadder.Tray.WinUI;

public sealed partial class TrayPopupWindow : Window
{
  private const int PopupWidthViewUnits = 360;
  private const int PopupMinHeightViewUnits = 460;
  private const int PopupMaxHeightViewUnits = 640;
  private const int SwHide = 0;
  private const uint MonitorDefaultToNearest = 2;
  private const int GwlStyle = -16;
  private const int GwlExStyle = -20;
  private const int WsCaption = 0x00C00000;
  private const int WsThickFrame = 0x00040000;
  private const int WsSysMenu = 0x00080000;
  private const int WsExToolWindow = 0x00000080;
  private const uint SwpNoMove = 0x0002;
  private const uint SwpNoSize = 0x0001;
  private const uint SwpNoZOrder = 0x0004;
  private const uint SwpFrameChanged = 0x0020;
  private readonly App _app;
  private readonly TrayPopupStateBuilder _stateBuilder = new();
  private readonly List<Control> _focusableControls = [];
  private readonly nint _windowHandle;
  private bool _popupStyleApplied;

  public TrayPopupWindow(App app)
  {
    _app = app ?? throw new ArgumentNullException(nameof(app));

    InitializeComponent();
    _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
    AppWindow.SetIcon("Assets/AppIcon.ico");
    AppWindow.Title = "Cadder tray popup";
    Activated += OnActivated;
  }

  public bool IsOpen { get; private set; }

  public async ValueTask ShowAtCursorAsync(CancellationToken cancellationToken = default)
  {
    await RefreshStateAsync(cancellationToken);
    ApplyPopupStyle();
    PositionAtCursor();
    ApplyRoundedWindowRegion();
    IsOpen = true;
    Activate();
    SetForegroundWindow(_windowHandle);
    _focusableControls.FirstOrDefault()?.Focus(FocusState.Keyboard);
  }

  public void Dismiss()
  {
    IsOpen = false;
    ShowWindow(_windowHandle, SwHide);
  }

  private async ValueTask RefreshStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var snapshot = await _app.QueryGuiStateAsync(cancellationToken);
      if (snapshot is null)
      {
        RenderUnavailableState("State snapshot is unavailable.");
        return;
      }

      RenderState(_stateBuilder.Build(snapshot));
    }
    catch (Exception ex)
    {
      RenderUnavailableState(ex.Message);
    }
  }

  private void RenderUnavailableState(string message)
  {
    MenuPanel.Children.Clear();
    _focusableControls.Clear();
    AddBrandHeader();
    AddSeparator();
    AddTextRow("Daemon", "Unavailable", message);
    AddSeparator();
    AddActionRows();
    SizeToContent();
  }

  private void RenderState(TrayPopupState state)
  {
    MenuPanel.Children.Clear();
    _focusableControls.Clear();

    AddBrandHeader();
    AddSummaryRow(state);

    if (state.Entrypoints.Count > 0)
    {
      AddSeparator();
      foreach (var group in state.Entrypoints)
      {
        AddEntrypointGroup(group);
      }
    }
    else
    {
      AddSeparator();
      AddTextRow("Entrypoints", "No registrations", "Waiting for project-local caddy.exe invocations.");
    }

    AddSeparator();
    AddActionRows();
    SizeToContent();
  }

  private void AddBrandHeader()
  {
    var grid = new Grid
    {
      Padding = new Thickness(12, 10, 12, 8),
      ColumnSpacing = 10,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    var image = new Image
    {
      Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-48_altform-lightunplated.png")),
      Width = 28,
      Height = 28,
      VerticalAlignment = VerticalAlignment.Center
    };
    AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
    grid.Children.Add(image);

    var title = new TextBlock
    {
      Text = "Cadder",
      FontSize = 18,
      FontWeight = FontWeights.SemiBold,
      VerticalAlignment = VerticalAlignment.Center,
      IsTextSelectionEnabled = false
    };
    AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
    Grid.SetColumn(title, 1);
    grid.Children.Add(title);

    AutomationProperties.SetName(grid, "Cadder");
    MenuPanel.Children.Add(grid);
  }

  private void AddSummaryRow(TrayPopupState state)
  {
    var details = $"{state.RuntimeStatus} · {state.ActiveEntrypointCount}/{state.EntrypointCount} entrypoints · {state.ActiveDomainCount} domains";
    var row = BuildFlyoutRow(
        "Daemon",
        state.DaemonStatus,
        details,
        BuildDetailsPanel(
            ("Runtime", state.RuntimeStatus),
            ("Config", state.ConfigStatus),
            ("Entrypoints", state.EntrypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Active domains", state.ActiveDomainCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Diagnostic", state.Diagnostic)),
        "TrayPopupDaemonStateRow");
    MenuPanel.Children.Add(row);
    _focusableControls.Add(row);
  }

  private void AddEntrypointGroup(TrayPopupEntrypointGroup group)
  {
    var header = BuildFlyoutRow(
        group.ProjectName,
        group.ActivationState.ToString(),
        group.SourcePath,
        BuildDetailsPanel(
            ("Registration", group.RegistrationId),
            ("Source", group.SourcePath),
            ("Domains", group.Domains.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        "TrayPopupEntrypointRow" + SanitizeAutomationPart(group.RegistrationId));
    MenuPanel.Children.Add(header);
    _focusableControls.Add(header);

    foreach (var domain in group.Domains)
    {
      AddDomainToggleRow(domain);
    }
  }

  private void AddDomainToggleRow(TrayPopupDomainRow domain)
  {
    var grid = new Grid
    {
      Padding = new Thickness(28, 5, 12, 5),
      ColumnSpacing = 8,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

    var labels = new StackPanel { Spacing = 1 };
    labels.Children.Add(new TextBlock
    {
      Text = domain.DisplayName,
      FontSize = 13,
      FontWeight = FontWeights.SemiBold,
      TextTrimming = TextTrimming.CharacterEllipsis,
      IsTextSelectionEnabled = false
    });
    labels.Children.Add(new TextBlock
    {
      Text = domain.ActivationState.ToString(),
      Style = CaptionStyle(),
      Foreground = SecondaryTextBrush(),
      FontSize = 11,
      IsTextSelectionEnabled = false
    });
    grid.Children.Add(labels);

    var toggle = new ToggleSwitch
    {
      IsOn = domain.IsEnabled,
      OnContent = string.Empty,
      OffContent = string.Empty,
      MinWidth = 0,
      Width = 48,
      HorizontalAlignment = HorizontalAlignment.Right,
      VerticalAlignment = VerticalAlignment.Center,
      Tag = domain
    };
    AutomationProperties.SetAutomationId(toggle, domain.AutomationId);
    AutomationProperties.SetName(toggle, $"{domain.DisplayName} domain");
    ToolTipService.SetToolTip(toggle, domain.IsEnabled ? "Disable domain" : "Enable domain");
    toggle.Toggled += DomainToggle_Toggled;
    Grid.SetColumn(toggle, 1);
    grid.Children.Add(toggle);

    MenuPanel.Children.Add(grid);
    _focusableControls.Add(toggle);
  }

  private void AddActionRows()
  {
    AddActionButton("Open panel", "TrayPopupOpenPanelButton", () =>
    {
      _app.ActivateMainWindow();
      Dismiss();
      return ValueTask.CompletedTask;
    });
    AddActionButton("Refresh", "TrayPopupRefreshButton", () => RefreshStateAsync());
    AddActionButton("Quit daemon", "TrayPopupQuitDaemonButton", async () =>
    {
      IsOpen = false;
      await _app.QuitDaemonAsync();
    });
  }

  private void AddActionButton(string text, string automationId, Func<ValueTask> action)
  {
    var button = BuildPlainButton(text, automationId);
    button.Tag = action;
    button.Click += async (_, _) => await action();
    MenuPanel.Children.Add(button);
    _focusableControls.Add(button);
  }

  private Button BuildFlyoutRow(
      string title,
      string badge,
      string detail,
      UIElement flyoutContent,
      string automationId)
  {
    var row = new Grid
    {
      Padding = new Thickness(12, 7, 12, 7),
      ColumnSpacing = 8,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

    var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
    {
      Width = 8,
      Height = 8,
      Fill = StatusBrush(badge),
      VerticalAlignment = VerticalAlignment.Center,
      Margin = new Thickness(0, 0, 2, 0)
    };
    AutomationProperties.SetAccessibilityView(dot, AccessibilityView.Raw);
    row.Children.Add(dot);

    var labels = new StackPanel { Spacing = 1 };
    labels.Children.Add(new TextBlock
    {
      Text = title,
      FontSize = 13,
      FontWeight = FontWeights.SemiBold,
      TextTrimming = TextTrimming.CharacterEllipsis,
      IsTextSelectionEnabled = false
    });
    labels.Children.Add(new TextBlock
    {
      Text = detail,
      Style = CaptionStyle(),
      Foreground = SecondaryTextBrush(),
      FontSize = 11,
      TextTrimming = TextTrimming.CharacterEllipsis,
      IsTextSelectionEnabled = false
    });
    Grid.SetColumn(labels, 1);
    row.Children.Add(labels);

    var chip = new Border
    {
      Background = ResourceBrush("ControlFillColorSecondaryBrush"),
      CornerRadius = new CornerRadius(4),
      Padding = new Thickness(6, 1, 6, 1),
      VerticalAlignment = VerticalAlignment.Center,
      Child = new TextBlock
      {
        Text = badge,
        FontSize = 10,
        Foreground = SecondaryTextBrush(),
        IsTextSelectionEnabled = false
      }
    };
    Grid.SetColumn(chip, 2);
    row.Children.Add(chip);

    var chevron = new FontIcon
    {
      Glyph = "\uE974",
      FontSize = 12,
      Opacity = 0.7,
      VerticalAlignment = VerticalAlignment.Center,
      Margin = new Thickness(4, 0, 0, 0)
    };
    AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
    Grid.SetColumn(chevron, 3);
    row.Children.Add(chevron);

    var button = new Button
    {
      Content = row,
      HorizontalAlignment = HorizontalAlignment.Stretch,
      HorizontalContentAlignment = HorizontalAlignment.Stretch,
      Padding = new Thickness(0),
      Background = TransparentBrush(),
      BorderThickness = new Thickness(0),
      CornerRadius = new CornerRadius(4)
    };
    AutomationProperties.SetAutomationId(button, automationId);
    AutomationProperties.SetName(button, $"{title} details");

    var flyout = new Flyout { Content = flyoutContent, Placement = FlyoutPlacementMode.Right };
    FlyoutBase.SetAttachedFlyout(button, flyout);
    button.Click += static (sender, _) => FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);

    return button;
  }

  private void AddTextRow(string title, string value, string detail)
  {
    var panel = new StackPanel
    {
      Padding = new Thickness(12, 8, 12, 8),
      Spacing = 2
    };
    panel.Children.Add(new TextBlock
    {
      Text = title,
      FontSize = 13,
      FontWeight = FontWeights.SemiBold,
      IsTextSelectionEnabled = false
    });
    panel.Children.Add(new TextBlock
    {
      Text = $"{value} · {detail}",
      Style = CaptionStyle(),
      Foreground = SecondaryTextBrush(),
      TextWrapping = TextWrapping.Wrap,
      IsTextSelectionEnabled = false
    });
    MenuPanel.Children.Add(panel);
  }

  private static Button BuildPlainButton(string text, string automationId)
  {
    var grid = new Grid
    {
      Padding = new Thickness(12, 7, 12, 7),
      ColumnSpacing = 10,
      HorizontalAlignment = HorizontalAlignment.Stretch
    };
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    var icon = new FontIcon
    {
      Glyph = text switch
      {
        "Open panel" => "\uE80F",
        "Refresh" => "\uE72C",
        "Quit daemon" => "\uE711",
        _ => "\uE10F"
      },
      FontSize = 16,
      Width = 24,
      VerticalAlignment = VerticalAlignment.Center
    };
    AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
    grid.Children.Add(icon);

    var label = new TextBlock
    {
      Text = text,
      FontSize = 14,
      VerticalAlignment = VerticalAlignment.Center,
      IsTextSelectionEnabled = false
    };
    Grid.SetColumn(label, 1);
    grid.Children.Add(label);

    var button = new Button
    {
      Content = grid,
      HorizontalAlignment = HorizontalAlignment.Stretch,
      HorizontalContentAlignment = HorizontalAlignment.Stretch,
      Padding = new Thickness(0),
      Background = new SolidColorBrush(Colors.Transparent),
      BorderThickness = new Thickness(0),
      CornerRadius = new CornerRadius(4)
    };
    AutomationProperties.SetAutomationId(button, automationId);
    AutomationProperties.SetName(button, text);
    return button;
  }

  private StackPanel BuildDetailsPanel(params (string Label, string Value)[] rows)
  {
    var panel = new StackPanel
    {
      Padding = new Thickness(12),
      Spacing = 6,
      MinWidth = 260,
      MaxWidth = 320
    };

    foreach (var (label, value) in rows)
    {
      panel.Children.Add(new TextBlock
      {
        Text = label,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = SecondaryTextBrush(),
        IsTextSelectionEnabled = false
      });
      panel.Children.Add(new TextBlock
      {
        Text = string.IsNullOrWhiteSpace(value) ? "-" : value,
        FontSize = 12,
        TextWrapping = TextWrapping.WrapWholeWords,
        IsTextSelectionEnabled = true
      });
    }

    return panel;
  }

  private void AddSeparator()
  {
    var separator = new Border
    {
      Height = 1,
      Margin = new Thickness(8, 6, 8, 6),
      Background = ResourceBrush("DividerStrokeColorDefaultBrush")
    };
    AutomationProperties.SetAccessibilityView(separator, AccessibilityView.Raw);
    MenuPanel.Children.Add(separator);
  }

  private async void DomainToggle_Toggled(object sender, RoutedEventArgs e)
  {
    if (sender is not ToggleSwitch toggle || toggle.Tag is not TrayPopupDomainRow domain)
    {
      return;
    }

    var requestedState = toggle.IsOn;
    toggle.IsEnabled = false;
    try
    {
      var accepted = await _app.SetDomainEnabledAsync(
          domain.RegistrationId,
          domain.ShimSessionNonce,
          domain.DomainKey,
          requestedState);
      if (accepted)
      {
        await RefreshStateAsync();
      }
      else
      {
        RestoreDomainToggleState(toggle, !requestedState);
      }
    }
    catch
    {
      RestoreDomainToggleState(toggle, !requestedState);
    }
    finally
    {
      toggle.IsEnabled = true;
    }
  }

  private void RestoreDomainToggleState(ToggleSwitch toggle, bool isOn)
  {
    toggle.Toggled -= DomainToggle_Toggled;
    try
    {
      toggle.IsOn = isOn;
    }
    finally
    {
      toggle.Toggled += DomainToggle_Toggled;
    }
  }

  private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
  {
    if (_focusableControls.Count == 0)
    {
      return;
    }

    var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as Control;
    var focusedIndex = focused is null ? -1 : _focusableControls.IndexOf(focused);

    switch (e.Key)
    {
      case VirtualKey.Down:
        _focusableControls[(focusedIndex + 1 + _focusableControls.Count) % _focusableControls.Count]
            .Focus(FocusState.Keyboard);
        e.Handled = true;
        break;
      case VirtualKey.Up:
        _focusableControls[(focusedIndex - 1 + _focusableControls.Count) % _focusableControls.Count]
            .Focus(FocusState.Keyboard);
        e.Handled = true;
        break;
      case VirtualKey.Enter:
      case VirtualKey.Space:
        if (focused is Button button)
        {
          if (button.Tag is Func<ValueTask> action)
          {
            await action();
          }
          else
          {
            FlyoutBase.ShowAttachedFlyout(button);
          }

          e.Handled = true;
        }
        break;
      case VirtualKey.Escape:
        Dismiss();
        e.Handled = true;
        break;
    }
  }

  private void OnActivated(object sender, WindowActivatedEventArgs args)
  {
    if (Environment.GetEnvironmentVariable("CADDER_UI_AUTOMATION") == "1")
    {
      return;
    }

    if (args.WindowActivationState != WindowActivationState.Deactivated)
    {
      return;
    }

    var timer = DispatcherQueue.CreateTimer();
    timer.Interval = TimeSpan.FromMilliseconds(150);
    timer.IsRepeating = false;
    timer.Tick += (_, _) =>
    {
      timer.Stop();
      if (!IsOpen)
      {
        return;
      }

      if (GetForegroundWindow() != _windowHandle)
      {
        Dismiss();
      }
    };
    timer.Start();
  }

  private void SizeToContent()
  {
    RootGrid.InvalidateMeasure();
    MenuPanel.InvalidateMeasure();
    RootGrid.UpdateLayout();
    MenuPanel.Measure(new Windows.Foundation.Size(PopupWidthViewUnits, double.PositiveInfinity));
    var desiredHeight = (int)Math.Ceiling(MenuPanel.DesiredSize.Height) + 2;
    var popupHeight = Math.Clamp(desiredHeight, PopupMinHeightViewUnits, PopupMaxHeightViewUnits);
    AppWindow.Resize(new SizeInt32(
        ConvertViewUnitsToPixels(PopupWidthViewUnits),
        ConvertViewUnitsToPixels(popupHeight)));
  }

  private void PositionAtCursor()
  {
    if (!GetCursorPos(out var cursor))
    {
      return;
    }

    var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
    var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
    if (!GetMonitorInfo(monitor, ref monitorInfo))
    {
      return;
    }

    var size = AppWindow.Size;
    var (x, y) = TrayPopupPositioner.CalculatePosition(
        cursor.X,
        cursor.Y,
        size.Width,
        size.Height,
        monitorInfo.WorkArea.Left,
        monitorInfo.WorkArea.Top,
        monitorInfo.WorkArea.Right,
        monitorInfo.WorkArea.Bottom,
        margin: 8);
    AppWindow.Move(new PointInt32(x, y));
  }

  private void ApplyPopupStyle()
  {
    if (_popupStyleApplied)
    {
      return;
    }

    var style = GetWindowLong(_windowHandle, GwlStyle);
    style &= ~(WsCaption | WsThickFrame | WsSysMenu);
    SetWindowLong(_windowHandle, GwlStyle, style);

    var exStyle = GetWindowLong(_windowHandle, GwlExStyle);
    exStyle |= WsExToolWindow;
    SetWindowLong(_windowHandle, GwlExStyle, exStyle);
    SetWindowPos(
        _windowHandle,
        0,
        0,
        0,
        0,
        0,
        SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    _popupStyleApplied = true;
  }

  private void ApplyRoundedWindowRegion()
  {
    if (!GetWindowRect(_windowHandle, out var rect))
    {
      return;
    }

    var width = rect.Right - rect.Left;
    var height = rect.Bottom - rect.Top;
    if (width <= 0 || height <= 0)
    {
      return;
    }

    var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, 16, 16);
    if (region == 0)
    {
      return;
    }

    if (SetWindowRgn(_windowHandle, region, true) == 0)
    {
      DeleteObject(region);
    }
  }

  private Brush StatusBrush(string status)
  {
    if (status.Contains("Failed", StringComparison.OrdinalIgnoreCase)
        || status.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase)
        || status.Contains("attention", StringComparison.OrdinalIgnoreCase)
        || status.Contains("Faulted", StringComparison.OrdinalIgnoreCase))
    {
      return ResourceBrush("SystemFillColorCriticalBrush");
    }

    if (status.Contains("Idle", StringComparison.OrdinalIgnoreCase)
        || status.Contains("Inactive", StringComparison.OrdinalIgnoreCase)
        || status.Contains("Not", StringComparison.OrdinalIgnoreCase))
    {
      return ResourceBrush("SystemFillColorNeutralBrush");
    }

    return ResourceBrush("SystemFillColorSuccessBrush");
  }

  private Style CaptionStyle()
  {
    return (Style)Application.Current.Resources["CaptionTextBlockStyle"];
  }

  private Brush SecondaryTextBrush()
  {
    return ResourceBrush("TextFillColorSecondaryBrush");
  }

  private Brush ResourceBrush(string key)
  {
    return (Brush)Application.Current.Resources[key];
  }

  private static Brush TransparentBrush()
  {
    return new SolidColorBrush(Colors.Transparent);
  }

  private static string SanitizeAutomationPart(string value)
  {
    var chars = value
        .Where(char.IsLetterOrDigit)
        .Take(64)
        .ToArray();
    return chars.Length == 0 ? "Unknown" : new string(chars);
  }

  private int ConvertViewUnitsToPixels(int viewUnits)
  {
    var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
    return Math.Max(1, (int)Math.Ceiling(viewUnits * scale));
  }

  [DllImport("user32.dll")]
  private static extern bool GetCursorPos(out Point point);

  [DllImport("user32.dll")]
  private static extern nint SetForegroundWindow(nint windowHandle);

  [DllImport("user32.dll")]
  private static extern nint GetForegroundWindow();

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool ShowWindow(nint windowHandle, int commandShow);

  [DllImport("user32.dll")]
  private static extern nint MonitorFromPoint(Point point, uint flags);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
  private static extern int GetWindowLong(nint windowHandle, int index);

  [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
  private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(
      nint windowHandle,
      nint insertAfter,
      int x,
      int y,
      int width,
      int height,
      uint flags);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool GetWindowRect(nint windowHandle, out Rect rect);

  [DllImport("gdi32.dll", SetLastError = true)]
  private static extern nint CreateRoundRectRgn(
      int left,
      int top,
      int right,
      int bottom,
      int ellipseWidth,
      int ellipseHeight);

  [DllImport("gdi32.dll", SetLastError = true)]
  private static extern bool DeleteObject(nint handle);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern int SetWindowRgn(nint windowHandle, nint region, bool redraw);

  [StructLayout(LayoutKind.Sequential)]
  private struct Point
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct Rect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MonitorInfo
  {
    public int Size;
    public Rect MonitorArea;
    public Rect WorkArea;
    public uint Flags;
  }
}
