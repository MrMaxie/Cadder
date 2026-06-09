using Cadder.Contracts;
using Cadder.Tray.WinUI;

namespace Cadder.Tray.WinUI.Tests;

public sealed class PanelStateBuilderTests
{
  private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

  [Fact]
  public void Build_WithNoRegistrations_ReportsInlineEmptyState()
  {
    var state = CreateBuilder().Build(CreateSnapshot([]));

    Assert.Equal(PanelConnectionState.Ready, state.ConnectionState);
    Assert.Equal("Idle", state.Summary.DaemonStatus);
    Assert.Equal(0, state.Summary.EntrypointCount);
    Assert.Equal(0, state.Summary.DomainCount);
    Assert.Empty(state.Instances);
    Assert.Empty(state.DomainGroups);
    Assert.Contains(state.SearchRecords, static record => record.TargetPageTag == "Overview");
    Assert.Contains(state.SearchRecords, static record => record.TargetPageTag == "Diagnostics");
  }

  [Fact]
  public void Build_WithMultipleInstances_ProjectsCardsAndDomains()
  {
    var first = CreateRegistration(
        "registration-1",
        "nonce-1",
        "D:\\Projects\\First",
        [
            CreateDomain("Api.Example.Localhost", ActivationState.Active),
            CreateDomain("admin.example.localhost", ActivationState.Inactive)
        ]);
    var second = CreateRegistration(
        "registration-2",
        "nonce-2",
        "D:\\Projects\\Second",
        [
            CreateDomain("site.example.localhost", ActivationState.Registered)
        ]);

    var state = CreateBuilder().Build(CreateSnapshot([second, first]));

    Assert.Equal("Serving", state.Summary.DaemonStatus);
    Assert.Equal(2, state.Summary.EntrypointCount);
    Assert.Equal(3, state.Summary.DomainCount);
    Assert.Equal(2, state.Summary.ActiveDomainCount);
    Assert.Equal(["First", "Second"], state.Instances.Select(static instance => instance.ProjectName).ToArray());
    Assert.Equal("Registered 30s ago", state.Instances[0].AgeLabel);
    Assert.Equal("Heartbeat 5s ago", state.Instances[0].LastHeartbeatLabel);
    Assert.Equal("Active · 1/2 domains enabled", state.Instances[0].ActivationSummary);
    Assert.Equal(["admin.example.localhost", "api.example.localhost"], state.DomainGroups[0].Domains.Select(static domain => domain.Hostname).ToArray());
    Assert.Equal("Not detected", state.DomainGroups[0].Domains[0].UpstreamTarget);
    Assert.Equal("Disabled", state.DomainGroups[0].Domains[0].EnabledState);
  }

  [Fact]
  public void Build_WithDomainConflict_MapsConflictAndLastErrorToDomain()
  {
    var registration = CreateRegistration(
        "registration-1",
        "nonce-1",
        "D:\\Projects\\Site",
        [CreateDomain("site.example.localhost", ActivationState.Active)]);
    var config = new CaddyConfigState(
        CaddyConfigApplyStatus.Failed,
        s_now,
        null,
        null,
        [new CaddyConfigDiagnostic(
            "domain-conflict",
            "Domain is registered by another instance.",
            "site.example.localhost",
            [registration.SourceConfigPath.Canonical!])]);

    var state = CreateBuilder().Build(CreateSnapshot([registration], config));

    var domain = Assert.Single(state.DomainGroups).Domains.Single();
    Assert.Equal("Needs attention", state.Summary.DaemonStatus);
    Assert.True(domain.IsConflicted);
    Assert.Equal("Conflict", domain.ConflictState);
    Assert.Equal("Domain is registered by another instance.", domain.LastError);
    Assert.Single(state.Instances.Single().Diagnostics);
  }

  [Fact]
  public void Build_WithRuntimeError_ProjectsDiagnostics()
  {
    var runtime = new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Unhealthy,
        null,
        null,
        Diagnostics:
        [
            new CaddyRuntimeDiagnostic(
                "runtime-start-failed",
                "Real Caddy runtime could not be started.",
                "ensure-running")
        ]);

