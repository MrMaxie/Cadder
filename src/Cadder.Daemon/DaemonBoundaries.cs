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
    RealCaddyRuntimeState RealCaddyRuntime);

public sealed class GuiStateProjector : IGuiStateProjector
{
  public GuiStateSnapshot Project(DaemonStateSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    return new GuiStateSnapshot(
        snapshot.CapturedAtUtc,
        snapshot.Registrations.ToArray(),
        snapshot.RealCaddyRuntime);
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
}
