using Cadder.Daemon;

namespace Cadder.Daemon.Tests;

public sealed class DaemonLifecycleTests
{
  [Fact]
  public void FirstSingletonAcquisitionOwnsTheDaemonLease()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());

    using var acquisition = new AcquisitionScope(coordinator.TryAcquire());

    Assert.Equal(DaemonSingletonAcquisitionStatus.Acquired, acquisition.Result.Status);
    Assert.True(acquisition.Result.HasOwnership);
    Assert.NotNull(acquisition.Result.Lease);
  }

  [Fact]
  public void SecondSingletonAcquisitionReportsAlreadyRunning()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    using var first = new AcquisitionScope(coordinator.TryAcquire());

    DaemonSingletonAcquisition? second = null;
    var secondThread = new Thread(() => second = coordinator.TryAcquire());
    secondThread.Start();
    Assert.True(secondThread.Join(TimeSpan.FromSeconds(5)));

    Assert.NotNull(second);
    Assert.Equal(DaemonSingletonAcquisitionStatus.AlreadyRunning, second.Status);
    Assert.False(second.HasOwnership);
    Assert.Null(second.Lease);
  }

  [Fact]
  public void AbandonedSingletonMutexIsRecoverable()
  {
    var mutexName = CreateMutexName();
    var mutex = new FakeSingletonMutex { ThrowAbandonedOnWait = true };
    var coordinator = new NamedMutexDaemonSingletonCoordinator(mutexName, _ => mutex);

    var recovered = new AcquisitionScope(coordinator.TryAcquire());

    Assert.Equal(DaemonSingletonAcquisitionStatus.AcquiredAfterAbandonedMutex, recovered.Result.Status);
    Assert.True(recovered.Result.HasOwnership);
    recovered.Dispose();
    Assert.True(mutex.Released);
    Assert.True(mutex.Disposed);
  }

  [Fact]
  public async Task ExplicitShutdownStopsBoundariesAndReleasesSingletonLease()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    var acquisition = coordinator.TryAcquire();
    Assert.NotNull(acquisition.Lease);

    var ipc = new RecordingIpcServer();
    var registrations = new RecordingRegistrationStore();
    var runtime = new RecordingRuntime();
    var host = new DaemonLifecycleHost(acquisition.Lease, ipc, registrations, runtime);

    await host.StartAsync();
    await host.ShutdownAsync();

    Assert.Equal(DaemonLifecycleState.Stopped, host.Snapshot.State);
    Assert.True(ipc.Started);
    Assert.True(ipc.Stopped);
    Assert.True(registrations.Cleared);
    Assert.True(runtime.Stopped);

    using var reacquired = new AcquisitionScope(coordinator.TryAcquire());
    Assert.Equal(DaemonSingletonAcquisitionStatus.Acquired, reacquired.Result.Status);
  }

  [Fact]
  public async Task Shutdown_DoesNotHoldLifecycleGateWhileStoppingIpc()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    var acquisition = coordinator.TryAcquire();
    Assert.NotNull(acquisition.Lease);

    DaemonLifecycleHost? host = null;
    var ipc = new CallbackOnStopIpcServer(async () =>
    {
      Assert.NotNull(host);
      await host.UpdateRegistrationCountAsync(0);
    });
    host = new DaemonLifecycleHost(
        acquisition.Lease,
        ipc,
        new RecordingRegistrationStore(),
        new RecordingRuntime());

    await host.StartAsync();

    await host.ShutdownAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(ipc.Stopped);
    Assert.Equal(DaemonLifecycleState.Stopped, host.Snapshot.State);
  }

  [Fact]
  public async Task ZeroRegistrationsKeepDaemonRunning()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    var acquisition = coordinator.TryAcquire();
    Assert.NotNull(acquisition.Lease);

    var ipc = new RecordingIpcServer();
    var host = new DaemonLifecycleHost(
        acquisition.Lease,
        ipc,
        new RecordingRegistrationStore(),
        new RecordingRuntime());

    await host.StartAsync();
    await host.UpdateRegistrationCountAsync(0);

    Assert.Equal(DaemonLifecycleState.Running, host.Snapshot.State);
    Assert.Equal(0, host.Snapshot.RegistrationCount);
    Assert.False(ipc.Stopped);

    await host.ShutdownAsync();
  }

  [Fact]
  public async Task FailedIpcStartReturnsLifecycleToCreatedForRetry()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    var acquisition = coordinator.TryAcquire();
    Assert.NotNull(acquisition.Lease);

    var ipc = new FailsFirstStartIpcServer();
    var host = new DaemonLifecycleHost(
        acquisition.Lease,
        ipc,
        new RecordingRegistrationStore(),
        new RecordingRuntime());

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

    Assert.Equal(DaemonLifecycleState.Created, host.Snapshot.State);

    await host.StartAsync();

    Assert.Equal(DaemonLifecycleState.Running, host.Snapshot.State);
    Assert.Equal(2, ipc.StartAttempts);

    await host.ShutdownAsync();
  }

  [Fact]
  public async Task ForwardedLaunchIntentIsRecordedByPrimaryDaemon()
  {
    var coordinator = new NamedMutexDaemonSingletonCoordinator(CreateMutexName());
    var acquisition = coordinator.TryAcquire();
    Assert.NotNull(acquisition.Lease);

    var host = new DaemonLifecycleHost(
        acquisition.Lease,
        new RecordingIpcServer(),
        new RecordingRegistrationStore(),
        new RecordingRuntime());

    await host.StartAsync();
    await host.RecordForwardedLaunchIntentAsync(
        new DaemonLaunchIntent(DateTimeOffset.Parse("2026-06-09T12:00:00Z"), "WinUI", ["--probe"]));

    Assert.Equal(1, host.Snapshot.ForwardedLaunchIntentCount);

    await host.ShutdownAsync();
  }

  private static string CreateMutexName()
  {
    return $@"Local\Cadder.Tests.{Guid.NewGuid():N}";
  }

  private sealed class AcquisitionScope : IDisposable
  {
    public AcquisitionScope(DaemonSingletonAcquisition result)
    {
      Result = result;
    }

    public DaemonSingletonAcquisition Result { get; }

    public void Dispose()
    {
      Result.Lease?.Dispose();
    }
  }

  private sealed class RecordingIpcServer : IDaemonIpcServer
  {
    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
      Started = true;
      return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
      Stopped = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FailsFirstStartIpcServer : IDaemonIpcServer
  {
    public int StartAttempts { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
      StartAttempts++;

      if (StartAttempts == 1)
      {
        throw new InvalidOperationException("Injected IPC start failure.");
      }

      return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class CallbackOnStopIpcServer(Func<ValueTask> onStop) : IDaemonIpcServer
  {
    public bool Stopped { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
      return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
      await onStop();
      Stopped = true;
    }
  }

  private sealed class RecordingRegistrationStore : ITransientRegistrationStore
  {
    public bool Cleared { get; private set; }

    public ValueTask ClearTransientRegistrationsAsync(CancellationToken cancellationToken = default)
    {
      Cleared = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingRuntime : ICadderOwnedRuntime
  {
    public bool Stopped { get; private set; }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
      Stopped = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class FakeSingletonMutex : IDaemonSingletonMutex
  {
    public bool ThrowAbandonedOnWait { get; init; }

    public bool Released { get; private set; }

    public bool Disposed { get; private set; }

    public bool WaitOne(TimeSpan timeout)
    {
      if (ThrowAbandonedOnWait)
      {
        throw new AbandonedMutexException();
      }

      return true;
    }

    public void ReleaseMutex()
    {
      Released = true;
    }

    public void Dispose()
    {
      Disposed = true;
    }
  }
}
