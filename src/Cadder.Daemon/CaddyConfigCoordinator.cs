using System.Security.Cryptography;
using System.Text;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class CaddyConfigCoordinator : ICaddyConfigCoordinator
{
  private readonly ICaddyfileConfigAdapter _adapter;
  private readonly CaddyJsonConfigInspector _inspector;
  private readonly CaddyJsonConfigComposer _composer;
  private readonly IRealCaddyRuntimeAdapter _runtime;
  private readonly TimeProvider _timeProvider;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private CaddyConfigState _currentState = new(
      CaddyConfigApplyStatus.NotApplied,
      null,
      null,
      null,
      []);
  private string? _lastKnownGoodConfig;

  public CaddyConfigCoordinator(
      IRealCaddyRuntimeAdapter runtime,
      ICaddyfileConfigAdapter? adapter = null,
      CaddyJsonConfigInspector? inspector = null,
      CaddyJsonConfigComposer? composer = null,
      TimeProvider? timeProvider = null)
  {
    _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    _adapter = adapter ?? new ProcessCaddyfileConfigAdapter();
    _inspector = inspector ?? new CaddyJsonConfigInspector();
    _composer = composer ?? new CaddyJsonConfigComposer(_inspector);
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  public CaddyConfigState CurrentState => _currentState;

  public async ValueTask<EntrypointRegistration> PrepareRegistrationAsync(
      EntrypointRegistration registration,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registration);

    var adaptResult = await _adapter.AdaptAsync(registration.SourceConfigPath, cancellationToken)
        .ConfigureAwait(false);
    if (!adaptResult.Succeeded || adaptResult.Config is null)
    {
      return registration;
    }

    return registration with
    {
      RegisteredDomains = CreateRegisteredDomains(adaptResult.Config, registration.RegisteredDomains)
    };
  }

  public async ValueTask<EntrypointRegistrationPatch> PreparePatchAsync(
      EntrypointRegistrationPatch patch,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(patch);

    if (patch.SourceConfigPath is null)
    {
      return patch;
    }

    var adaptResult = await _adapter.AdaptAsync(patch.SourceConfigPath, cancellationToken)
        .ConfigureAwait(false);
    if (!adaptResult.Succeeded || adaptResult.Config is null)
    {
      return patch;
    }

    return patch with
    {
      RegisteredDomains = CreateRegisteredDomains(adaptResult.Config, patch.RegisteredDomains ?? [])
    };
  }

  public async ValueTask<CaddyConfigState> ApplyAsync(
      IReadOnlyList<EntrypointRegistration> registrations,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(registrations);

    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var attemptedAtUtc = _timeProvider.GetUtcNow();
      var adaptResults = new Dictionary<string, CaddyfileAdaptResult>(StringComparer.Ordinal);
      foreach (var registration in registrations)
      {
        if (registration.ActivationState is ActivationState.Inactive or ActivationState.Faulted)
        {
          continue;
        }

        adaptResults[registration.RegistrationId] = await _adapter
            .AdaptAsync(registration.SourceConfigPath, cancellationToken)
            .ConfigureAwait(false);
      }

      var composition = _composer.Compose(registrations, adaptResults);
      if (!composition.Succeeded)
      {
        _currentState = Failed(attemptedAtUtc, composition.Diagnostics);
        return _currentState;
      }

      var config = new CaddyRuntimeConfig(composition.Content);
      var validation = await _runtime.ValidateConfigAsync(config, cancellationToken).ConfigureAwait(false);
      if (!validation.Succeeded)
      {
        _currentState = Failed(attemptedAtUtc, NormalizeDiagnostics(
            "config-validation-failed",
            validation.Message ?? "Composed Caddy config validation failed.",
            validation.Diagnostics));
        return _currentState;
      }

      var reload = await _runtime.ReloadConfigAsync(config, cancellationToken).ConfigureAwait(false);
      if (!reload.Succeeded)
      {
        _currentState = Failed(attemptedAtUtc, NormalizeDiagnostics(
            "config-reload-failed",
            reload.Message ?? "Real Caddy runtime rejected the composed config reload.",
            reload.Diagnostics));
        return _currentState;
      }

      _lastKnownGoodConfig = composition.Content;
      _currentState = new CaddyConfigState(
          CaddyConfigApplyStatus.Applied,
          attemptedAtUtc,
          attemptedAtUtc,
          ComputeHash(composition.Content),
          []);
      return _currentState;
    }
    finally
    {
      _gate.Release();
    }
  }

  private CaddyConfigState Failed(DateTimeOffset attemptedAtUtc, CaddyConfigDiagnostic[] diagnostics)
  {
    return new CaddyConfigState(
        CaddyConfigApplyStatus.Failed,
        attemptedAtUtc,
        _currentState.LastSuccessfulReloadAtUtc,
        _lastKnownGoodConfig is null ? _currentState.EffectiveConfigHash : ComputeHash(_lastKnownGoodConfig),
        diagnostics);
  }

  private RegisteredDomain[] CreateRegisteredDomains(
      System.Text.Json.Nodes.JsonObject config,
      RegisteredDomain[] existingDomains)
  {
    var activationByDomain = existingDomains
        .GroupBy(static domain => domain.Name.Canonical ?? domain.Name.Raw.ToLowerInvariant(), StringComparer.Ordinal)
        .ToDictionary(
            static group => group.Key,
            static group => group.First().ActivationState,
            StringComparer.Ordinal);

    return [.. _inspector.ExtractHosts(config)
        .GroupBy(static address => address.Canonical, StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .Select(group =>
        {
          var address = group.First();
          var activationState = activationByDomain.GetValueOrDefault(address.Canonical, ActivationState.Active);
          return new RegisteredDomain(
              new DomainName(address.Raw, address.Canonical),
              activationState,
              new LogStreamIdentity($"domain-{address.Canonical}", address.Canonical, "caddy"));
        })];
  }

  private static CaddyConfigDiagnostic[] NormalizeDiagnostics(
      string code,
      string message,
      CaddyConfigDiagnostic[] diagnostics)
  {
    return diagnostics.Length > 0
        ? diagnostics
        : [new CaddyConfigDiagnostic(code, message, null, [])];
  }

  private static string ComputeHash(string content)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