    var state = CreateBuilder().Build(CreateSnapshot([], runtime: runtime));

    Assert.Equal("Needs attention", state.Summary.DaemonStatus);
    Assert.Equal(PanelStatusKind.Error, state.Summary.StatusKind);
    var diagnostic = Assert.Single(state.Diagnostics);
    Assert.Equal("Runtime", diagnostic.Scope);
    Assert.Equal("runtime-start-failed", diagnostic.Code);
  }

  [Fact]
  public void Build_WithOldHeartbeat_MarksStaleOwner()
  {
    var registration = CreateRegistration(
        "registration-1",
        "nonce-1",
        "D:\\Projects\\Site",
        [CreateDomain("site.example.localhost", ActivationState.Active)],
        lastHeartbeatUtc: s_now - TimeSpan.FromSeconds(45));

    var state = CreateBuilder().Build(CreateSnapshot([registration]));

    var instance = Assert.Single(state.Instances);
    Assert.True(instance.IsStaleOwner);
    Assert.Equal("Stale owner", instance.ProcessStatus);
  }

  [Fact]
  public void Build_SearchRecordsIncludeDomainsSourcePathsAndConfigPaths()
  {
    var registration = CreateRegistration(
        "registration-1",
        "nonce-1",
        "D:\\Projects\\Site",
        [CreateDomain("site.example.localhost", ActivationState.Active)]);

    var state = CreateBuilder().Build(CreateSnapshot([registration]));

    Assert.Contains(state.SearchRecords, record =>
        record.TargetPageTag == "Domains"
        && record.SearchText.Contains("site.example.localhost", StringComparison.Ordinal));
    Assert.Contains(state.SearchRecords, record =>
        record.TargetPageTag == "Instances"
        && record.SearchText.Contains("d:\\projects\\site", StringComparison.Ordinal));
    Assert.Contains(state.SearchRecords, record =>
        record.TargetPageTag == "Instances"
        && record.SearchText.Contains("d:\\projects\\site\\caddyfile", StringComparison.Ordinal));
  }

  private static PanelStateBuilder CreateBuilder()
  {
    return new PanelStateBuilder(new FixedTimeProvider(s_now));
  }

  private static GuiStateSnapshot CreateSnapshot(
      EntrypointRegistration[] registrations,
      CaddyConfigState? config = null,
      RealCaddyRuntimeState? runtime = null)
  {
    return new GuiStateSnapshot(
        s_now,
        registrations,
        runtime ?? new RealCaddyRuntimeState(RealCaddyRuntimeStatus.Running, null, null, Diagnostics: []),
        config);
  }

  private static EntrypointRegistration CreateRegistration(
      string registrationId,
      string nonce,
      string sourceWorkingDirectory,
      RegisteredDomain[] domains,
      DateTimeOffset? createdAtUtc = null,
      DateTimeOffset? lastHeartbeatUtc = null)
  {
    var logStream = new LogStreamIdentity($"entrypoint-{registrationId}", null, "shim");

    return new EntrypointRegistration(
        registrationId,
        new EntrypointInstanceIdentity(registrationId, s_now - TimeSpan.FromMinutes(1), nonce),
        new SourcePath(sourceWorkingDirectory, sourceWorkingDirectory),
        new SourcePath("Caddyfile", Path.Combine(sourceWorkingDirectory, "Caddyfile")),
        domains,
        ActivationState.Active,
        new OwnerProcessIdentity(1234, s_now - TimeSpan.FromMinutes(2), nonce, "C:\\tools\\caddy.exe"),
        logStream,
        CreatedAtUtc: createdAtUtc ?? s_now - TimeSpan.FromSeconds(30),
        LastHeartbeatUtc: lastHeartbeatUtc ?? s_now - TimeSpan.FromSeconds(5));
  }

  private static RegisteredDomain CreateDomain(string name, ActivationState state)
  {
    var canonical = name.ToLowerInvariant();
    return new RegisteredDomain(
        new DomainName(name, canonical),
        state,
        new LogStreamIdentity($"domain-{canonical}", canonical, "caddy"));
  }

  private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow()
    {
      return utcNow;
    }
  }
}
