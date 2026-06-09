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
    Unhealthy = 4
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
    ShimRunMetadata? ShimRun = null);

public sealed record ShimRunMetadata(
    string? Adapter,
    string[] RawArguments);

public sealed record RealCaddyBinaryIdentity(
    string? ResolvedPath,
    string? FileIdentity);

public sealed record RealCaddyRuntimeState(
    RealCaddyRuntimeStatus Status,
    RealCaddyBinaryIdentity? Binary,
    string? Version);

public sealed record GuiStateSnapshot(
    DateTimeOffset CapturedAtUtc,
    EntrypointRegistration[] Registrations,
    RealCaddyRuntimeState RealCaddyRuntime);
