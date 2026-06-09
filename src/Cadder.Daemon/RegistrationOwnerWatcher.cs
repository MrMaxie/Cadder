using System.ComponentModel;
using System.Diagnostics;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class RegistrationOwnerWatcher : IRegistrationOwnerWatcher
{
  private readonly IRegistrationStore _registrationStore;
  private readonly IOwnerProcessProbe _processProbe;
  private readonly IRealCaddyRuntimeAdapter _realCaddyRuntime;
  private readonly IGuiStateProjector _guiStateProjector;
  private readonly IGuiStateChangeBroadcaster _guiStateBroadcaster;
  private readonly Func<int, CancellationToken, ValueTask>? _registrationCountChanged;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _pollInterval;
  private CancellationTokenSource? _stopping;
  private Task? _watchLoop;

  public RegistrationOwnerWatcher(
      IRegistrationStore registrationStore,
      IOwnerProcessProbe processProbe,
      IRealCaddyRuntimeAdapter realCaddyRuntime,
      IGuiStateProjector guiStateProjector,
      IGuiStateChangeBroadcaster guiStateBroadcaster,
      Func<int, CancellationToken, ValueTask>? registrationCountChanged = null,
      TimeProvider? timeProvider = null,
      TimeSpan? pollInterval = null)
  {
    _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
    _processProbe = processProbe ?? throw new ArgumentNullException(nameof(processProbe));
    _realCaddyRuntime = realCaddyRuntime ?? throw new ArgumentNullException(nameof(realCaddyRuntime));
    _guiStateProjector = guiStateProjector ?? throw new ArgumentNullException(nameof(guiStateProjector));
    _guiStateBroadcaster = guiStateBroadcaster ?? throw new ArgumentNullException(nameof(guiStateBroadcaster));
    _registrationCountChanged = registrationCountChanged;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
  }

  public ValueTask StartAsync(CancellationToken cancellationToken = default)
  {
    if (_watchLoop is not null)
    {
      return ValueTask.CompletedTask;
    }

    _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _watchLoop = WatchAsync(_stopping.Token);
    return ValueTask.CompletedTask;
  }

  public async ValueTask StopAsync(CancellationToken cancellationToken = default)
  {
    if (_stopping is null)
    {
      return;
    }

    await _stopping.CancelAsync().ConfigureAwait(false);

    if (_watchLoop is not null)
    {
      await _watchLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    _stopping.Dispose();
    _stopping = null;
    _watchLoop = null;
  }

  public async ValueTask<int> SweepOnceAsync(CancellationToken cancellationToken = default)
  {
    var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
    var deadOwners = registrations
        .Select(static registration => registration.OwnerProcess)
        .Distinct()
        .Where(owner => _processProbe.GetLiveness(owner) == OwnerProcessLiveness.Dead)
        .ToArray();

    var removed = 0;
    foreach (var owner in deadOwners)
    {
      removed += await _registrationStore.RemoveByOwnerAsync(owner, cancellationToken).ConfigureAwait(false);
    }

    if (removed > 0)
    {
      await PublishCleanupAsync(cancellationToken).ConfigureAwait(false);
    }

    return removed;
  }

  private async Task WatchAsync(CancellationToken cancellationToken)
  {
    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        await SweepOnceAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
  }

  private async ValueTask PublishCleanupAsync(CancellationToken cancellationToken)
  {
    var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
    if (_registrationCountChanged is not null)
    {
      await _registrationCountChanged(registrations.Count, cancellationToken).ConfigureAwait(false);
    }

    var runtime = await _realCaddyRuntime.InspectAsync(cancellationToken).ConfigureAwait(false);
    var snapshot = _guiStateProjector.Project(new DaemonStateSnapshot(
        _timeProvider.GetUtcNow(),
        registrations,
        runtime));

    await _guiStateBroadcaster.PublishAsync(
        GuiStateChangeKind.RegistrationsChanged,
        snapshot,
        null,
        cancellationToken).ConfigureAwait(false);
  }
}

public sealed class SystemOwnerProcessProbe : IOwnerProcessProbe
{
  private readonly Func<int, IOwnerProcessHandle> _processFactory;

  public SystemOwnerProcessProbe()
      : this(static processId => new SystemOwnerProcessHandle(Process.GetProcessById(processId)))
  {
  }

  public SystemOwnerProcessProbe(Func<int, IOwnerProcessHandle> processFactory)
  {
    _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
  }

  public OwnerProcessLiveness GetLiveness(OwnerProcessIdentity owner)
  {
    ArgumentNullException.ThrowIfNull(owner);

    try
    {
      using var process = _processFactory(owner.ProcessId);
      var startedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);

      return startedAtUtc == owner.ProcessStartTimeUtc
          ? OwnerProcessLiveness.Alive
          : OwnerProcessLiveness.Dead;
    }
    catch (ArgumentException)
    {
      return OwnerProcessLiveness.Dead;
    }
    catch (InvalidOperationException)
    {
      return OwnerProcessLiveness.Dead;
    }
    catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
    {
      return OwnerProcessLiveness.Unknown;
    }
  }

  private sealed class SystemOwnerProcessHandle(Process process) : IOwnerProcessHandle
  {
    public DateTime StartTime => process.StartTime;

    public void Dispose()
    {
      process.Dispose();
    }
  }
}

public interface IOwnerProcessHandle : IDisposable
{
  DateTime StartTime { get; }
}
