using Cadder.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadder.Tray.WinUI;

public sealed partial class MainPage : Page
{
  private readonly DispatcherTimer _refreshTimer = new()
  {
    Interval = TimeSpan.FromSeconds(2)
  };

  public MainPage()
  {
    InitializeComponent();
    Loaded += OnLoaded;
    Unloaded += OnUnloaded;
    _refreshTimer.Tick += OnRefreshTimerTick;
  }

  private async void QuitDaemonButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
  {
    QuitDaemonButton.IsEnabled = false;

    if (Microsoft.UI.Xaml.Application.Current is App app)
    {
      await app.QuitDaemonAsync();
    }
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    await RefreshStateAsync();
    _refreshTimer.Start();
  }

  private void OnUnloaded(object sender, RoutedEventArgs e)
  {
    _refreshTimer.Stop();
  }

  private async void OnRefreshTimerTick(object? sender, object e)
  {
    await RefreshStateAsync();
  }

  private async ValueTask RefreshStateAsync()
  {
    try
    {
      if (Application.Current is not App app)
      {
        return;
      }

      var snapshot = await app.QueryGuiStateAsync();
      if (snapshot is null)
      {
        return;
      }

      ApplySnapshot(snapshot);
    }
    catch (Exception ex)
    {
      RuntimeStatusText.Text = "Unhealthy";
      RuntimeDiagnosticText.Text = ex.Message;
    }
  }

  private void ApplySnapshot(GuiStateSnapshot snapshot)
  {
    var runtime = snapshot.RealCaddyRuntime;
    RuntimeStatusText.Text = runtime.Status.ToString();
    RuntimeProcessText.Text = runtime.Process is null
        ? "-"
        : $"{runtime.Process.ProcessId} ({(runtime.Process.OwnedByCadder ? "owned" : "external")})";
    RuntimeAdminEndpointText.Text = runtime.AdminEndpoint ?? "-";
    RuntimeVersionText.Text = runtime.Version ?? "-";
    ConfigStatusText.Text = snapshot.CaddyConfig?.Status.ToString() ?? "NotApplied";
    RegistrationCountText.Text = snapshot.Registrations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
    RuntimeDiagnosticText.Text = runtime.Diagnostics?.FirstOrDefault()?.Message
        ?? snapshot.CaddyConfig?.Diagnostics.FirstOrDefault()?.Message
        ?? "-";
  }
}
