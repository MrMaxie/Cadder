using System.Diagnostics;
using System.Text;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class RealCaddyRuntimeAdapterTests
{
  [Fact]
  public void Resolve_WhenCandidateIsKnownCadderShim_RejectsIt()
  {
    using var temp = TestExecutable.Create("caddy.exe");
    var resolver = new RealCaddyExecutableResolver(temp.Path, [temp.Path]);

    var resolution = resolver.Resolve();

    Assert.False(resolution.Succeeded);
    var diagnostic = Assert.Single(resolution.Diagnostics);
    Assert.Equal("runtime-resolved-to-cadder-shim", diagnostic.Code);
  }

  [Fact]
  public async Task EnsureRunningAsync_WithResolvedRuntime_StartsOwnedProcessAndStopKillsOnlyThatProcess()
  {
    using var temp = TestExecutable.Create("caddy-real.exe");
    var resolver = new RealCaddyExecutableResolver(temp.Path, []);
    var processFactory = new FakeProcessFactory();
    var logStore = new InMemoryCaddyLogStore(maxAge: TimeSpan.FromDays(1));
    var adapter = new ProcessRealCaddyRuntimeAdapter(
        resolver: resolver,
        processFactory: processFactory,
        startupObservationDelay: TimeSpan.Zero,
        logSink: logStore);

    var state = await adapter.EnsureRunningAsync(new CaddyRuntimeConfig("{}"));
    await adapter.StopAsync();

    Assert.Equal(RealCaddyRuntimeStatus.Running, state.Status);
    Assert.Equal(temp.Path, state.Binary?.ResolvedPath);
    Assert.Equal(8675, state.Process?.ProcessId);
    Assert.True(state.Process?.OwnedByCadder);
    Assert.Equal(["version", "run"], processFactory.StartedOperations);
    Assert.True(processFactory.OwnedRuntimeProcess?.KillCalled);
    Assert.True(processFactory.OwnedRuntimeProcess?.Disposed);
    Assert.True(processFactory.RunStartInfo?.RedirectStandardOutput);
    Assert.True(processFactory.RunStartInfo?.RedirectStandardError);
  }

  [Fact]
  public async Task EnsureRunningAsync_CapturesRuntimeJsonOutputByDomainStream()
  {
    using var temp = TestExecutable.Create("caddy-real.exe");
    var resolver = new RealCaddyExecutableResolver(temp.Path, []);
    var processFactory = new FakeProcessFactory
    {
      RuntimeStdout = """
        {"level":"info","ts":"2026-06-09T12:00:05Z","request":{"host":"Api.Example.Localhost"},"msg":"handled"}
        """
    };
    var logStore = new InMemoryCaddyLogStore(maxAge: TimeSpan.FromDays(1));
    var adapter = new ProcessRealCaddyRuntimeAdapter(
        resolver: resolver,
        processFactory: processFactory,
        startupObservationDelay: TimeSpan.Zero,
        logSink: logStore);

    await adapter.EnsureRunningAsync(new CaddyRuntimeConfig("{}"));
    await adapter.StopAsync();

    var result = logStore.Query(new CaddyLogQuery(
        new LogStreamIdentity("domain-api.example.localhost", "api.example.localhost", "caddy"),
        10,
        null,
        null,
        null,
        null));

    var entry = Assert.Single(result.Entries);
    Assert.Equal(CaddyLogSeverity.Info, entry.Severity);
    Assert.Equal(CaddyLogAttributionKind.Domain, entry.AttributionKind);
    Assert.Equal("api.example.localhost", entry.DomainKey);
    Assert.Equal("run", entry.Operation);
    Assert.Contains("\"msg\":\"handled\"", entry.RawMessage, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ValidateConfigAsync_CapturesRuntimeControlOutputAndRedactsSecrets()
  {
    using var temp = TestExecutable.Create("caddy-real.exe");
    var resolver = new RealCaddyExecutableResolver(temp.Path, []);
    var processFactory = new FakeProcessFactory
    {
      ValidateExitCode = 1,
      ValidateStderr = "validation failed token=super-secret"
    };
    var logStore = new InMemoryCaddyLogStore();
    var adapter = new ProcessRealCaddyRuntimeAdapter(
        resolver: resolver,
        processFactory: processFactory,
        logSink: logStore);

    var result = await adapter.ValidateConfigAsync(new CaddyRuntimeConfig("{}"));

    Assert.False(result.Succeeded);
    Assert.DoesNotContain("super-secret", result.Message, StringComparison.Ordinal);
    var logs = logStore.Query(new CaddyLogQuery(
        CaddyRuntimeLogParser.RuntimeControlStream,
        10,
        null,
        CaddyLogSeverity.Error,
        null,
        null));
    Assert.Contains(logs.Entries, static entry => entry.Operation == "validate");
    Assert.DoesNotContain(logs.Entries, static entry => entry.RawMessage.Contains("super-secret", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ValidateConfigAsync_WhenResolutionFails_ReturnsStructuredRuntimeDiagnostic()
  {
    var resolver = new RealCaddyExecutableResolver("missing-caddy-real-for-test", []);
    var adapter = new ProcessRealCaddyRuntimeAdapter(resolver: resolver);

    var result = await adapter.ValidateConfigAsync(new CaddyRuntimeConfig("{}"));
    var state = await adapter.InspectAsync();

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "runtime-not-found");
    Assert.Equal(RealCaddyRuntimeStatus.NotResolved, state.Status);
    Assert.Contains(state.Diagnostics ?? [], static diagnostic => diagnostic.Code == "runtime-not-found");
  }

  private sealed class FakeProcessFactory : IManagedProcessFactory
  {
    public FakeManagedProcess? OwnedRuntimeProcess { get; private set; }

    public List<string> StartedOperations { get; } = [];

    public ProcessStartInfo? RunStartInfo { get; private set; }

    public string RuntimeStdout { get; init; } = string.Empty;

    public string RuntimeStderr { get; init; } = string.Empty;

    public int ValidateExitCode { get; init; }

    public string ValidateStdout { get; init; } = string.Empty;

    public string ValidateStderr { get; init; } = string.Empty;

    public IManagedProcess? Start(ProcessStartInfo startInfo)
    {
      var operation = startInfo.ArgumentList[0];
      StartedOperations.Add(operation);

      if (operation == "version")
      {
        return FakeManagedProcess.Exited(301, "2.8.4 test-build");
      }

      if (operation == "run")
      {
        RunStartInfo = startInfo;
        OwnedRuntimeProcess = FakeManagedProcess.Running(8675, RuntimeStdout, RuntimeStderr);
        return OwnedRuntimeProcess;
      }

      if (operation == "validate")
      {
        return FakeManagedProcess.Exited(302, ValidateStdout, ValidateStderr, ValidateExitCode);
      }

      throw new InvalidOperationException($"Unexpected operation '{operation}'.");
    }
  }

  private sealed class FakeManagedProcess : IManagedProcess
  {
    private readonly MemoryStream _stdout;
    private readonly MemoryStream _stderr;

    private FakeManagedProcess(int id, bool hasExited, int exitCode, string stdout, string stderr)
    {
      Id = id;
      HasExited = hasExited;
      ExitCode = exitCode;
      StartTime = DateTime.Parse("2026-06-09T12:00:00Z").ToUniversalTime();
      _stdout = new MemoryStream(Encoding.UTF8.GetBytes(stdout));
      _stderr = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
      StandardOutput = new StreamReader(_stdout);
      StandardError = new StreamReader(_stderr);
    }

    public int Id { get; }

    public DateTime StartTime { get; }

    public bool HasExited { get; private set; }

    public int ExitCode { get; private set; }

    public StreamReader StandardOutput { get; }

    public StreamReader StandardError { get; }

    public bool KillCalled { get; private set; }

    public bool Disposed { get; private set; }

    public static FakeManagedProcess Exited(
        int id,
        string stdout,
        string stderr = "",
        int exitCode = 0)
    {
      return new FakeManagedProcess(id, true, exitCode, stdout, stderr);
    }

    public static FakeManagedProcess Running(
        int id,
        string stdout = "",
        string stderr = "")
    {
      return new FakeManagedProcess(id, false, 0, stdout, stderr);
    }

    public ValueTask WaitForExitAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      HasExited = true;
      return ValueTask.CompletedTask;
    }

    public void Kill(bool entireProcessTree)
    {
      Assert.True(entireProcessTree);
      KillCalled = true;
      HasExited = true;
    }

    public void Dispose()
    {
      Disposed = true;
      StandardOutput.Dispose();
      StandardError.Dispose();
      _stdout.Dispose();
      _stderr.Dispose();
    }
  }

  private sealed class TestExecutable : IDisposable
  {
    private readonly string _directoryPath;

    private TestExecutable(string directoryPath, string path)
    {
      _directoryPath = directoryPath;
      Path = path;
    }

    public string Path { get; }

    public static TestExecutable Create(string fileName)
    {
      var directoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cadder-runtime-tests-{Guid.NewGuid():N}");
      Directory.CreateDirectory(directoryPath);
      var path = System.IO.Path.Combine(directoryPath, fileName);
      File.WriteAllText(path, "test executable placeholder");
      return new TestExecutable(directoryPath, path);
    }

    public void Dispose()
    {
      Directory.Delete(_directoryPath, recursive: true);
    }
  }
}
