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

    var json = JsonSerializer.Serialize(registration, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    Assert.Contains("sourceWorkingDirectory", json, StringComparison.Ordinal);
    Assert.Contains("sourceConfigPath", json, StringComparison.Ordinal);
    Assert.Contains("entrypointInstance", json, StringComparison.Ordinal);
    Assert.Contains("registeredDomains", json, StringComparison.Ordinal);
    Assert.Contains("activationState", json, StringComparison.Ordinal);
    Assert.Contains("ownerProcess", json, StringComparison.Ordinal);
    Assert.Contains("logStream", json, StringComparison.Ordinal);
    Assert.Contains("shimRun", json, StringComparison.Ordinal);
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
          new ShimRunMetadata("caddyfile", ["run", "--config", "Caddyfile", "--adapter", "caddyfile"]));
    }
  }
}
