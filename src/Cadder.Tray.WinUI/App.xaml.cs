using Cadder.Contracts;
using Cadder.Daemon;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Cadder.Tray.WinUI;

public partial class App : Application
{
  private static DaemonSingletonAcquisition? s_singletonAcquisition;
  private readonly DaemonLifecycleHost _daemonHost;
  private readonly CadderIpcEndpoint _endpoint;
  private TrayPopupWindow? _trayPopup;
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
    var realCaddyRuntime = new ProcessRealCaddyRuntimeAdapter();
    var caddyConfigCoordinator = new CaddyConfigCoordinator(realCaddyRuntime);
    var guiStateProjector = new GuiStateProjector();
    var guiStateBroadcaster = new InMemoryGuiStateChangeBroadcaster();
    Func<int, CancellationToken, ValueTask> registrationCountChanged = async (registrationCount, cancellationToken) =>
    {
      if (daemonHost is not null)
      {
        await daemonHost.UpdateRegistrationCountAsync(registrationCount, cancellationToken);
      }
    };
    var endpoint = new CadderIpcEndpoint(
        registrationStore,
        realCaddyRuntime,
        caddyConfigCoordinator,
        guiStateBroadcaster,
        guiStateProjector,
        registrationCountChanged);
    _endpoint = endpoint;
    var ipcServer = new NamedPipeDaemonIpcServer(endpoint);
    var ownerWatcher = new RegistrationOwnerWatcher(
        registrationStore,
        new SystemOwnerProcessProbe(),
        realCaddyRuntime,
        guiStateProjector,
        guiStateBroadcaster,
        registrationCountChanged);

    daemonHost = new DaemonLifecycleHost(
        acquisition.Lease,
        ipcServer,
        registrationStore,
        realCaddyRuntime,
        ownerWatcher: ownerWatcher);
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
      _trayPopup?.Dismiss();
      _trayPresence?.Dispose();
      Exit();
    }
  }

  public async ValueTask<GuiStateSnapshot?> QueryGuiStateAsync(CancellationToken cancellationToken = default)
  {
    var response = await _endpoint.QueryStateAsync(
        new QueryGuiStateRequest($"tray-state-{Guid.NewGuid():N}"),
        cancellationToken);
    return response.Snapshot;
  }

  public async ValueTask<bool> SetDomainEnabledAsync(
      string registrationId,
      string shimSessionNonce,
      string domainKey,
      bool enabled,
      CancellationToken cancellationToken = default)
  {
    var list = await _endpoint.ListAsync(
        new ListEntrypointsRequest($"tray-list-{Guid.NewGuid():N}"),
        cancellationToken);
    if (!list.Accepted)
    {
      return false;
    }

    var registration = list.Registrations.FirstOrDefault(candidate =>
        string.Equals(candidate.RegistrationId, registrationId, StringComparison.Ordinal)
        && string.Equals(candidate.EntrypointInstance.ShimSessionNonce, shimSessionNonce, StringComparison.Ordinal));
    if (registration is null)
    {
      return false;
    }

    var matched = false;
    var domains = registration.RegisteredDomains
        .Select(domain =>
        {
          var candidateKey = domain.Name.Canonical ?? domain.Name.Raw;
          if (!string.Equals(candidateKey, domainKey, StringComparison.OrdinalIgnoreCase))
          {
            return domain;
          }

          matched = true;
          return domain with
          {
            ActivationState = enabled ? ActivationState.Active : ActivationState.Inactive
          };
        })
        .ToArray();
    if (!matched)
    {
      return false;
    }

    var response = await _endpoint.UpdateAsync(
        new UpdateEntrypointRequest(
            $"tray-domain-toggle-{Guid.NewGuid():N}",
            registration.RegistrationId,
            registration.EntrypointInstance.ShimSessionNonce,
            null,
            null,
            domains,
            null,
            null),
        cancellationToken);

    return response.Accepted;
  }

  protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
  {
    await _daemonHost.StartAsync();
    _window = new MainWindow();
    _trayPresence ??= new DaemonTrayPresence(_window);
    _trayPresence.Activated += OnTrayIconActivated;
    _window.Activate();
  }

  private async void OnAppInstanceActivated(object? sender, AppActivationArguments args)
  {
    await _daemonHost.RecordForwardedLaunchIntentAsync(
        new DaemonLaunchIntent(DateTimeOffset.UtcNow, $"WinUI:{args.Kind}", ExtractActivationArguments(args)));

    ActivateMainWindow();
  }

  internal void ActivateMainWindow()
  {
    _window?.DispatcherQueue.TryEnqueue(() => _window.ShowAndActivate());
  }

  private void OnTrayIconActivated(object? sender, TrayIconActivatedEventArgs args)
  {
    _window?.DispatcherQueue.TryEnqueue(async () =>
    {
      if (_trayPopup?.IsOpen == true)
      {
        _trayPopup.Dismiss();
        _trayPopup = null;
        return;
      }

      _trayPopup ??= new TrayPopupWindow(this);
      await _trayPopup.ShowAtCursorAsync();
    });
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
