namespace Cadder.Contracts;

public enum ActivationState
{
  Unknown = 0,
  Registered = 1,
  Activating = 2,
  Active = 3,
  Inactive = 4,
  Faulted = 5
}

public enum RealCaddyRuntimeStatus
{
  Unknown = 0,
  NotResolved = 1,
  Resolved = 2,
  Running = 3,
  Unhealthy = 4,
  Idle = 5
}

public enum GuiStateChangeKind
{
  Snapshot = 0,
  RegistrationsChanged = 1,
  RuntimeChanged = 2
}

public enum CaddyConfigApplyStatus
{
  Unknown = 0,
  NotApplied = 1,
  Applied = 2,
  Failed = 3,
  Idle = 4
}

public sealed record SourcePath(
    string Raw,
    string? Canonical);

public sealed record DomainName(
    string Raw,
    string? Canonical);

public sealed record EntrypointInstanceIdentity(
    string InstanceId,
    DateTimeOffset StartedAtUtc,
    string ShimSessionNonce);

public sealed record OwnerProcessIdentity(
    int ProcessId,
    DateTimeOffset ProcessStartTimeUtc,
    string ShimSessionNonce,
    string? ExecutablePath);

public sealed record LogStreamIdentity(
    string StreamId,
    string? DomainKey,
    string Channel);

public sealed record RegisteredDomain(
    DomainName Name,
    ActivationState ActivationState,
    LogStreamIdentity LogStream);

public sealed record EntrypointRegistration(
    string RegistrationId,
    EntrypointInstanceIdentity EntrypointInstance,
    SourcePath SourceWorkingDirectory,
    SourcePath SourceConfigPath,
    RegisteredDomain[] RegisteredDomains,
    ActivationState ActivationState,
    OwnerProcessIdentity OwnerProcess,
    LogStreamIdentity LogStream,
    ShimRunMetadata? ShimRun = null,
    DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset LastHeartbeatUtc = default);

public sealed record ShimRunMetadata(
    string? Adapter,
    string[] RawArguments,
    string CommandLine = "");

public sealed record RealCaddyBinaryIdentity(
    string? ResolvedPath,
    string? FileIdentity);

public sealed record RealCaddyProcessIdentity(
    int ProcessId,
    DateTimeOffset ProcessStartTimeUtc,
    bool OwnedByCadder);

public sealed record CaddyRuntimeDiagnostic(
    string Code,
    string Message,
    string? Operation);

public sealed record RealCaddyRuntimeState(
    RealCaddyRuntimeStatus Status,
    RealCaddyBinaryIdentity? Binary,
    string? Version,
    RealCaddyProcessIdentity? Process = null,
    string? AdminEndpoint = null,
    CaddyRuntimeDiagnostic[]? Diagnostics = null);

public sealed record CaddyConfigDiagnostic(
    string Code,
    string Message,
    string? DomainKey,
    string[] SourceConfigPaths);

public sealed record CaddyConfigState(
    CaddyConfigApplyStatus Status,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? LastSuccessfulReloadAtUtc,
    string? EffectiveConfigHash,
    CaddyConfigDiagnostic[] Diagnostics);

public sealed record GuiStateSnapshot(
    DateTimeOffset CapturedAtUtc,
    EntrypointRegistration[] Registrations,
    RealCaddyRuntimeState RealCaddyRuntime,
    CaddyConfigState? CaddyConfig = null);
