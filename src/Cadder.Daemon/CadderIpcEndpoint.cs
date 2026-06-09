using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class CadderIpcEndpoint : ICadderIpcEndpoint
{
  private readonly IRegistrationStore _registrationStore;
  private readonly IRealCaddyRuntimeAdapter _realCaddyRuntime;
  private readonly ICaddyConfigCoordinator _caddyConfigCoordinator;
  private readonly IGuiStateChangeBroadcaster _guiStateBroadcaster;
  private readonly IGuiStateProjector _guiStateProjector;
  private readonly Func<int, CancellationToken, ValueTask>? _registrationCountChanged;
  private readonly TimeProvider _timeProvider;

  public CadderIpcEndpoint(
      IRegistrationStore registrationStore,
      IRealCaddyRuntimeAdapter realCaddyRuntime,
      ICaddyConfigCoordinator? caddyConfigCoordinator = null,
      IGuiStateChangeBroadcaster? guiStateBroadcaster = null,
      IGuiStateProjector? guiStateProjector = null,
      Func<int, CancellationToken, ValueTask>? registrationCountChanged = null,
      TimeProvider? timeProvider = null)
  {
    _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
    _realCaddyRuntime = realCaddyRuntime ?? throw new ArgumentNullException(nameof(realCaddyRuntime));
    _caddyConfigCoordinator = caddyConfigCoordinator ?? new NoopCaddyConfigCoordinator();
    _guiStateBroadcaster = guiStateBroadcaster ?? new InMemoryGuiStateChangeBroadcaster();
    _guiStateProjector = guiStateProjector ?? new GuiStateProjector();
    _registrationCountChanged = registrationCountChanged;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public async ValueTask<RegisterEntrypointResponse> RegisterAsync(
      RegisterEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Registration);

    if (!TryValidateRegistration(request.Registration, out var validationMessage))
    {
      return new RegisterEntrypointResponse(
          request.RequestId,
          false,
          validationMessage,
          null);
    }

    var preparedRegistration = await _caddyConfigCoordinator
        .PrepareRegistrationAsync(request.Registration, cancellationToken)
        .ConfigureAwait(false);
    var registration = await _registrationStore.RegisterAsync(
        preparedRegistration,
        _timeProvider.GetUtcNow(),
        cancellationToken).ConfigureAwait(false);
    var configState = await ApplyCurrentConfigAsync(cancellationToken).ConfigureAwait(false);
    await PublishRegistrationsChangedAsync(registration.RegistrationId, true, cancellationToken)
        .ConfigureAwait(false);

    return new RegisterEntrypointResponse(
        request.RequestId,
        true,
        MessageWithConfigState("Entrypoint registered.", configState),
        registration.RegistrationId);
  }

  public async ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
      UnregisterEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var removed = await _registrationStore.RemoveAsync(
        request.RegistrationId,
        request.ShimSessionNonce,
        cancellationToken).ConfigureAwait(false);
    if (removed)
    {
      await ApplyCurrentConfigAsync(cancellationToken).ConfigureAwait(false);
      await PublishRegistrationsChangedAsync(request.RegistrationId, true, cancellationToken)
          .ConfigureAwait(false);
    }

    return new UnregisterEntrypointResponse(
        request.RequestId,
        removed,
        removed
            ? "Entrypoint unregistered."
            : "Entrypoint was not found for the requested owner.");
  }

  public async ValueTask<UpdateEntrypointResponse> UpdateAsync(
      UpdateEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var patch = await _caddyConfigCoordinator.PreparePatchAsync(
        new EntrypointRegistrationPatch(
            request.RegistrationId,
            request.ShimSessionNonce,
            request.SourceWorkingDirectory,
            request.SourceConfigPath,
            request.RegisteredDomains,
            request.ActivationState,
            request.ShimRun),
        cancellationToken).ConfigureAwait(false);
    var updated = await _registrationStore.UpdateAsync(
        patch,
        _timeProvider.GetUtcNow(),
        cancellationToken).ConfigureAwait(false);
    CaddyConfigState? configState = null;
    if (updated is not null)
    {
      configState = await ApplyCurrentConfigAsync(cancellationToken).ConfigureAwait(false);
      await PublishRegistrationsChangedAsync(updated.RegistrationId, true, cancellationToken)
          .ConfigureAwait(false);
    }

    return new UpdateEntrypointResponse(
        request.RequestId,
        updated is not null,
        updated is not null
            ? MessageWithConfigState("Entrypoint updated.", configState)
            : "Entrypoint was not found for the requested owner.",
        updated);
  }

  public async ValueTask<ListEntrypointsResponse> ListAsync(
      ListEntrypointsRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);

    return new ListEntrypointsResponse(
        request.RequestId,
        true,
        "Entrypoints returned.",
        registrations.ToArray());
  }

  public async ValueTask<ToggleEntrypointResponse> ToggleAsync(
      ToggleEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var updated = await _registrationStore.ToggleAsync(
        request.RegistrationId,
        request.ShimSessionNonce,
        request.Enabled,
        _timeProvider.GetUtcNow(),
        cancellationToken).ConfigureAwait(false);
    CaddyConfigState? configState = null;
    if (updated is not null)
    {
      configState = await ApplyCurrentConfigAsync(cancellationToken).ConfigureAwait(false);
      await PublishRegistrationsChangedAsync(updated.RegistrationId, true, cancellationToken)
          .ConfigureAwait(false);
    }

    return new ToggleEntrypointResponse(
        request.RequestId,
        updated is not null,
        updated is not null
            ? MessageWithConfigState("Entrypoint toggled.", configState)
            : "Entrypoint was not found for the requested owner.",
        updated);
  }

  public async ValueTask<HeartbeatEntrypointResponse> HeartbeatAsync(
      HeartbeatEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var updated = await _registrationStore.HeartbeatAsync(
        request.RegistrationId,
        request.ShimSessionNonce,
        _timeProvider.GetUtcNow(),
        cancellationToken).ConfigureAwait(false);
    if (updated is not null)
    {
      await PublishRegistrationsChangedAsync(updated.RegistrationId, false, cancellationToken)
          .ConfigureAwait(false);
    }

    return new HeartbeatEntrypointResponse(
        request.RequestId,
        updated is not null,
        updated is not null
            ? "Heartbeat accepted."
            : "Entrypoint was not found for the requested owner.",
        updated);
  }

  public async ValueTask<QueryGuiStateResponse> QueryStateAsync(
      QueryGuiStateRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    var snapshot = await CreateGuiStateSnapshotAsync(cancellationToken).ConfigureAwait(false);

    return new QueryGuiStateResponse(
        request.RequestId,
        true,
        "State snapshot returned.",
        snapshot);
  }

  public IAsyncEnumerable<GuiStateChangedEvent> SubscribeGuiStateAsync(
      SubscribeGuiStateRequest request,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    return _guiStateBroadcaster.SubscribeAsync(
        request.RequestId,
        CreateGuiStateSnapshotAsync,
        cancellationToken);
  }

  private async ValueTask PublishRegistrationsChangedAsync(
      string? registrationId,
      bool publishRegistrationCount,
      CancellationToken cancellationToken)
  {
    if (publishRegistrationCount && _registrationCountChanged is not null)
    {
      var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
      await _registrationCountChanged(registrations.Count, cancellationToken).ConfigureAwait(false);
    }

    var snapshot = await CreateGuiStateSnapshotAsync(cancellationToken).ConfigureAwait(false);
    await _guiStateBroadcaster.PublishAsync(
        GuiStateChangeKind.RegistrationsChanged,
        snapshot,
        registrationId,
        cancellationToken).ConfigureAwait(false);
  }

  private async ValueTask<CaddyConfigState> ApplyCurrentConfigAsync(CancellationToken cancellationToken)
  {
    var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
    return await _caddyConfigCoordinator.ApplyAsync(registrations, cancellationToken).ConfigureAwait(false);
  }

  private async ValueTask<GuiStateSnapshot> CreateGuiStateSnapshotAsync(CancellationToken cancellationToken)
  {
    var registrations = await _registrationStore.ListAsync(cancellationToken).ConfigureAwait(false);
    var runtime = await _realCaddyRuntime.InspectAsync(cancellationToken).ConfigureAwait(false);
    var snapshot = new DaemonStateSnapshot(
        _timeProvider.GetUtcNow(),
        registrations,
        runtime,
        _caddyConfigCoordinator.CurrentState);

    return _guiStateProjector.Project(snapshot);
  }

  private static string MessageWithConfigState(string successMessage, CaddyConfigState? configState)
  {
    if (configState?.Status != CaddyConfigApplyStatus.Failed)
    {
      return successMessage;
    }

    var detail = configState.Diagnostics.FirstOrDefault()?.Message ?? "Caddy config reload failed.";
    return $"{successMessage} {detail}";
  }

  private static bool TryValidateRegistration(
      EntrypointRegistration registration,
      out string message)
  {
    if (string.IsNullOrWhiteSpace(registration.RegistrationId))
    {
      message = "RegistrationId is required.";
      return false;
    }

    if (!string.Equals(
        registration.RegistrationId,
        registration.EntrypointInstance.InstanceId,
        StringComparison.Ordinal))
    {
      message = "RegistrationId must match the entrypoint instance id.";
      return false;
    }

    if (string.IsNullOrWhiteSpace(registration.EntrypointInstance.ShimSessionNonce)
        || string.IsNullOrWhiteSpace(registration.OwnerProcess.ShimSessionNonce)
        || !string.Equals(
            registration.EntrypointInstance.ShimSessionNonce,
            registration.OwnerProcess.ShimSessionNonce,
            StringComparison.Ordinal))
    {
      message = "Entrypoint and owner shim session nonce values must match.";
      return false;
    }

    message = string.Empty;
    return true;
  }
}
