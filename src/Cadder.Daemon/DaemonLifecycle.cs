using System.Collections.Concurrent;

namespace Cadder.Daemon;

public enum DaemonLifecycleState
{
    Created = 0,
    Starting = 1,
    Running = 2,
    ShuttingDown = 3,
    Stopped = 4
}

public sealed record DaemonLaunchIntent(
    DateTimeOffset ReceivedAtUtc,
    string Source,
    string[] Arguments);

public sealed record DaemonLifecycleSnapshot(
    DaemonLifecycleState State,
    int RegistrationCount,
    int ForwardedLaunchIntentCount,
    DateTimeOffset LastChangedAtUtc);

public interface IDaemonIpcServer
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface ITransientRegistrationStore
{
    ValueTask ClearTransientRegistrationsAsync(CancellationToken cancellationToken = default);
}

public interface ICadderOwnedRuntime
{
    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public sealed class DaemonLifecycleHost
{
    private readonly IDaemonSingletonLease _singletonLease;
    private readonly IDaemonIpcServer _ipcServer;
    private readonly ITransientRegistrationStore _registrationStore;
    private readonly ICadderOwnedRuntime _runtime;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<DaemonLaunchIntent> _forwardedLaunchIntents = new();
    private DaemonLifecycleState _state = DaemonLifecycleState.Created;
    private int _registrationCount;
    private DateTimeOffset _lastChangedAtUtc;
    private bool _leaseDisposed;

    public DaemonLifecycleHost(
        IDaemonSingletonLease singletonLease,
        IDaemonIpcServer ipcServer,
        ITransientRegistrationStore registrationStore,
        ICadderOwnedRuntime runtime,
        TimeProvider? timeProvider = null)
    {
        _singletonLease = singletonLease ?? throw new ArgumentNullException(nameof(singletonLease));
        _ipcServer = ipcServer ?? throw new ArgumentNullException(nameof(ipcServer));
        _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastChangedAtUtc = _timeProvider.GetUtcNow();
    }

    public DaemonLifecycleSnapshot Snapshot => new(
        _state,
        _registrationCount,
        _forwardedLaunchIntents.Count,
        _lastChangedAtUtc);

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is DaemonLifecycleState.Running or DaemonLifecycleState.Starting)
            {
                return;
            }

            ThrowIfStopped();
            SetState(DaemonLifecycleState.Starting);
            try
            {
                await _ipcServer.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                SetState(DaemonLifecycleState.Created);
                throw;
            }

            SetState(DaemonLifecycleState.Running);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask UpdateRegistrationCountAsync(int registrationCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(registrationCount);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _registrationCount = registrationCount;
            _lastChangedAtUtc = _timeProvider.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RecordForwardedLaunchIntentAsync(
        DaemonLaunchIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            _forwardedLaunchIntents.Enqueue(intent);
            _lastChangedAtUtc = _timeProvider.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        List<Exception> errors = [];

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == DaemonLifecycleState.Stopped)
            {
                return;
            }

            SetState(DaemonLifecycleState.ShuttingDown);
        }
        finally
        {
            _gate.Release();
        }

        await RunShutdownStepAsync(
            () => _ipcServer.StopAsync(cancellationToken),
            errors).ConfigureAwait(false);
        await RunShutdownStepAsync(
            () => _registrationStore.ClearTransientRegistrationsAsync(cancellationToken),
            errors).ConfigureAwait(false);
        await RunShutdownStepAsync(
            () => _runtime.StopAsync(cancellationToken),
            errors).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(DaemonLifecycleState.Stopped);
            DisposeLease();
        }
        finally
        {
            _gate.Release();
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Daemon shutdown completed with boundary failures.", errors);
        }
    }

    private async ValueTask RunShutdownStepAsync(
        Func<ValueTask> shutdownStep,
        List<Exception> errors)
    {
        try
        {
            await shutdownStep().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private void ThrowIfStopped()
    {
        if (_state == DaemonLifecycleState.Stopped)
        {
            throw new InvalidOperationException("The daemon lifecycle has already stopped.");
        }
    }

    private void SetState(DaemonLifecycleState state)
    {
        _state = state;
        _lastChangedAtUtc = _timeProvider.GetUtcNow();
    }

    private void DisposeLease()
    {
        if (_leaseDisposed)
        {
            return;
        }

        _singletonLease.Dispose();
        _leaseDisposed = true;
    }
}

public sealed class NoopDaemonIpcServer : IDaemonIpcServer
{
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryTransientRegistrationStore : ITransientRegistrationStore
{
    public ValueTask ClearTransientRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class NoopCadderOwnedRuntime : ICadderOwnedRuntime
{
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
