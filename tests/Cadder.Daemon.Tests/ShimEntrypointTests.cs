using Cadder.CaddyShim;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class ShimEntrypointTests
{
  [Fact]
  public async Task RunAsync_RunCommand_RegistersAndUnregistersAroundShimLifetime()
  {
    var connection = new RecordingDaemonConnection();
    var output = new StringWriter();
    var error = new StringWriter();
    var dependencies = CreateDependencies(
        new SucceedingConnector(connection),
        new RecordingDaemonLauncher(),
        new CompletingLifetimeWaiter(),
        output,
        error);

    var exitCode = await ShimEntrypoint.RunAsync(["run"], dependencies);

    Assert.Equal(0, exitCode);
    Assert.NotNull(connection.RegisterRequest);
    Assert.NotNull(connection.UnregisterRequest);
    Assert.Equal(connection.RegisterRequest.Registration.RegistrationId, connection.UnregisterRequest.RegistrationId);
    Assert.Equal("nonce-1", connection.RegisterRequest.Registration.EntrypointInstance.ShimSessionNonce);
    Assert.Equal("nonce-1", connection.UnregisterRequest.ShimSessionNonce);
    Assert.Equal(string.Empty, error.ToString());
  }

  [Fact]
  public async Task RunAsync_WhenIpcUnavailable_StartsDaemonAndWaitsUntilConnectionSucceeds()
  {
    var connection = new RecordingDaemonConnection();
    var connector = new FlakyConnector(connection);
    var launcher = new RecordingDaemonLauncher();
    var dependencies = CreateDependencies(
        connector,
        launcher,
        new CompletingLifetimeWaiter(),
        new StringWriter(),
        new StringWriter());

    var exitCode = await ShimEntrypoint.RunAsync(["run"], dependencies);

    Assert.Equal(0, exitCode);
    Assert.True(launcher.Started);
    Assert.Equal(2, connector.Attempts);
    Assert.NotNull(connection.RegisterRequest);
  }

  [Fact]
  public async Task RunAsync_WhenUnregisterFailsAfterLifetime_ReturnsSuccess()
  {
    var connection = new RecordingDaemonConnection { ThrowOnUnregister = true };
    var error = new StringWriter();
    var dependencies = CreateDependencies(
        new SucceedingConnector(connection),
        new RecordingDaemonLauncher(),
        new CompletingLifetimeWaiter(),
        new StringWriter(),
        error);

    var exitCode = await ShimEntrypoint.RunAsync(["run"], dependencies);

    Assert.Equal(0, exitCode);
    Assert.Contains("unregister failed after session end", error.ToString(), StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_UnsupportedCommand_ReturnsClearError()
  {
    var error = new StringWriter();
    var dependencies = CreateDependencies(
        new SucceedingConnector(new RecordingDaemonConnection()),
        new RecordingDaemonLauncher(),
        new CompletingLifetimeWaiter(),
        new StringWriter(),
        error);

    var exitCode = await ShimEntrypoint.RunAsync(["version"], dependencies);

    Assert.Equal(2, exitCode);
    Assert.Contains("Unsupported caddy command 'version'", error.ToString(), StringComparison.Ordinal);
    Assert.Contains("caddy run", error.ToString(), StringComparison.Ordinal);
  }

  private static ShimRuntimeDependencies CreateDependencies(
      ICadderDaemonConnector connector,
      IDaemonProcessLauncher launcher,
      IShimLifetimeWaiter lifetimeWaiter,
      TextWriter output,
      TextWriter error)
  {
    return new ShimRuntimeDependencies
    {
      CurrentDirectoryProvider = () => Path.Combine(Path.GetTempPath(), "cadder-shim-entrypoint"),
      ProcessIdentityProvider = new FixedProcessIdentityProvider(),
      DaemonConnector = connector,
      DaemonLauncher = launcher,
      LifetimeWaiter = lifetimeWaiter,
      Output = output,
      Error = error,
      NonceFactory = () => "nonce-1",
      DaemonReadyTimeout = TimeSpan.FromSeconds(1),
      DaemonReadyPollInterval = TimeSpan.FromMilliseconds(1)
    };
  }

  private sealed class FixedProcessIdentityProvider : IShimProcessIdentityProvider
  {
    public ShimProcessIdentity GetCurrentProcessIdentity()
    {
      return new ShimProcessIdentity(
          1234,
          DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
          "C:\\tools\\caddy.exe");
    }
  }

  private sealed class CompletingLifetimeWaiter : IShimLifetimeWaiter
  {
    public ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingDaemonLauncher : IDaemonProcessLauncher
  {
    public bool Started { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
      Started = true;
      return ValueTask.CompletedTask;
    }
  }

  private sealed class SucceedingConnector(ICadderDaemonConnection connection) : ICadderDaemonConnector
  {
    public ValueTask<ICadderDaemonConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
      return ValueTask.FromResult(connection);
    }
  }

  private sealed class FlakyConnector(ICadderDaemonConnection connection) : ICadderDaemonConnector
  {
    public int Attempts { get; private set; }

    public ValueTask<ICadderDaemonConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
      Attempts++;

      if (Attempts == 1)
      {
        throw new IOException("Pipe is unavailable.");
      }

      return ValueTask.FromResult(connection);
    }
  }

  private sealed class RecordingDaemonConnection : ICadderDaemonConnection
  {
    public RegisterEntrypointRequest? RegisterRequest { get; private set; }

    public UnregisterEntrypointRequest? UnregisterRequest { get; private set; }

    public bool ThrowOnUnregister { get; init; }

    public ValueTask<RegisterEntrypointResponse> RegisterAsync(
        RegisterEntrypointRequest request,
        CancellationToken cancellationToken = default)
    {
      RegisterRequest = request;
      return ValueTask.FromResult(new RegisterEntrypointResponse(
          request.RequestId,
          true,
          "Registered.",
          request.Registration.RegistrationId));
    }

    public ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
        UnregisterEntrypointRequest request,
        CancellationToken cancellationToken = default)
    {
      if (ThrowOnUnregister)
      {
        throw new IOException("Pipe was closed.");
      }

      UnregisterRequest = request;
      return ValueTask.FromResult(new UnregisterEntrypointResponse(
          request.RequestId,
          true,
          "Unregistered."));
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }
}
