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

  ValueTask<RealCaddyRuntimeState> EnsureRunningAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default);

  ValueTask<CaddyRuntimeOperationResult> ValidateConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default);

  ValueTask<CaddyRuntimeOperationResult> ReloadConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default);

  ValueTask<RealCaddyRuntimeState> EnterIdleAsync(CancellationToken cancellationToken = default);
}

public interface ICaddyLogSink
{
  bool TryWrite(CaddyLogWriteRequest request);
}

public interface ICaddyLogStore : ICaddyLogSink
{
  CaddyLogQueryResult Query(CaddyLogQuery query);
}

public interface ICaddyLogRedactor
{
  string Redact(string value);

  CaddyRuntimeDiagnostic Redact(CaddyRuntimeDiagnostic diagnostic);

  CaddyConfigDiagnostic Redact(CaddyConfigDiagnostic diagnostic);

  ShimRunMetadata Redact(ShimRunMetadata shimRun);
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

  ValueTask<QueryCaddyLogsResponse> QueryCaddyLogsAsync(
      QueryCaddyLogsRequest request,
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

public sealed record CaddyLogWriteRequest(
    LogStreamIdentity Stream,
    CaddyLogSeverity Severity,
    CaddyLogAttributionKind AttributionKind,
    CaddyLogEntryKind EntryKind,
    string RawMessage,
    DateTimeOffset? TimestampUtc = null,
    string? DomainKey = null,
    string? SourceRegistrationId = null,
    string? SourceInstanceId = null,
    string? Operation = null);

public sealed record CaddyLogQuery(
    LogStreamIdentity Stream,
    int Limit,
    long? AfterSequence,
    CaddyLogSeverity? MinimumSeverity,
    DateTimeOffset? SinceUtc,
    DateTimeOffset? UntilUtc);

public sealed record CaddyLogQueryResult(
    CaddyLogEntry[] Entries,
    long? NextSequence,
    bool HasGap,
    bool HasMoreBefore,
    bool TruncatedByRetention);

public sealed class GuiStateProjector : IGuiStateProjector
{
  private readonly ICaddyLogRedactor _redactor;

  public GuiStateProjector(ICaddyLogRedactor? redactor = null)
  {
    _redactor = redactor ?? new CaddyLogRedactor();
  }

  public GuiStateSnapshot Project(DaemonStateSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    return new GuiStateSnapshot(
        snapshot.CapturedAtUtc,
        [.. snapshot.Registrations.Select(RedactRegistration)],
        RedactRuntimeState(snapshot.RealCaddyRuntime),
        RedactConfigState(snapshot.CaddyConfig));
  }

  private EntrypointRegistration RedactRegistration(EntrypointRegistration registration)
  {
    return registration.ShimRun is null
        ? registration
        : registration with { ShimRun = _redactor.Redact(registration.ShimRun) };
  }

  private RealCaddyRuntimeState RedactRuntimeState(RealCaddyRuntimeState state)
  {
    return state with
    {
      Diagnostics = state.Diagnostics is null
          ? null
          : [.. state.Diagnostics.Select(_redactor.Redact)]
    };
  }

  private CaddyConfigState? RedactConfigState(CaddyConfigState? state)
  {
    return state is null
        ? null
        : state with { Diagnostics = [.. state.Diagnostics.Select(_redactor.Redact)] };
  }
}

public sealed class NoopRealCaddyRuntimeAdapter : IRealCaddyRuntimeAdapter
{
  private RealCaddyRuntimeState _state = new(
      RealCaddyRuntimeStatus.NotResolved,
      null,
      null,
      Diagnostics: []);

  public ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult(_state);
  }

  public ValueTask<RealCaddyRuntimeState> EnsureRunningAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);
    cancellationToken.ThrowIfCancellationRequested();

    _state = new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Running,
        null,
        null,
        Diagnostics: []);
    return ValueTask.FromResult(_state);
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

  public ValueTask<RealCaddyRuntimeState> EnterIdleAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    _state = new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Idle,
        null,
        null,
        Diagnostics: []);
    return ValueTask.FromResult(_state);
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
