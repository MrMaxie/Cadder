using System.Xml.Linq;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class DaemonBoundaryTests
{
  [Fact]
  public void GuiStateProjectorCreatesTrayReadModel()
  {
    var registration = Samples.Registration();
    var runtime = new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Resolved,
        new RealCaddyBinaryIdentity("C:\\caddy\\caddy.exe", "file-id-1"),
        "2.8.4");
    var snapshot = new DaemonStateSnapshot(
        DateTimeOffset.Parse("2026-06-09T11:00:00Z"),
        [registration],
        runtime);

    var projected = new GuiStateProjector().Project(snapshot);

    Assert.Equal(snapshot.CapturedAtUtc, projected.CapturedAtUtc);
    Assert.Single(projected.Registrations);
    Assert.Equal("registration-1", projected.Registrations[0].RegistrationId);
    Assert.Equal(RealCaddyRuntimeStatus.Resolved, projected.RealCaddyRuntime.Status);
  }

  [Fact]
  public void ProcessBoundaryRoleNamesAreExplicit()
  {
    Assert.Equal("tray-daemon-singleton", CadderRoles.TrayDaemonSingleton);
    Assert.Equal("caddy-shim-entrypoint", CadderRoles.CaddyShimEntrypoint);
    Assert.Equal("real-caddy-runtime-adapter", CadderRoles.RealCaddyRuntimeAdapter);
    Assert.Equal("ipc-contract", CadderRoles.IpcContract);
    Assert.Equal("registration-store", CadderRoles.RegistrationStore);
    Assert.Equal("gui-state-projection", CadderRoles.GuiStateProjection);
  }

  [Fact]
  public void ShimProjectBuildsAsCaddyExecutable()
  {
    var projectPath = Path.Combine(FindRepositoryRoot(), "src", "Cadder.CaddyShim", "Cadder.CaddyShim.csproj");
    var project = XDocument.Load(projectPath);
    var properties = project.Root?.Elements("PropertyGroup").Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);

    Assert.NotNull(properties);
    Assert.Equal("caddy", properties["AssemblyName"]);
    Assert.Equal("Cadder.CaddyShim", properties["RootNamespace"]);
    Assert.Equal("Exe", properties["OutputType"]);
    Assert.Equal("true", properties["UseAppHost"]);
  }

  private static string FindRepositoryRoot()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "Cadder.slnx")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Repository root could not be found from the test output path.");
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
          logStream);
    }
  }
}
