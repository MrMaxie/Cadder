using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class CadderIpcEndpoint : ICadderIpcEndpoint
{
    private readonly IRegistrationStore _registrationStore;
    private readonly IRealCaddyRuntimeAdapter _realCaddyRuntime;
    private readonly Func<int, CancellationToken, ValueTask>? _registrationCountChanged;
    private readonly TimeProvider _timeProvider;

    public CadderIpcEndpoint(
        IRegistrationStore registrationStore,
        IRealCaddyRuntimeAdapter realCaddyRuntime,
        Func<int, CancellationToken, ValueTask>? registrationCountChanged = null,
        TimeProvider? timeProvider = null)
    {
        _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
        _realCaddyRuntime = realCaddyRuntime ?? throw new ArgumentNullException(nameof(realCaddyRuntime));
        _registrationCountChanged = registrationCountChanged;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<RegisterEntrypointResponse> RegisterAsync(
        RegisterEntrypointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Registration);

        await _registrationStore.UpsertAsync(request.Registration, cancellationToken).ConfigureAwait(false);
        await PublishRegistrationCountAsync(cancellationToken).ConfigureAwait(false);

        return new RegisterEntrypointResponse(
            request.RequestId,
            true,
            "Entrypoint registered.",
            request.Registration.RegistrationId);
    }

    public async ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
        UnregisterEntrypointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _registrationStore.RemoveAsync(
            request.RegistrationId,
            request.ShimSessionNonce,
            cancellationToken).ConfigureAwait(false);
        await PublishRegistrationCountAsync(cancellationToken).ConfigureAwait(false);

        return new UnregisterEntrypointResponse(
            request.RequestId,
            true,
            "Entrypoint unregistered.");
    }

    public async ValueTask<QueryGuiStateResponse> QueryStateAsync(
        QueryGuiStateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var runtime = await _realCaddyRuntime.InspectAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = new DaemonStateSnapshot(
            _timeProvider.GetUtcNow(),
            registrations,
            runtime);

        return new QueryGuiStateResponse(
            request.RequestId,
            true,
            "State snapshot returned.",
            new GuiStateProjector().Project(snapshot));
    }

    private async ValueTask PublishRegistrationCountAsync(CancellationToken cancellationToken)
    {
        if (_registrationCountChanged is null)
        {
            return;
        }

        var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
        await _registrationCountChanged(registrations.Count, cancellationToken).ConfigureAwait(false);
    }
}
