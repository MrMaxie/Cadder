using Cadder.Contracts;
using Cadder.Tray.WinUI;

namespace Cadder.Tray.WinUI.Tests;

public sealed class PanelStateStoreTests
{
  [Fact]
  public void SetReady_WithOnlyRefreshMetadataChanged_UpdatesStateWithoutNotifying()
  {
    var store = new PanelStateStore();
    var changeCount = 0;
    store.Changed += (_, _) => changeCount++;
    var firstCapturedAt = DateTimeOffset.Parse("2026-06-09T12:00:00Z");
    var secondCapturedAt = DateTimeOffset.Parse("2026-06-09T12:00:05Z");

    store.SetReady(CreateState(firstCapturedAt, "Registered just now", "Heartbeat just now"));
    store.SetReady(CreateState(secondCapturedAt, "Registered 5s ago", "Heartbeat 5s ago"));

    Assert.Equal(1, changeCount);
    Assert.Equal(secondCapturedAt, store.CurrentState.CapturedAtUtc);
  }

  [Fact]
  public void SetReady_WithMaterialStateChanged_Notifies()
  {
    var store = new PanelStateStore();
    var changeCount = 0;
    store.Changed += (_, _) => changeCount++;

    store.SetReady(CreateState(
        DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
        "Registered 14s ago",
        "Heartbeat 14s ago"));
    store.SetReady(CreateState(
        DateTimeOffset.Parse("2026-06-09T12:00:01Z"),
        "Registered 15s ago",
        "Heartbeat 15s ago",
        processStatus: "Stale owner",
        isStaleOwner: true));

    Assert.Equal(2, changeCount);
  }

  [Fact]
  public void SetFilter_WithNewFilter_Notifies()
  {
    var store = new PanelStateStore();
    var changeCount = 0;
    store.Changed += (_, _) => changeCount++;

    store.SetFilter(" api.example.localhost ");
    store.SetFilter("api.example.localhost");

    Assert.Equal(1, changeCount);
    Assert.Equal("api.example.localhost", store.FilterText);
  }

  private static PanelState CreateState(
      DateTimeOffset capturedAtUtc,
      string ageLabel,
      string heartbeatLabel,
      string processStatus = "PID 1234",
      bool isStaleOwner = false)
  {
    var diagnostics = Array.Empty<PanelDiagnosticRow>();
    var instance = new PanelInstanceRow(
        "registration-1",
        "nonce-1",
        "Site",
        "D:\\Projects\\Site",
        "D:\\Projects\\Site\\Caddyfile",
        processStatus,
        "D:\\Tools\\caddy.exe",
        ageLabel,
        heartbeatLabel,
        1,
        1,
        "Active · 1/1 domains enabled",
        ActivationState.Active,
        isStaleOwner,
        diagnostics,
        "PanelInstanceCardregistration1",
        "registration-1 site d:\\projects\\site");
    var domain = new PanelDomainRow(
        "registration-1",
        "nonce-1",
        "api.example.localhost",
        "api.example.localhost",
        "Not detected",
        "Enabled",
        "No conflict",
        "-",
        ActivationState.Active,
        true,
        false,
        "PanelDomainRowregistration1apiexamplelocalhost",
        "api.example.localhost site d:\\projects\\site");
    var group = new PanelDomainGroup(
        "registration-1",
        "Site",
        "D:\\Projects\\Site",
        "D:\\Projects\\Site\\Caddyfile",
        [domain],
        "PanelDomainGroupregistration1",
        "registration-1 site api.example.localhost");
    var summary = new PanelSummary(
        "Serving",
        "Resolved runtime · Applied config · 1 active instances · 1 active domains",
        PanelStatusKind.Success,
        "Resolved",
        "Applied",
        1,
        1,
        1,
        1,
        "-");

    return new PanelState(
        PanelConnectionState.Ready,
        capturedAtUtc,
        "Connected to the local Cadder daemon.",
        summary,
        [instance],
        [group],
        diagnostics,
        [
            new PanelSearchRecord(
                "api.example.localhost",
                "Site · D:\\Projects\\Site",
                "Domains",
                "api.example.localhost",
                "api.example.localhost site d:\\projects\\site",
                "PanelSearchDomainregistration1apiexamplelocalhost")
        ]);
  }
}
