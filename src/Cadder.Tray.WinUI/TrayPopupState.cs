using System.Globalization;
using Cadder.Contracts;

namespace Cadder.Tray.WinUI;

internal sealed record TrayPopupState(
    string DaemonStatus,
    string RuntimeStatus,
    string ConfigStatus,
    int EntrypointCount,
    int ActiveEntrypointCount,
    int ActiveDomainCount,
    string Diagnostic,
    IReadOnlyList<TrayPopupEntrypointGroup> Entrypoints);

internal sealed record TrayPopupEntrypointGroup(
    string RegistrationId,
    string ShimSessionNonce,
    string ProjectName,
    string SourcePath,
    ActivationState ActivationState,
    IReadOnlyList<TrayPopupDomainRow> Domains);

internal sealed record TrayPopupDomainRow(
    string RegistrationId,
    string ShimSessionNonce,
    string DomainKey,
    string DisplayName,
    ActivationState ActivationState,
    bool IsEnabled,
    string AutomationId);

internal sealed class TrayPopupStateBuilder
{
  public TrayPopupState Build(GuiStateSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    var groups = snapshot.Registrations
        .OrderBy(static registration => DisplayProjectName(registration), StringComparer.OrdinalIgnoreCase)
        .ThenBy(static registration => registration.RegistrationId, StringComparer.Ordinal)
        .Select(BuildGroup)
        .ToArray();
    var activeEntrypoints = groups.Count(static group => IsActive(group.ActivationState));
    var activeDomains = groups.Sum(static group => group.Domains.Count(static domain => domain.IsEnabled));
    var configStatus = snapshot.CaddyConfig?.Status.ToString() ?? CaddyConfigApplyStatus.NotApplied.ToString();

    return new TrayPopupState(
        BuildDaemonStatus(snapshot, activeEntrypoints, activeDomains),
        snapshot.RealCaddyRuntime.Status.ToString(),
        configStatus,
        groups.Length,
        activeEntrypoints,
        activeDomains,
        FirstDiagnostic(snapshot),
        groups);
  }

  private static TrayPopupEntrypointGroup BuildGroup(EntrypointRegistration registration)
  {
    return new TrayPopupEntrypointGroup(
        registration.RegistrationId,
        registration.EntrypointInstance.ShimSessionNonce,
        DisplayProjectName(registration),
        DisplaySourcePath(registration),
        registration.ActivationState,
        [.. registration.RegisteredDomains
            .OrderBy(static domain => DomainDisplayName(domain), StringComparer.OrdinalIgnoreCase)
            .Select(domain => BuildDomainRow(registration, domain))]);
  }

  private static TrayPopupDomainRow BuildDomainRow(
      EntrypointRegistration registration,
      RegisteredDomain domain)
  {
    var domainKey = domain.Name.Canonical ?? domain.Name.Raw;

    return new TrayPopupDomainRow(
        registration.RegistrationId,
        registration.EntrypointInstance.ShimSessionNonce,
        domainKey,
        DomainDisplayName(domain),
        domain.ActivationState,
        IsActive(domain.ActivationState),
        "TrayPopupDomainToggle" + SanitizeAutomationPart(domainKey));
  }

  private static string BuildDaemonStatus(
      GuiStateSnapshot snapshot,
      int activeEntrypoints,
      int activeDomains)
  {
    if (snapshot.CaddyConfig?.Status == CaddyConfigApplyStatus.Failed
        || snapshot.RealCaddyRuntime.Status == RealCaddyRuntimeStatus.Unhealthy)
    {
      return "Needs attention";
    }

    if (activeEntrypoints == 0 || activeDomains == 0)
    {
      return "Idle";
    }

    return "Serving";
  }

  private static string FirstDiagnostic(GuiStateSnapshot snapshot)
  {
    return snapshot.RealCaddyRuntime.Diagnostics?.FirstOrDefault()?.Message
        ?? snapshot.CaddyConfig?.Diagnostics.FirstOrDefault()?.Message
        ?? "-";
  }

  private static string DisplayProjectName(EntrypointRegistration registration)
  {
    var sourceDirectory = registration.SourceWorkingDirectory.Canonical
        ?? registration.SourceWorkingDirectory.Raw;
    var directoryName = LastPathSegment(sourceDirectory);
    if (!string.IsNullOrWhiteSpace(directoryName))
    {
      return directoryName;
    }

    var configName = Path.GetFileName(registration.SourceConfigPath.Canonical ?? registration.SourceConfigPath.Raw);
    return string.IsNullOrWhiteSpace(configName)
        ? registration.RegistrationId
        : configName;
  }

  private static string DisplaySourcePath(EntrypointRegistration registration)
  {
    var sourceDirectory = registration.SourceWorkingDirectory.Canonical
        ?? registration.SourceWorkingDirectory.Raw;
    if (!string.IsNullOrWhiteSpace(sourceDirectory))
    {
      return sourceDirectory;
    }

    return registration.SourceConfigPath.Canonical
        ?? registration.SourceConfigPath.Raw;
  }

  private static string LastPathSegment(string path)
  {
    var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return Path.GetFileName(trimmed);
  }

  private static string DomainDisplayName(RegisteredDomain domain)
  {
    return domain.Name.Canonical ?? domain.Name.Raw;
  }

  private static bool IsActive(ActivationState state)
  {
    return state is ActivationState.Registered or ActivationState.Activating or ActivationState.Active;
  }

  private static string SanitizeAutomationPart(string value)
  {
    var chars = value
        .Where(char.IsLetterOrDigit)
        .Take(64)
        .ToArray();

    return chars.Length == 0
        ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        : new string(chars);
  }
}
