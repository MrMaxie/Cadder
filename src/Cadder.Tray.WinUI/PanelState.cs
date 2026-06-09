using System.Globalization;
using Cadder.Contracts;

namespace Cadder.Tray.WinUI;

public enum PanelConnectionState
{
  Loading = 0,
  Ready = 1,
  Disconnected = 2
}

public enum PanelStatusKind
{
  Neutral = 0,
  Success = 1,
  Warning = 2,
  Error = 3
}

public sealed record PanelState(
    PanelConnectionState ConnectionState,
    DateTimeOffset CapturedAtUtc,
    string ConnectionMessage,
    PanelSummary Summary,
    IReadOnlyList<PanelInstanceRow> Instances,
    IReadOnlyList<PanelDomainGroup> DomainGroups,
    IReadOnlyList<PanelDiagnosticRow> Diagnostics,
    IReadOnlyList<PanelSearchRecord> SearchRecords)
{
  public static PanelState Loading(DateTimeOffset capturedAtUtc)
  {
    return Empty(PanelConnectionState.Loading, capturedAtUtc, "Loading daemon state.");
  }

  public static PanelState Disconnected(DateTimeOffset capturedAtUtc, string message)
  {
    return Empty(
        PanelConnectionState.Disconnected,
        capturedAtUtc,
        string.IsNullOrWhiteSpace(message) ? "Daemon state is unavailable." : message);
  }

  private static PanelState Empty(
      PanelConnectionState connectionState,
      DateTimeOffset capturedAtUtc,
      string connectionMessage)
  {
    return new PanelState(
        connectionState,
        capturedAtUtc,
        connectionMessage,
        new PanelSummary(
            connectionState == PanelConnectionState.Loading ? "Loading" : "Disconnected",
            connectionMessage,
            PanelStatusKind.Neutral,
            "Unknown",
            "NotApplied",
            0,
            0,
            0,
            0,
            "-"),
        [],
        [],
        [],
        PanelStateBuilder.CreateNavigationSearchRecords());
  }
}

public sealed record PanelSummary(
    string DaemonStatus,
    string DaemonDetail,
    PanelStatusKind StatusKind,
    string RuntimeStatus,
    string ConfigStatus,
    int EntrypointCount,
    int ActiveEntrypointCount,
    int DomainCount,
    int ActiveDomainCount,
    string FirstDiagnostic);

public sealed record PanelInstanceRow(
    string RegistrationId,
    string ShimSessionNonce,
    string ProjectName,
    string SourcePath,
    string ConfigPath,
    string ProcessStatus,
    string OwnerExecutablePath,
    string AgeLabel,
    string LastHeartbeatLabel,
    int DomainCount,
    int ActiveDomainCount,
    string ActivationSummary,
    ActivationState ActivationState,
    bool IsStaleOwner,
    IReadOnlyList<PanelDiagnosticRow> Diagnostics,
    string AutomationId,
    string SearchText);

public sealed record PanelDomainGroup(
    string RegistrationId,
    string ProjectName,
    string SourcePath,
    string ConfigPath,
    IReadOnlyList<PanelDomainRow> Domains,
    string AutomationId,
    string SearchText);

public sealed record PanelDomainRow(
    string RegistrationId,
    string ShimSessionNonce,
    string DomainKey,
    string Hostname,
    string UpstreamTarget,
    string EnabledState,
    string ConflictState,
    string LastError,
    ActivationState ActivationState,
    bool IsEnabled,
    bool IsConflicted,
    string AutomationId,
    string SearchText);

public sealed record PanelDiagnosticRow(
    PanelStatusKind Severity,
    string Scope,
    string Code,
    string Message,
    string? DomainKey,
    IReadOnlyList<string> SourceConfigPaths,
    string AutomationId);

public sealed record PanelSearchRecord(
    string Title,
    string Subtitle,
    string TargetPageTag,
    string FilterText,
    string SearchText,
    string AutomationId);

