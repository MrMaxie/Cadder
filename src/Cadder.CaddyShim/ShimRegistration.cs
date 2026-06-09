using System.Diagnostics;
using Cadder.Contracts;

namespace Cadder.CaddyShim;

public sealed record ShimProcessIdentity(
    int ProcessId,
    DateTimeOffset ProcessStartTimeUtc,
    string? ExecutablePath);

public interface IShimProcessIdentityProvider
{
    ShimProcessIdentity GetCurrentProcessIdentity();
}

public sealed class CurrentShimProcessIdentityProvider : IShimProcessIdentityProvider
{
    private readonly TimeProvider _timeProvider;

    public CurrentShimProcessIdentityProvider(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ShimProcessIdentity GetCurrentProcessIdentity()
    {
        using var process = Process.GetCurrentProcess();

        DateTimeOffset startedAtUtc;
        try
        {
            startedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (InvalidOperationException)
        {
            startedAtUtc = _timeProvider.GetUtcNow();
        }

        return new ShimProcessIdentity(
            process.Id,
            startedAtUtc,
            Environment.ProcessPath);
    }
}

public static class ShimRegistrationFactory
{
    public static EntrypointRegistration Create(
        ShimRunCommand command,
        ShimProcessIdentity processIdentity,
        DateTimeOffset startedAtUtc,
        string shimSessionNonce)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(processIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(shimSessionNonce);

        var registrationId = $"shim-{shimSessionNonce}";
        var workingDirectory = Path.GetFullPath(command.WorkingDirectory);
        var logStream = new LogStreamIdentity($"entrypoint-{shimSessionNonce}", null, "shim");

        return new EntrypointRegistration(
            registrationId,
            new EntrypointInstanceIdentity(registrationId, startedAtUtc, shimSessionNonce),
            new SourcePath(command.WorkingDirectory, workingDirectory),
            new SourcePath(command.RawConfigPath, command.CanonicalConfigPath),
            [],
            ActivationState.Registered,
            new OwnerProcessIdentity(
                processIdentity.ProcessId,
                processIdentity.ProcessStartTimeUtc,
                shimSessionNonce,
                processIdentity.ExecutablePath),
            logStream,
            new ShimRunMetadata(command.Adapter, command.RawArguments));
    }
}
