using Cadder.Daemon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Cadder.Tray.WinUI;

public static class Program
{
  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    var currentInstance = AppInstance.GetCurrent();
    var mainInstance = AppInstance.FindOrRegisterForKey(
        DaemonSingletonMutexNames.CreatePerUserAppInstanceKey());

    if (!mainInstance.IsCurrent)
    {
      await mainInstance.RedirectActivationToAsync(currentInstance.GetActivatedEventArgs());
      return 0;
    }

    var singletonCoordinator = new NamedMutexDaemonSingletonCoordinator(
        DaemonSingletonMutexNames.CreatePerUserName());
    var acquisition = singletonCoordinator.TryAcquire();

    if (!acquisition.HasOwnership || acquisition.Lease is null)
    {
      return 0;
    }

    App.ConfigureDaemonSingleton(acquisition);

    WinRT.ComWrappersSupport.InitializeComWrappers();
    Application.Start(_ =>
    {
      var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
      SynchronizationContext.SetSynchronizationContext(context);
      new App();
    });

    return 0;
  }
}