public sealed class PanelStateBuilder
{
  private static readonly TimeSpan s_defaultStaleOwnerThreshold = TimeSpan.FromSeconds(15);
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _staleOwnerThreshold;

  public PanelStateBuilder(TimeProvider? timeProvider = null, TimeSpan? staleOwnerThreshold = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
    _staleOwnerThreshold = staleOwnerThreshold ?? s_defaultStaleOwnerThreshold;
  }

  public PanelState Build(GuiStateSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    var now = _timeProvider.GetUtcNow();
    var diagnostics = BuildDiagnostics(snapshot);
    var instances = snapshot.Registrations
        .OrderBy(static registration => DisplayProjectName(registration), StringComparer.OrdinalIgnoreCase)
        .ThenBy(static registration => registration.RegistrationId, StringComparer.Ordinal)
        .Select(registration => BuildInstanceRow(registration, diagnostics, now))
        .ToArray();
    var domainGroups = snapshot.Registrations
        .OrderBy(static registration => DisplayProjectName(registration), StringComparer.OrdinalIgnoreCase)
        .ThenBy(static registration => registration.RegistrationId, StringComparer.Ordinal)
        .Select(registration => BuildDomainGroup(registration, diagnostics))
        .ToArray();
    var activeEntrypoints = instances.Count(static instance => IsActive(instance.ActivationState));
    var domainCount = domainGroups.Sum(static group => group.Domains.Count);
    var activeDomains = domainGroups.Sum(static group => group.Domains.Count(static domain => domain.IsEnabled));
    var summary = new PanelSummary(
        BuildDaemonStatus(snapshot, activeEntrypoints, activeDomains),
        BuildDaemonDetail(snapshot, activeEntrypoints, activeDomains),
        BuildSummaryKind(snapshot, activeEntrypoints, activeDomains),
        snapshot.RealCaddyRuntime.Status.ToString(),
        snapshot.CaddyConfig?.Status.ToString() ?? CaddyConfigApplyStatus.NotApplied.ToString(),
        instances.Length,
        activeEntrypoints,
        domainCount,
        activeDomains,
        FirstDiagnostic(snapshot));

    return new PanelState(
        PanelConnectionState.Ready,
        snapshot.CapturedAtUtc,
        "Connected to the local Cadder daemon.",
        summary,
        instances,
        domainGroups,
        diagnostics,
        BuildSearchRecords(instances, domainGroups));
  }

  public static IReadOnlyList<PanelSearchRecord> CreateNavigationSearchRecords()
  {
    return
    [
        NavigationSearchRecord("Overview", "Daemon, runtime, config, and health summary.", "Overview"),
        NavigationSearchRecord("Instances", "Entrypoint process cards and owner state.", "Instances"),
        NavigationSearchRecord("Domains", "Grouped domain activation, conflicts, and errors.", "Domains"),
        NavigationSearchRecord("Logs", "Domain log surface status.", "Logs"),
        NavigationSearchRecord("Settings", "Panel settings and task scope.", "Settings"),
        NavigationSearchRecord("Diagnostics", "Runtime and config diagnostics.", "Diagnostics")
    ];
  }

