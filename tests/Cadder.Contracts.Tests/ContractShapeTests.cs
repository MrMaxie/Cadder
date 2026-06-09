using System.Text.Json;
using Cadder.Contracts;

namespace Cadder.Contracts.Tests;

public sealed class ContractShapeTests
{
  [Fact]
  public void EntrypointRegistrationContainsRequiredFieldsAndSerializes()
  {
    var registration = Samples.Registration();

    Assert.Equal("registration-1", registration.RegistrationId);
    Assert.Equal("Caddyfile", registration.SourceConfigPath.Raw);
    Assert.Equal("C:\\work\\site", registration.SourceWorkingDirectory.Canonical);
    Assert.Single(registration.RegisteredDomains);
    Assert.Equal(ActivationState.Active, registration.RegisteredDomains[0].ActivationState);
    Assert.Equal("domain-example.com", registration.RegisteredDomains[0].LogStream.StreamId);
    Assert.Equal(DateTimeOffset.Parse("2026-06-09T10:00:01Z"), registration.CreatedAtUtc);
    Assert.Equal(DateTimeOffset.Parse("2026-06-09T10:00:02Z"), registration.LastHeartbeatUtc);

    var json = JsonSerializer.Serialize(registration, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    Assert.Contains("sourceWorkingDirectory", json, StringComparison.Ordinal);
    Assert.Contains("sourceConfigPath", json, StringComparison.Ordinal);
    Assert.Contains("entrypointInstance", json, StringComparison.Ordinal);
    Assert.Contains("registeredDomains", json, StringComparison.Ordinal);
    Assert.Contains("activationState", json, StringComparison.Ordinal);
    Assert.Contains("ownerProcess", json, StringComparison.Ordinal);
    Assert.Contains("logStream", json, StringComparison.Ordinal);
    Assert.Contains("shimRun", json, StringComparison.Ordinal);
    Assert.Contains("createdAtUtc", json, StringComparison.Ordinal);
    Assert.Contains("lastHeartbeatUtc", json, StringComparison.Ordinal);
  }

  [Fact]
  public void GuiStateSnapshotSerializesCaddyConfigState()
  {
    var snapshot = new GuiStateSnapshot(
        DateTimeOffset.Parse("2026-06-09T10:00:03Z"),
        [Samples.Registration()],
        new RealCaddyRuntimeState(
            RealCaddyRuntimeStatus.Running,
            new RealCaddyBinaryIdentity("C:\\tools\\caddy-real.exe", "file-id-1"),
            "2.8.4",
            new RealCaddyProcessIdentity(8675, DateTimeOffset.Parse("2026-06-09T10:00:01Z"), true),
            "http://127.0.0.1:2019",
            [new CaddyRuntimeDiagnostic("runtime-healthy", "Runtime process is running.", "inspect")]),
        new CaddyConfigState(
            CaddyConfigApplyStatus.Failed,
            DateTimeOffset.Parse("2026-06-09T10:00:04Z"),
            DateTimeOffset.Parse("2026-06-09T10:00:02Z"),
            "hash-1",
            [
                new CaddyConfigDiagnostic(
                    "domain-conflict",
                    "Domain is registered by multiple entrypoint instances.",
                    "example.com",
                    ["C:\\work\\site\\Caddyfile", "C:\\work\\other\\Caddyfile"])
            ]));

    var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    Assert.Contains("caddyConfig", json, StringComparison.Ordinal);
    Assert.Contains("adminEndpoint", json, StringComparison.Ordinal);
    Assert.Contains("ownedByCadder", json, StringComparison.Ordinal);
    Assert.Contains("runtime-healthy", json, StringComparison.Ordinal);
    Assert.Contains("domain-conflict", json, StringComparison.Ordinal);
    Assert.Contains("effectiveConfigHash", json, StringComparison.Ordinal);
  }

  [Fact]
  public void OwnerProcessIdentityIsNotPidOnly()
  {
    var owner = Samples.Registration().OwnerProcess;

    Assert.Equal(4242, owner.ProcessId);
    Assert.NotEqual(default, owner.ProcessStartTimeUtc);
    Assert.Equal("nonce-1", owner.ShimSessionNonce);
  }

  [Fact]
  public void SourceAndDomainValuesPreserveRawAndCanonicalForms()
  {
    var registration = Samples.Registration();
    var domain = registration.RegisteredDomains[0].Name;

    Assert.Equal(".\\site", registration.SourceWorkingDirectory.Raw);
    Assert.Equal("C:\\work\\site", registration.SourceWorkingDirectory.Canonical);
    Assert.Equal("Example.COM", domain.Raw);
    Assert.Equal("example.com", domain.Canonical);
  }

  [Fact]
  public void ShimRunMetadataPreservesAdapterAndRawArguments()
  {
    var shimRun = Samples.Registration().ShimRun;

    Assert.NotNull(shimRun);
    Assert.Equal("caddyfile", shimRun.Adapter);
    Assert.Equal(["run", "--config", "Caddyfile", "--adapter", "caddyfile"], shimRun.RawArguments);
    Assert.Equal("run --config Caddyfile --adapter caddyfile", shimRun.CommandLine);
  }

  [Fact]
  public void IpcContractsCoverTaskRegistrationApi()
  {
    Assert.Equal("update-entrypoint-request", CadderIpcMessageTypes.UpdateEntrypointRequest);
    Assert.Equal("list-entrypoints-request", CadderIpcMessageTypes.ListEntrypointsRequest);
    Assert.Equal("toggle-entrypoint-request", CadderIpcMessageTypes.ToggleEntrypointRequest);
    Assert.Equal("heartbeat-entrypoint-request", CadderIpcMessageTypes.HeartbeatEntrypointRequest);
    Assert.Equal("subscribe-gui-state-request", CadderIpcMessageTypes.SubscribeGuiStateRequest);
    Assert.Equal("gui-state-changed-event", CadderIpcMessageTypes.GuiStateChangedEvent);

    var update = new UpdateEntrypointRequest(
        "request-1",
        "registration-1",
        "nonce-1",
        null,
        new SourcePath("Caddyfile.alt", "C:\\work\\site\\Caddyfile.alt"),
        [],
        ActivationState.Inactive,
        null);
    var json = JsonSerializer.Serialize(update, CadderIpcJson.SerializerOptions);

    Assert.Contains("registrationId", json, StringComparison.Ordinal);
    Assert.Contains("shimSessionNonce", json, StringComparison.Ordinal);
    Assert.Contains("sourceConfigPath", json, StringComparison.Ordinal);
    Assert.Contains("activationState", json, StringComparison.Ordinal);
  }

  private static class Samples
  {
    public static EntrypointRegistration Registration()
    {
      var logStream = new LogStreamIdentity("domain-example.com", "example.com", "stdout");

      return new EntrypointRegistration(
          "registration-1",
          new EntrypointInstanceIdentity("entrypoint-1", DateTimeOffset.Parse("2026-06-09T10:00:00Z"), "nonce-1"),
          new SourcePath(".\\site", "C:\\work\\site"),
          new SourcePath("Caddyfile", "C:\\work\\site\\Caddyfile"),
          [
              new RegisteredDomain(
                        new DomainName("Example.COM", "example.com"),
                        ActivationState.Active,
                        logStream)
          ],
          ActivationState.Active,
          new OwnerProcessIdentity(4242, DateTimeOffset.Parse("2026-06-09T09:59:59Z"), "nonce-1", "C:\\tools\\caddy.exe"),
          logStream,
          new ShimRunMetadata(
              "caddyfile",
              ["run", "--config", "Caddyfile", "--adapter", "caddyfile"],
              "run --config Caddyfile --adapter caddyfile"),
          DateTimeOffset.Parse("2026-06-09T10:00:01Z"),
          DateTimeOffset.Parse("2026-06-09T10:00:02Z"));
    }
  }
}
