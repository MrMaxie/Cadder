namespace Cadder.Contracts;

public abstract record CadderIpcRequest(string RequestId);

public abstract record CadderIpcResponse(
    string RequestId,
    bool Accepted,
    string? Message);

public sealed record RegisterEntrypointRequest(
    string RequestId,
    EntrypointRegistration Registration) : CadderIpcRequest(RequestId);

public sealed record RegisterEntrypointResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    string? RegistrationId) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record UnregisterEntrypointRequest(
    string RequestId,
    string RegistrationId,
    string ShimSessionNonce) : CadderIpcRequest(RequestId);

public sealed record UnregisterEntrypointResponse(
    string RequestId,
    bool Accepted,
    string? Message) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record UpdateEntrypointRequest(
    string RequestId,
    string RegistrationId,
    string ShimSessionNonce,
    SourcePath? SourceWorkingDirectory,
    SourcePath? SourceConfigPath,
    RegisteredDomain[]? RegisteredDomains,
    ActivationState? ActivationState,
    ShimRunMetadata? ShimRun) : CadderIpcRequest(RequestId);

public sealed record UpdateEntrypointResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    EntrypointRegistration? Registration) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record ListEntrypointsRequest(
    string RequestId) : CadderIpcRequest(RequestId);

public sealed record ListEntrypointsResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    EntrypointRegistration[] Registrations) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record ToggleEntrypointRequest(
    string RequestId,
    string RegistrationId,
    string ShimSessionNonce,
    bool Enabled) : CadderIpcRequest(RequestId);

public sealed record ToggleEntrypointResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    EntrypointRegistration? Registration) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record HeartbeatEntrypointRequest(
    string RequestId,
    string RegistrationId,
    string ShimSessionNonce) : CadderIpcRequest(RequestId);

public sealed record HeartbeatEntrypointResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    EntrypointRegistration? Registration) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record QueryGuiStateRequest(
    string RequestId) : CadderIpcRequest(RequestId);

public sealed record QueryGuiStateResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    GuiStateSnapshot? Snapshot) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record QueryCaddyLogsRequest(
    string RequestId,
    LogStreamIdentity Stream,
    int? Limit = null,
    string? Cursor = null,
    CaddyLogSeverity? MinimumSeverity = null,
    DateTimeOffset? SinceUtc = null,
    DateTimeOffset? UntilUtc = null) : CadderIpcRequest(RequestId);

public sealed record QueryCaddyLogsResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    LogStreamIdentity Stream,
    CaddyLogStreamStatus StreamStatus,
    CaddyLogEntry[] Entries,
    string? NextCursor,
    bool HasGap,
    bool HasMoreBefore,
    bool TruncatedByRetention) : CadderIpcResponse(RequestId, Accepted, Message);

public sealed record SubscribeGuiStateRequest(
    string RequestId) : CadderIpcRequest(RequestId);

public sealed record GuiStateChangedEvent(
    string RequestId,
    long SequenceNumber,
    GuiStateChangeKind ChangeKind,
    GuiStateSnapshot Snapshot,
    string? RegistrationId);
