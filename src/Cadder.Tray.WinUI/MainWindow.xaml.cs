using System.Runtime.InteropServices;
using Cadder.Tray.WinUI.Pages;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Cadder.Tray.WinUI;

public sealed partial class MainWindow : Window
{
  private const int SwHide = 0;
  private const int SwShow = 5;
  private readonly DispatcherTimer _refreshTimer = new()
  {
    Interval = TimeSpan.FromSeconds(2)
  };
  private readonly PanelStateBuilder _stateBuilder = new();
  private readonly PanelStateStore _store = new();
  private readonly nint _windowHandle;
  private bool _isNavigating;
  private bool _isProgrammaticSearchTextUpdate;
  private string _currentPageTag = "Overview";
  private Type? _currentPageType;

  public MainWindow()
  {
    InitializeComponent();

    _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
    AppWindow.Closing += OnAppWindowClosing;

    ConfigureModernTitleBar();
    AppWindow.SetIcon("Assets/AppIcon.ico");
    ResizeForPanel();
    RootGrid.Loaded += RootGrid_Loaded;
    _refreshTimer.Tick += RefreshTimer_Tick;
    _store.Changed += Store_Changed;
    PanelStateStoreLocator.Current = _store;
  }

  public void ShowAndActivate()
  {
    ShowWindow(_windowHandle, SwShow);
    Activate();
    _ = RefreshStateAsync();
  }

  private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
  {
    args.Cancel = true;
    ShowWindow(_windowHandle, SwHide);
  }

  private void ConfigureModernTitleBar()
  {
    ExtendsContentIntoTitleBar = true;
    SetTitleBar(AppTitleBar);

    var titleBar = AppWindow.TitleBar;
    titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
    titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
  }

  private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
  {
    if (_currentPageType is null)
    {
      NavigateToTag("Overview");
    }

    _store.SetLoading(DateTimeOffset.UtcNow);
    await RefreshStateAsync();
    _refreshTimer.Start();
  }

  private async void RefreshTimer_Tick(object? sender, object e)
  {
    await RefreshStateAsync();
  }

  private async Task RefreshStateAsync()
  {
    try
    {
      if (Application.Current is not App app)
      {
        _store.SetDisconnected(DateTimeOffset.UtcNow, "Cadder application host is unavailable.");
        return;
      }

      var snapshot = await app.QueryGuiStateAsync();
      if (snapshot is null)
      {
        _store.SetDisconnected(DateTimeOffset.UtcNow, "Daemon returned no GUI state snapshot.");
        return;
      }

      _store.SetReady(_stateBuilder.Build(snapshot));
    }
    catch (Exception ex)
    {
      _store.SetDisconnected(DateTimeOffset.UtcNow, ex.Message);
    }
  }

  private void Store_Changed(object? sender, EventArgs e)
  {
    UpdateTitleStatus();
    UpdateSearchSuggestions(TitleSearchBox.Text);
  }

  private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
  {
    if (_isNavigating || args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
    {
      return;
    }

    NavigateToTag(tag);
  }

  private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
  {
    RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;
  }

  private void TitleSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
  {
    if (_isProgrammaticSearchTextUpdate || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
    {
      return;
    }

    _store.SetFilter(sender.Text);
    UpdateSearchSuggestions(sender.Text);
  }

  private void TitleSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
  {
    if (args.SelectedItem is not PanelSearchRecord record)
    {
      return;
    }

    _isProgrammaticSearchTextUpdate = true;
    try
    {
      sender.Text = string.IsNullOrWhiteSpace(record.FilterText) ? record.Title : record.FilterText;
    }
    finally
    {
      _isProgrammaticSearchTextUpdate = false;
    }
  }

  private void TitleSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
  {
    var record = args.ChosenSuggestion as PanelSearchRecord
        ?? SearchRecords(sender.Text).FirstOrDefault();
    if (record is not null)
    {
      _store.SetFilter(record.FilterText);
      NavigateToTag(record.TargetPageTag);
      return;
    }

    _store.SetFilter(args.QueryText);
  }

  private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
  {
    if (!IsControlKeyDown())
    {
      return;
    }

    if (e.Key is VirtualKey.E or VirtualKey.K or VirtualKey.F)
    {
      TitleSearchBox.Focus(FocusState.Keyboard);
      e.Handled = true;
    }
  }

  private void NavigateToTag(string tag)
  {
    var pageType = PageTypeFor(tag);
    if (pageType is null)
    {
      return;
    }

    _currentPageTag = tag;
    _isNavigating = true;
    try
    {
      RootNavigation.SelectedItem = NavigationItemFor(tag);
      if (_currentPageType != pageType)
      {
        ContentHost.Content = Activator.CreateInstance(pageType);
        _currentPageType = pageType;
      }
    }
    finally
    {
      _isNavigating = false;
    }
  }

  private Type? PageTypeFor(string tag)
  {
    return tag switch
    {
      "Overview" => typeof(OverviewPage),
      "Instances" => typeof(InstancesPage),
      "Domains" => typeof(DomainsPage),
      "Logs" => typeof(LogsPage),
      "Settings" => typeof(SettingsPage),
      "Diagnostics" => typeof(DiagnosticsPage),
      _ => null
    };
  }

  private NavigationViewItem? NavigationItemFor(string tag)
  {
    return RootNavigation.MenuItems
        .OfType<NavigationViewItem>()
        .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
  }

  private void UpdateTitleStatus()
  {
    var state = _store.CurrentState;
    TitleStatusText.Text = state.Summary.DaemonStatus;
    TitleCountsText.Text = $"{state.Summary.ActiveEntrypointCount}/{state.Summary.EntrypointCount} instances · {state.Summary.ActiveDomainCount} domains";
    TitleStatusDot.Fill = ResourceBrush(StatusBrushKey(state.Summary.StatusKind));
  }

  private void UpdateSearchSuggestions(string query)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      TitleSearchBox.ItemsSource = null;
      return;
    }

    TitleSearchBox.ItemsSource = SearchRecords(query).Take(8).ToArray();
  }

  private IEnumerable<PanelSearchRecord> SearchRecords(string query)
  {
    var trimmed = query.Trim();
    if (trimmed.Length == 0)
    {
      return [];
    }

    return _store.CurrentState.SearchRecords
        .Where(record => record.SearchText.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || record.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        .OrderBy(record => record.TargetPageTag == _currentPageTag ? 0 : 1)
        .ThenBy(static record => record.Title, StringComparer.OrdinalIgnoreCase);
  }

  private void ResizeForPanel()
  {
    var scale = GetDpiForWindow(_windowHandle) / 96.0;
    AppWindow.Resize(new SizeInt32(
        (int)Math.Ceiling(1240 * scale),
        (int)Math.Ceiling(820 * scale)));
  }

  private Brush ResourceBrush(string key)
  {
    return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
        ? brush
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
  }

  private static string StatusBrushKey(PanelStatusKind kind)
  {
    return kind switch
    {
      PanelStatusKind.Success => "SystemFillColorSuccessBrush",
      PanelStatusKind.Warning => "SystemFillColorCautionBrush",
      PanelStatusKind.Error => "SystemFillColorCriticalBrush",
      _ => "SystemFillColorNeutralBrush"
    };
  }

  private static bool IsControlKeyDown()
  {
    return InputKeyboardSource
        .GetKeyStateForCurrentThread(VirtualKey.Control)
        .HasFlag(CoreVirtualKeyStates.Down);
  }

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool ShowWindow(nint windowHandle, int commandShow);

  [DllImport("user32.dll")]
  private static extern uint GetDpiForWindow(nint windowHandle);
}
