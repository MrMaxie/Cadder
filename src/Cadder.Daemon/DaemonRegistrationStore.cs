using System.Collections.Concurrent;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class InMemoryRegistrationStore : IRegistrationStore, ITransientRegistrationStore
{
  private readonly ConcurrentDictionary<string, EntrypointRegistration> _registrations = new(StringComparer.Ordinal);

  public ValueTask UpsertAsync(EntrypointRegistration registration, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registration);
    cancellationToken.ThrowIfCancellationRequested();

    _registrations[registration.RegistrationId] = registration;
    return ValueTask.CompletedTask;
  }

  public ValueTask<bool> RemoveAsync(
      string registrationId,
      string? shimSessionNonce = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    cancellationToken.ThrowIfCancellationRequested();

    if (!_registrations.TryGetValue(registrationId, out var registration))
    {
      return ValueTask.FromResult(false);
    }

    if (shimSessionNonce is not null
        && !string.Equals(
            registration.EntrypointInstance.ShimSessionNonce,
            shimSessionNonce,
            StringComparison.Ordinal))
    {
      return ValueTask.FromResult(false);
    }

    return ValueTask.FromResult(_registrations.TryRemove(registrationId, out _));
  }

  public ValueTask<EntrypointRegistration?> FindAsync(
      string registrationId,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
    cancellationToken.ThrowIfCancellationRequested();

    _registrations.TryGetValue(registrationId, out var registration);
    return ValueTask.FromResult(registration);
  }

  public ValueTask<IReadOnlyList<EntrypointRegistration>> ListAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    IReadOnlyList<EntrypointRegistration> registrations = [.. _registrations.Values];
    return ValueTask.FromResult(registrations);
  }

  public ValueTask ClearTransientRegistrationsAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    _registrations.Clear();
    return ValueTask.CompletedTask;
  }
}
