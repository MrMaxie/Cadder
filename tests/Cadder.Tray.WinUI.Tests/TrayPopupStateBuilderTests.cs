using Cadder.Contracts;
using Cadder.Tray.WinUI;

namespace Cadder.Tray.WinUI.Tests;

public sealed class TrayPopupStateBuilderTests
{
  [Fact]
  public void Build_WithNoRegistrations_ReportsIdleCounts()
  {
    var snapshot = CreateSnapshot([]);
    var builder = new TrayPopupStateBuilder();

    var state = builder.Build(snapshot);

    Assert.Equal("Idle", state.DaemonStatus);
    Assert.Equal("Idle", state.RuntimeStatus);
    Assert.Equal(0, state.EntrypointCount);
    Assert.Equal(0, state.ActiveEntrypointCount);
    Assert.Equal(0, state.ActiveDomainCount);
    Assert.Empty(state.Entrypoints);
  }

  [Fact]
  public void Build_WithMultipleEntrypoints_GroupsDomainsAndProjects()
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
    var builder = new TrayPopupStateBuilder();

    var state = builder.Build(CreateSnapshot([second, first]));

    Assert.Equal("Serving", state.DaemonStatus);
    Assert.Equal(2, state.EntrypointCount);
    Assert.Equal(2, state.ActiveEntrypointCount);
    Assert.Equal(2, state.ActiveDomainCount);
    Assert.Equal(["First", "Second"], state.Entrypoints.Select(static group => group.ProjectName).ToArray());
    var firstGroup = state.Entrypoints[0];
    Assert.Equal("D:\\Projects\\First", firstGroup.SourcePath);
    Assert.Equal(["admin.example.localhost", "api.example.localhost"], firstGroup.Domains.Select(static domain => domain.DisplayName).ToArray());
    Assert.False(firstGroup.Domains[0].IsEnabled);
    Assert.True(firstGroup.Domains[1].IsEnabled);
  }

  [Fact]
  public void Build_WithFailedConfig_ReportsNeedsAttentionAndDiagnostic()
  {
    var snapshot = CreateSnapshot(
        [CreateRegistration("registration-1", "nonce-1", "D:\\Projects\\Site", [CreateDomain("site.example.localhost", ActivationState.Active)])],
        new CaddyConfigState(
            CaddyConfigApplyStatus.Failed,
            DateTimeOffset.Parse("2026-06-09T12:00:01Z"),
            null,
            null,
            [new CaddyConfigDiagnostic("domain-conflict", "Domain conflict.", "site.example.localhost", [])]));
    var builder = new TrayPopupStateBuilder();

    var state = builder.Build(snapshot);

    Assert.Equal("Needs attention", state.DaemonStatus);
    Assert.Equal("Domain conflict.", state.Diagnostic);
  }

  private static GuiStateSnapshot CreateSnapshot(
      EntrypointRegistration[] registrations,
      CaddyConfigState? config = null)
  {
    return new GuiStateSnapshot(
        DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
        registrations,
        new RealCaddyRuntimeState(RealCaddyRuntimeStatus.Idle, null, null, Diagnostics: []),
        config);
  }

  private static EntrypointRegistration CreateRegistration(
      string registrationId,
      string nonce,
      string sourceWorkingDirectory,
      RegisteredDomain[] domains)
  {
    var logStream = new LogStreamIdentity($"entrypoint-{registrationId}", null, "shim");

    return new EntrypointRegistration(
        registrationId,
        new EntrypointInstanceIdentity(registrationId, DateTimeOffset.Parse("2026-06-09T12:00:00Z"), nonce),
        new SourcePath(sourceWorkingDirectory, sourceWorkingDirectory),
        new SourcePath("Caddyfile", Path.Combine(sourceWorkingDirectory, "Caddyfile")),
        domains,
        ActivationState.Active,
        new OwnerProcessIdentity(1234, DateTimeOffset.Parse("2026-06-09T11:59:59Z"), nonce, "C:\\tools\\caddy.exe"),
        logStream);
  }

  private static RegisteredDomain CreateDomain(string name, ActivationState state)
  {
    var canonical = name.ToLowerInvariant();
    return new RegisteredDomain(
        new DomainName(name, canonical),
        state,
        new LogStreamIdentity($"domain-{canonical}", canonical, "caddy"));
  }
}