  private PanelInstanceRow BuildInstanceRow(
      EntrypointRegistration registration,
      IReadOnlyList<PanelDiagnosticRow> diagnostics,
      DateTimeOffset now)
  {
    var sourcePath = DisplaySourcePath(registration);
    var configPath = DisplayConfigPath(registration);
    var activeDomains = registration.RegisteredDomains.Count(static domain => IsActive(domain.ActivationState));
    var isStaleOwner = IsStaleOwner(registration, now);
    var instanceDiagnostics = diagnostics
        .Where(diagnostic => DiagnosticAppliesToRegistration(diagnostic, registration))
        .ToArray();
    var processStatus = isStaleOwner
        ? "Stale owner"
        : $"PID {registration.OwnerProcess.ProcessId.ToString(CultureInfo.InvariantCulture)}";
    var activationSummary = $"{registration.ActivationState} · {activeDomains.ToString(CultureInfo.InvariantCulture)}/{registration.RegisteredDomains.Length.ToString(CultureInfo.InvariantCulture)} domains enabled";

    return new PanelInstanceRow(
        registration.RegistrationId,
        registration.EntrypointInstance.ShimSessionNonce,
        DisplayProjectName(registration),
        sourcePath,
        configPath,
        processStatus,
        string.IsNullOrWhiteSpace(registration.OwnerProcess.ExecutablePath) ? "-" : registration.OwnerProcess.ExecutablePath,
        $"Registered {FormatAge(registration.CreatedAtUtc, now)}",
        $"Heartbeat {FormatAge(registration.LastHeartbeatUtc, now)}",
        registration.RegisteredDomains.Length,
        activeDomains,
        activationSummary,
        registration.ActivationState,
        isStaleOwner,
        instanceDiagnostics,
        "PanelInstanceCard" + SanitizeAutomationPart(registration.RegistrationId),
        JoinSearchText(
            registration.RegistrationId,
            registration.EntrypointInstance.ShimSessionNonce,
            DisplayProjectName(registration),
            sourcePath,
            configPath,
            registration.OwnerProcess.ExecutablePath,
            string.Join(' ', registration.RegisteredDomains.Select(static domain => DomainDisplayName(domain)))));
  }

  private static PanelDomainGroup BuildDomainGroup(
      EntrypointRegistration registration,
      IReadOnlyList<PanelDiagnosticRow> diagnostics)
  {
    var domains = registration.RegisteredDomains
        .OrderBy(static domain => DomainDisplayName(domain), StringComparer.OrdinalIgnoreCase)
        .Select(domain => BuildDomainRow(registration, domain, diagnostics))
        .ToArray();
    var projectName = DisplayProjectName(registration);
    var sourcePath = DisplaySourcePath(registration);
    var configPath = DisplayConfigPath(registration);

    return new PanelDomainGroup(
        registration.RegistrationId,
        projectName,
        sourcePath,
        configPath,
        domains,
        "PanelDomainGroup" + SanitizeAutomationPart(registration.RegistrationId),
        JoinSearchText(
            registration.RegistrationId,
            projectName,
            sourcePath,
            configPath,
            string.Join(' ', domains.Select(static domain => domain.Hostname))));
  }

