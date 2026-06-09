using Cadder.Daemon;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Cadder.Tray.WinUI;

public partial class App : Application
{
  private static DaemonSingletonAcquisition? s_singletonAcquisition;
  private readonly DaemonLifecycleHost _daemonHost;
  private DaemonTrayPresence? _trayPresence;
  private MainWindow? _window;

  public App()
  {
    var acquisition = s_singletonAcquisition
        ?? throw new InvalidOperationException("Cadder daemon singleton must be configured before App starts.");
    if (acquisition.Lease is null)
    {
      throw new InvalidOperationException("Cadder daemon singleton lease is required.");
    }

    DaemonLifecycleHost? daemonHost = null;
    var registrationStore = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(
        registrationStore,
        new NoopRealCaddyRuntimeAdapter(),
        async (registrationCount, cancellationToken) =>
        {
          if (daemonHost is not null)
          {
            await daemonHost.UpdateRegistrationCountAsync(registrationCount, cancellationToken);
          }
        });
    var ipcServer = new NamedPipeDaemonIpcServer(endpoint);

    daemonHost = new DaemonLifecycleHost(
        acquisition.Lease,
        ipcServer,
        registrationStore,
        new NoopCadderOwnedRuntime());
    _daemonHost = daemonHost;

    InitializeComponent();
    AppInstance.GetCurrent().Activated += OnAppInstanceActivated;
  }

  public static void ConfigureDaemonSingleton(DaemonSingletonAcquisition acquisition)
  {
    ArgumentNullException.ThrowIfNull(acquisition);

    if (!acquisition.HasOwnership)
    {
      throw new ArgumentException("The current process must own the daemon singleton.", nameof(acquisition));
    }

    s_singletonAcquisition = acquisition;
  }

  public async ValueTask QuitDaemonAsync()
  {
    try
    {
      await _daemonHost.ShutdownAsync();
    }
    finally
    {
      _trayPresence?.Dispose();
      Exit();
    }
  }

  protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
  {
    await _daemonHost.StartAsync();
    _window = new MainWindow();
    _trayPresence ??= new DaemonTrayPresence(_window);
    _window.Activate();
  }

  private async void OnAppInstanceActivated(object? sender, AppActivationArguments args)
  {
    await _daemonHost.RecordForwardedLaunchIntentAsync(
        new DaemonLaunchIntent(DateTimeOffset.UtcNow, $"WinUI:{args.Kind}", ExtractActivationArguments(args)));

    ActivateMainWindow();
  }

  private void ActivateMainWindow()
  {
    _window?.DispatcherQueue.TryEnqueue(() => _window.ShowAndActivate());
  }

  private static string[] ExtractActivationArguments(AppActivationArguments args)
  {
    if (args.Data is Microsoft.UI.Xaml.LaunchActivatedEventArgs launchArgs
        && !string.IsNullOrWhiteSpace(launchArgs.Arguments))
    {
      return [launchArgs.Arguments];
    }

    if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs uwpLaunchArgs
        && !string.IsNullOrWhiteSpace(uwpLaunchArgs.Arguments))
    {
      return [uwpLaunchArgs.Arguments];
    }

    return [$"Kind={args.Kind}", $"Data={args.Data?.GetType().FullName ?? "<none>"}"];
  }
}
