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

public sealed record QueryGuiStateRequest(
    string RequestId) : CadderIpcRequest(RequestId);

public sealed record QueryGuiStateResponse(
    string RequestId,
    bool Accepted,
    string? Message,
    GuiStateSnapshot? Snapshot) : CadderIpcResponse(RequestId, Accepted, Message);