  private static PanelDomainRow BuildDomainRow(
      EntrypointRegistration registration,
      RegisteredDomain domain,
      IReadOnlyList<PanelDiagnosticRow> diagnostics)
  {
    var domainKey = domain.Name.Canonical ?? domain.Name.Raw;
    var domainDiagnostics = diagnostics
        .Where(diagnostic => DiagnosticAppliesToDomain(diagnostic, domainKey))
        .ToArray();
    var isConflicted = domainDiagnostics.Any(static diagnostic =>
        diagnostic.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    var lastError = domainDiagnostics.FirstOrDefault(static diagnostic => diagnostic.Severity == PanelStatusKind.Error)?.Message
        ?? domainDiagnostics.FirstOrDefault()?.Message
        ?? "-";

    return new PanelDomainRow(
        registration.RegistrationId,
        registration.EntrypointInstance.ShimSessionNonce,
        domainKey,
        DomainDisplayName(domain),
        "Not detected",
        IsActive(domain.ActivationState) ? "Enabled" : "Disabled",
        isConflicted ? "Conflict" : "No conflict",
        lastError,
        domain.ActivationState,
        IsActive(domain.ActivationState),
        isConflicted,
        "PanelDomainRow" + SanitizeAutomationPart(registration.RegistrationId + domainKey),
        JoinSearchText(
            domainKey,
            DomainDisplayName(domain),
            DisplayProjectName(registration),
            DisplaySourcePath(registration),
            DisplayConfigPath(registration),
            domain.ActivationState.ToString()));
  }

  private static IReadOnlyList<PanelDiagnosticRow> BuildDiagnostics(GuiStateSnapshot snapshot)
  {
    var rows = new List<PanelDiagnosticRow>();

    foreach (var diagnostic in snapshot.RealCaddyRuntime.Diagnostics ?? [])
    {
      rows.Add(new PanelDiagnosticRow(
          PanelStatusKind.Error,
          "Runtime",
          diagnostic.Code,
          diagnostic.Message,
          null,
          [],
          "PanelRuntimeDiagnostic" + SanitizeAutomationPart(diagnostic.Code)));
    }

    if (snapshot.RealCaddyRuntime.Status == RealCaddyRuntimeStatus.Unhealthy
        && rows.Count == 0)
    {
      rows.Add(new PanelDiagnosticRow(
          PanelStatusKind.Error,
          "Runtime",
          "runtime-unhealthy",
          "Real Caddy runtime is unhealthy.",
          null,
          [],
          "PanelRuntimeDiagnosticUnhealthy"));
    }

    foreach (var diagnostic in snapshot.CaddyConfig?.Diagnostics ?? [])
    {
      rows.Add(new PanelDiagnosticRow(
          DiagnosticSeverity(diagnostic),
          diagnostic.DomainKey is null ? "Config" : "Domain",
          diagnostic.Code,
          diagnostic.Message,
          diagnostic.DomainKey,
          diagnostic.SourceConfigPaths,
          "PanelConfigDiagnostic" + SanitizeAutomationPart(diagnostic.Code + diagnostic.DomainKey)));
    }

    return rows;
  }

  private static IReadOnlyList<PanelSearchRecord> BuildSearchRecords(
      IReadOnlyList<PanelInstanceRow> instances,
      IReadOnlyList<PanelDomainGroup> domainGroups)
  {
    var records = new List<PanelSearchRecord>();
    records.AddRange(CreateNavigationSearchRecords());

    foreach (var instance in instances)
    {
      records.Add(new PanelSearchRecord(
          instance.ProjectName,
          $"{instance.SourcePath} · {instance.ConfigPath}",
          "Instances",
          instance.ProjectName,
          instance.SearchText,
          "PanelSearchInstance" + SanitizeAutomationPart(instance.RegistrationId)));
      records.Add(new PanelSearchRecord(
          instance.ConfigPath,
          $"{instance.ProjectName} config path",
          "Instances",
          instance.ConfigPath,
          instance.SearchText,
          "PanelSearchConfig" + SanitizeAutomationPart(instance.RegistrationId)));
    }

    foreach (var group in domainGroups)
    {
      foreach (var domain in group.Domains)
      {
        records.Add(new PanelSearchRecord(
            domain.Hostname,
            $"{group.ProjectName} · {group.SourcePath}",
            "Domains",
            domain.Hostname,
            domain.SearchText,
            "PanelSearchDomain" + SanitizeAutomationPart(group.RegistrationId + domain.DomainKey)));
      }
    }

    return records;
  }

  private bool IsStaleOwner(EntrypointRegistration registration, DateTimeOffset now)
  {
    if (registration.LastHeartbeatUtc == default)
    {
      return false;
    }

    return now - registration.LastHeartbeatUtc > _staleOwnerThreshold;
  }

  private static bool DiagnosticAppliesToRegistration(
      PanelDiagnosticRow diagnostic,
      EntrypointRegistration registration)
  {
    if (diagnostic.SourceConfigPaths.Count == 0)
    {
      return false;
    }

    var configPath = DisplayConfigPath(registration);
    return diagnostic.SourceConfigPaths.Any(path =>
        string.Equals(path, configPath, StringComparison.OrdinalIgnoreCase));
  }

  private static bool DiagnosticAppliesToDomain(PanelDiagnosticRow diagnostic, string domainKey)
  {
    return diagnostic.DomainKey is not null
        && string.Equals(diagnostic.DomainKey, domainKey, StringComparison.OrdinalIgnoreCase);
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

  private static string BuildDaemonDetail(
      GuiStateSnapshot snapshot,
      int activeEntrypoints,
      int activeDomains)
  {
    var configStatus = snapshot.CaddyConfig?.Status.ToString() ?? CaddyConfigApplyStatus.NotApplied.ToString();
    return $"{snapshot.RealCaddyRuntime.Status} runtime · {configStatus} config · {activeEntrypoints.ToString(CultureInfo.InvariantCulture)} active instances · {activeDomains.ToString(CultureInfo.InvariantCulture)} active domains";
  }

  private static PanelStatusKind BuildSummaryKind(
      GuiStateSnapshot snapshot,
      int activeEntrypoints,
      int activeDomains)
  {
    if (snapshot.CaddyConfig?.Status == CaddyConfigApplyStatus.Failed
        || snapshot.RealCaddyRuntime.Status == RealCaddyRuntimeStatus.Unhealthy)
    {
      return PanelStatusKind.Error;
    }

    if (activeEntrypoints == 0 || activeDomains == 0)
    {
      return PanelStatusKind.Neutral;
    }

    return PanelStatusKind.Success;
  }

  private static PanelStatusKind DiagnosticSeverity(CaddyConfigDiagnostic diagnostic)
  {
    return diagnostic.Code.Contains("conflict", StringComparison.OrdinalIgnoreCase)
        || diagnostic.Code.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || diagnostic.Code.Contains("missing", StringComparison.OrdinalIgnoreCase)
        ? PanelStatusKind.Error
        : PanelStatusKind.Warning;
  }

  private static string FirstDiagnostic(GuiStateSnapshot snapshot)
  {
    return snapshot.RealCaddyRuntime.Diagnostics?.FirstOrDefault()?.Message
        ?? snapshot.CaddyConfig?.Diagnostics.FirstOrDefault()?.Message
        ?? "-";
  }

  private static PanelSearchRecord NavigationSearchRecord(string title, string subtitle, string targetPageTag)
  {
    return new PanelSearchRecord(
        title,
        subtitle,
        targetPageTag,
        string.Empty,
        JoinSearchText(title, subtitle, targetPageTag),
        "PanelSearchNavigation" + targetPageTag);
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

    var configName = Path.GetFileName(DisplayConfigPath(registration));
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

    return DisplayConfigPath(registration);
  }

  private static string DisplayConfigPath(EntrypointRegistration registration)
  {
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

  private static string FormatAge(DateTimeOffset timestamp, DateTimeOffset now)
  {
    if (timestamp == default)
    {
      return "unknown";
    }

    var age = now - timestamp;
    if (age < TimeSpan.Zero)
    {
      age = TimeSpan.Zero;
    }

    if (age < TimeSpan.FromSeconds(2))
    {
      return "just now";
    }

    if (age < TimeSpan.FromMinutes(1))
    {
      return $"{Math.Floor(age.TotalSeconds).ToString(CultureInfo.InvariantCulture)}s ago";
    }

    if (age < TimeSpan.FromHours(1))
    {
      return $"{Math.Floor(age.TotalMinutes).ToString(CultureInfo.InvariantCulture)}m ago";
    }

    if (age < TimeSpan.FromDays(1))
    {
      return $"{Math.Floor(age.TotalHours).ToString(CultureInfo.InvariantCulture)}h ago";
    }

    return $"{Math.Floor(age.TotalDays).ToString(CultureInfo.InvariantCulture)}d ago";
  }

  private static string JoinSearchText(params string?[] values)
  {
    return string.Join(
        ' ',
        values.Where(static value => !string.IsNullOrWhiteSpace(value)))
        .ToLowerInvariant();
  }

  private static string SanitizeAutomationPart(string? value)
  {
    var chars = (value ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Take(64)
        .ToArray();

    return chars.Length == 0
        ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        : new string(chars);
  }
}
