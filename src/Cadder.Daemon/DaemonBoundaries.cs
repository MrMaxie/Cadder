using Cadder.Contracts;

namespace Cadder.Daemon;

public interface ICadderDaemonHost
{
  ValueTask<DaemonStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IRegistrationStore
{
  ValueTask<EntrypointRegistration> RegisterAsync(
      EntrypointRegistration registration,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default);

  ValueTask<EntrypointRegistration?> UpdateAsync(
      EntrypointRegistrationPatch patch,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default);

  ValueTask<EntrypointRegistration?> ToggleAsync(
      string registrationId,
      string shimSessionNonce,
      bool enabled,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default);

  ValueTask<EntrypointRegistration?> HeartbeatAsync(
      string registrationId,
      string shimSessionNonce,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default);

  ValueTask<bool> RemoveAsync(
      string registrationId,
      string? shimSessionNonce = null,
      CancellationToken cancellationToken = default);

  ValueTask<int> RemoveByOwnerAsync(
      OwnerProcessIdentity owner,
      CancellationToken cancellationToken = default);

  ValueTask<EntrypointRegistration?> FindAsync(string registrationId, CancellationToken cancellationToken = default);

  ValueTask<IReadOnlyList<EntrypointRegistration>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IRealCaddyRuntimeAdapter
{
  ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default);

  ValueTask<CaddyRuntimeOperationResult> ValidateConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default);

  ValueTask<CaddyRuntimeOperationResult> ReloadConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default);
}

public interface ICaddyConfigCoordinator
{
  CaddyConfigState CurrentState { get; }

  ValueTask<EntrypointRegistration> PrepareRegistrationAsync(
      EntrypointRegistration registration,
      CancellationToken cancellationToken = default);

  ValueTask<EntrypointRegistrationPatch> PreparePatchAsync(
      EntrypointRegistrationPatch patch,
      CancellationToken cancellationToken = default);

  ValueTask<CaddyConfigState> ApplyAsync(
      IReadOnlyList<EntrypointRegistration> registrations,
      CancellationToken cancellationToken = default);
}

public interface ICadderIpcEndpoint
{
  ValueTask<RegisterEntrypointResponse> RegisterAsync(
      RegisterEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
      UnregisterEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<UpdateEntrypointResponse> UpdateAsync(
      UpdateEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<ListEntrypointsResponse> ListAsync(
      ListEntrypointsRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<ToggleEntrypointResponse> ToggleAsync(
      ToggleEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<HeartbeatEntrypointResponse> HeartbeatAsync(
      HeartbeatEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<QueryGuiStateResponse> QueryStateAsync(
      QueryGuiStateRequest request,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<GuiStateChangedEvent> SubscribeGuiStateAsync(
      SubscribeGuiStateRequest request,
      CancellationToken cancellationToken = default);
}

public interface IGuiStateProjector
{
  GuiStateSnapshot Project(DaemonStateSnapshot snapshot);
}

public interface IGuiStateChangeBroadcaster
{
  IAsyncEnumerable<GuiStateChangedEvent> SubscribeAsync(
      string requestId,
      Func<CancellationToken, ValueTask<GuiStateSnapshot>> initialSnapshotFactory,
      CancellationToken cancellationToken = default);

  ValueTask PublishAsync(
      GuiStateChangeKind changeKind,
      GuiStateSnapshot snapshot,
      string? registrationId = null,
      CancellationToken cancellationToken = default);
}

public enum OwnerProcessLiveness
{
  Unknown = 0,
  Alive = 1,
  Dead = 2
}

public interface IOwnerProcessProbe
{
  OwnerProcessLiveness GetLiveness(OwnerProcessIdentity owner);
}

public interface IRegistrationOwnerWatcher
{
  ValueTask StartAsync(CancellationToken cancellationToken = default);

  ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public sealed record EntrypointRegistrationPatch(
    string RegistrationId,
    string ShimSessionNonce,
    SourcePath? SourceWorkingDirectory,
    SourcePath? SourceConfigPath,
    RegisteredDomain[]? RegisteredDomains,
    ActivationState? ActivationState,
    ShimRunMetadata? ShimRun);

public sealed record DaemonStateSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<EntrypointRegistration> Registrations,
    RealCaddyRuntimeState RealCaddyRuntime,
    CaddyConfigState? CaddyConfig = null);

public sealed record CaddyRuntimeConfig(string Content);

public sealed record CaddyRuntimeOperationResult(
    bool Succeeded,
    string? Message,
    CaddyConfigDiagnostic[] Diagnostics)
{
  public static CaddyRuntimeOperationResult Success(string? message = null)
  {
    return new CaddyRuntimeOperationResult(true, message, []);
  }

  public static CaddyRuntimeOperationResult Failure(
      string message,
      CaddyConfigDiagnostic[]? diagnostics = null)
  {
    return new CaddyRuntimeOperationResult(false, message, diagnostics ?? []);
  }
}

public sealed class GuiStateProjector : IGuiStateProjector
{
  public GuiStateSnapshot Project(DaemonStateSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    return new GuiStateSnapshot(
        snapshot.CapturedAtUtc,
        snapshot.Registrations.ToArray(),
        snapshot.RealCaddyRuntime,
        snapshot.CaddyConfig);
  }
}

public sealed class NoopRealCaddyRuntimeAdapter : IRealCaddyRuntimeAdapter
{
  public ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default)
  {
    return ValueTask.FromResult(new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.NotResolved,
        null,
        null));
  }

  public ValueTask<CaddyRuntimeOperationResult> ValidateConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(CaddyRuntimeOperationResult.Success("Config validation skipped."));
  }

  public ValueTask<CaddyRuntimeOperationResult> ReloadConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(CaddyRuntimeOperationResult.Success("Config reload skipped."));
  }
}

public sealed class NoopCaddyConfigCoordinator : ICaddyConfigCoordinator
{
  public CaddyConfigState CurrentState { get; } = new(
      CaddyConfigApplyStatus.NotApplied,
      null,
      null,
      null,
      []);

  public ValueTask<EntrypointRegistration> PrepareRegistrationAsync(
      EntrypointRegistration registration,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registration);
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(registration);
  }

  public ValueTask<EntrypointRegistrationPatch> PreparePatchAsync(
      EntrypointRegistrationPatch patch,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(patch);
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(patch);
  }

  public ValueTask<CaddyConfigState> ApplyAsync(
      IReadOnlyList<EntrypointRegistration> registrations,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registrations);
    cancellationToken.ThrowIfCancellationRequested();

    return ValueTask.FromResult(CurrentState);
  }
}
