using Cadder.Contracts;

namespace Cadder.Daemon;

public interface ICadderDaemonHost
{
    ValueTask<DaemonStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IRegistrationStore
{
    ValueTask UpsertAsync(EntrypointRegistration registration, CancellationToken cancellationToken = default);

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

    ValueTask<QueryGuiStateResponse> QueryStateAsync(
        QueryGuiStateRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGuiStateProjector
{
    GuiStateSnapshot Project(DaemonStateSnapshot snapshot);
}

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
