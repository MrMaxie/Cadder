using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class InMemoryRegistrationStore : IRegistrationStore, ITransientRegistrationStore
{
  private readonly object _gate = new();
  private readonly Dictionary<string, EntrypointRegistration> _registrations = new(StringComparer.Ordinal);

  public ValueTask<EntrypointRegistration> RegisterAsync(
      EntrypointRegistration registration,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registration);
    ArgumentException.ThrowIfNullOrWhiteSpace(registration.RegistrationId);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      var createdAtUtc = registration.CreatedAtUtc == default
          ? observedAtUtc
          : registration.CreatedAtUtc;
      if (_registrations.TryGetValue(registration.RegistrationId, out var existing)
          && existing.CreatedAtUtc != default)
      {
        createdAtUtc = existing.CreatedAtUtc;
      }

      var stored = registration with
      {
        CreatedAtUtc = createdAtUtc,
        LastHeartbeatUtc = observedAtUtc
      };

      _registrations[stored.RegistrationId] = stored;
      return ValueTask.FromResult(stored);
    }
  }

  public ValueTask<EntrypointRegistration?> UpdateAsync(
      EntrypointRegistrationPatch patch,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(patch);
    ArgumentException.ThrowIfNullOrWhiteSpace(patch.RegistrationId);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      if (!TryGetOwnedRegistration(patch.RegistrationId, patch.ShimSessionNonce, out var existing))
      {
        return ValueTask.FromResult<EntrypointRegistration?>(null);
      }

      var updated = existing with
      {
        SourceWorkingDirectory = patch.SourceWorkingDirectory ?? existing.SourceWorkingDirectory,
        SourceConfigPath = patch.SourceConfigPath ?? existing.SourceConfigPath,
        RegisteredDomains = patch.RegisteredDomains ?? existing.RegisteredDomains,
        ActivationState = patch.ActivationState ?? existing.ActivationState,
        ShimRun = patch.ShimRun ?? existing.ShimRun,
        LastHeartbeatUtc = observedAtUtc
      };

      _registrations[updated.RegistrationId] = updated;
      return ValueTask.FromResult<EntrypointRegistration?>(updated);
    }
  }

  public ValueTask<EntrypointRegistration?> ToggleAsync(
      string registrationId,
      string shimSessionNonce,
      bool enabled,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    ArgumentException.ThrowIfNullOrWhiteSpace(shimSessionNonce);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      if (!TryGetOwnedRegistration(registrationId, shimSessionNonce, out var existing))
      {
        return ValueTask.FromResult<EntrypointRegistration?>(null);
      }

      var updated = existing with
      {
        ActivationState = enabled ? ActivationState.Active : ActivationState.Inactive,
        LastHeartbeatUtc = observedAtUtc
      };

      _registrations[updated.RegistrationId] = updated;
      return ValueTask.FromResult<EntrypointRegistration?>(updated);
    }
  }

  public ValueTask<EntrypointRegistration?> HeartbeatAsync(
      string registrationId,
      string shimSessionNonce,
      DateTimeOffset observedAtUtc,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    ArgumentException.ThrowIfNullOrWhiteSpace(shimSessionNonce);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      if (!TryGetOwnedRegistration(registrationId, shimSessionNonce, out var existing))
      {
        return ValueTask.FromResult<EntrypointRegistration?>(null);
      }

      var updated = existing with { LastHeartbeatUtc = observedAtUtc };
      _registrations[updated.RegistrationId] = updated;
      return ValueTask.FromResult<EntrypointRegistration?>(updated);
    }
  }

  public ValueTask<bool> RemoveAsync(
      string registrationId,
      string? shimSessionNonce = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      if (!_registrations.TryGetValue(registrationId, out var registration))
      {
        return ValueTask.FromResult(false);
      }

      if (shimSessionNonce is not null && !IsOwnedByShimSession(registration, shimSessionNonce))
      {
        return ValueTask.FromResult(false);
      }

      return ValueTask.FromResult(_registrations.Remove(registrationId));
    }
  }

  public ValueTask<int> RemoveByOwnerAsync(
      OwnerProcessIdentity owner,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(owner);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      var ownedRegistrationIds = _registrations
          .Where(pair => IsOwnedByProcess(pair.Value, owner))
          .Select(static pair => pair.Key)
          .ToArray();

      foreach (var registrationId in ownedRegistrationIds)
      {
        _registrations.Remove(registrationId);
      }

      return ValueTask.FromResult(ownedRegistrationIds.Length);
    }
  }

  public ValueTask<EntrypointRegistration?> FindAsync(
      string registrationId,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      _registrations.TryGetValue(registrationId, out var registration);
      return ValueTask.FromResult(registration);
    }
  }

  public ValueTask<IReadOnlyList<EntrypointRegistration>> ListAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      IReadOnlyList<EntrypointRegistration> registrations = [.. _registrations.Values
          .OrderBy(static registration => registration.RegistrationId, StringComparer.Ordinal)];
      return ValueTask.FromResult(registrations);
    }
  }

  public ValueTask ClearTransientRegistrationsAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    lock (_gate)
    {
      _registrations.Clear();
    }

    return ValueTask.CompletedTask;
  }

  private bool TryGetOwnedRegistration(
      string registrationId,
      string shimSessionNonce,
      out EntrypointRegistration registration)
  {
    if (_registrations.TryGetValue(registrationId, out registration!)
        && IsOwnedByShimSession(registration, shimSessionNonce))
    {
      return true;
    }

    registration = null!;
    return false;
  }

  private static bool IsOwnedByShimSession(
      EntrypointRegistration registration,
      string shimSessionNonce)
  {
    return string.Equals(
        registration.OwnerProcess.ShimSessionNonce,
        shimSessionNonce,
        StringComparison.Ordinal)
        && string.Equals(
            registration.EntrypointInstance.ShimSessionNonce,
            shimSessionNonce,
            StringComparison.Ordinal);
  }

  private static bool IsOwnedByProcess(
      EntrypointRegistration registration,
      OwnerProcessIdentity owner)
  {
    return registration.OwnerProcess.ProcessId == owner.ProcessId
        && registration.OwnerProcess.ProcessStartTimeUtc == owner.ProcessStartTimeUtc
        && string.Equals(
            registration.OwnerProcess.ShimSessionNonce,
            owner.ShimSessionNonce,
            StringComparison.Ordinal);
  }
}
