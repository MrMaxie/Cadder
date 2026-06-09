using System.Text.Json.Nodes;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class CaddyConfigCoordinatorTests
{
  [Fact]
  public async Task RegisterAsync_WithCaddyfile_PopulatesRegisteredDomainsAndReloadsRuntime()
  {
    using var temp = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter();
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(temp.Path, AdaptedJson(["api.example.localhost", "app.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);

    var response = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-1",
        CreateRegistration("nonce-1", temp.Path)));

    var registrations = await endpoint.ListAsync(new ListEntrypointsRequest("list-1"));
    Assert.True(response.Accepted);
    Assert.Single(runtime.EnsuredConfigs);
    Assert.Single(runtime.ReloadedConfigs);
    Assert.Equal(
        ["api.example.localhost", "app.example.localhost"],
        registrations.Registrations.Single().RegisteredDomains.Select(static domain => domain.Name.Canonical!).ToArray());
    Assert.Equal(CaddyConfigApplyStatus.Applied, (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-1"))).Snapshot?.CaddyConfig?.Status);
  }

  [Fact]
  public async Task UpdateAsync_WithInactiveSingleDomain_RemovesOnlyThatDomainFromComposedConfig()
  {
    using var temp = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter();
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(temp.Path, AdaptedJson(["api.example.localhost", "app.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);
    await endpoint.RegisterAsync(new RegisterEntrypointRequest("register-1", CreateRegistration("nonce-1", temp.Path)));
    var registered = (await endpoint.ListAsync(new ListEntrypointsRequest("list-1"))).Registrations.Single();
    var updatedDomains = registered.RegisteredDomains
        .Select(static domain => domain.Name.Canonical == "api.example.localhost"
            ? domain with { ActivationState = ActivationState.Inactive }
            : domain)
        .ToArray();

    var update = await endpoint.UpdateAsync(new UpdateEntrypointRequest(
        "update-1",
        registered.RegistrationId,
        registered.EntrypointInstance.ShimSessionNonce,
        null,
        null,
        updatedDomains,
        null,
        null));

    Assert.True(update.Accepted);
    Assert.Equal(2, runtime.EnsuredConfigs.Count);
    Assert.Equal(2, runtime.ReloadedConfigs.Count);
    Assert.DoesNotContain("api.example.localhost", runtime.ReloadedConfigs.Last(), StringComparison.Ordinal);
    Assert.Contains("app.example.localhost", runtime.ReloadedConfigs.Last(), StringComparison.Ordinal);
    Assert.Equal(2, update.Registration?.RegisteredDomains.Length);
  }

  [Fact]
  public async Task UpdateAsync_WithZeroActiveDomains_EntersIdleWithoutReloadingEmptyConfig()
  {
    using var temp = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter();
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(temp.Path, AdaptedJson(["api.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);
    await endpoint.RegisterAsync(new RegisterEntrypointRequest("register-1", CreateRegistration("nonce-1", temp.Path)));
    var registered = (await endpoint.ListAsync(new ListEntrypointsRequest("list-1"))).Registrations.Single();
    var inactiveDomains = registered.RegisteredDomains
        .Select(static domain => domain with { ActivationState = ActivationState.Inactive })
        .ToArray();

    var update = await endpoint.UpdateAsync(new UpdateEntrypointRequest(
        "update-1",
        registered.RegistrationId,
        registered.EntrypointInstance.ShimSessionNonce,
        null,
        null,
        inactiveDomains,
        null,
        null));
    var snapshot = (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-1"))).Snapshot;

    Assert.True(update.Accepted);
    Assert.Single(runtime.ReloadedConfigs);
    Assert.Equal(1, runtime.IdleCount);
    Assert.Equal(CaddyConfigApplyStatus.Idle, snapshot?.CaddyConfig?.Status);
    Assert.Equal(RealCaddyRuntimeStatus.Idle, snapshot?.RealCaddyRuntime.Status);
  }

  [Fact]
  public async Task RegisterAsync_WithConflictingDomainAcrossInstances_ReportsConflictBeforeReload()
  {
    using var first = TestCaddyfile.Create();
    using var second = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter();
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(first.Path, AdaptedJson(["api.example.localhost"]));
    adapter.SetConfig(second.Path, AdaptedJson(["api.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);
    await endpoint.RegisterAsync(new RegisterEntrypointRequest("register-1", CreateRegistration("nonce-1", first.Path)));

    var response = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-2",
        CreateRegistration("nonce-2", second.Path, processId: 5678)));
    var state = (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-1"))).Snapshot?.CaddyConfig;

    Assert.True(response.Accepted);
    Assert.Single(runtime.ReloadedConfigs);
    Assert.Equal(CaddyConfigApplyStatus.Failed, state?.Status);
    var diagnostic = Assert.Single(state?.Diagnostics ?? []);
    Assert.Equal("domain-conflict", diagnostic.Code);
    Assert.Equal("api.example.localhost", diagnostic.DomainKey);
    Assert.Equal(
        [.. new[] { first.Path, second.Path }.OrderBy(static path => path, StringComparer.Ordinal)],
        diagnostic.SourceConfigPaths);
  }

  [Fact]
  public async Task UpdateAsync_WhenValidationFails_PreservesLastKnownGoodConfig()
  {
    using var temp = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter
    {
      Validate = config => !config.Contains("invalid.example.localhost", StringComparison.Ordinal)
    };
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(temp.Path, AdaptedJson(["api.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);
    await endpoint.RegisterAsync(new RegisterEntrypointRequest("register-1", CreateRegistration("nonce-1", temp.Path)));
    var goodState = (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-good"))).Snapshot?.CaddyConfig;
    adapter.SetConfig(temp.Path, AdaptedJson(["invalid.example.localhost"]));

    var update = await endpoint.UpdateAsync(new UpdateEntrypointRequest(
        "update-1",
        "shim-nonce-1",
        "nonce-1",
        null,
        new SourcePath(temp.Path, temp.Path),
        [],
        null,
        null));
    var failedState = (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-failed"))).Snapshot?.CaddyConfig;

    Assert.True(update.Accepted);
    Assert.Single(runtime.ReloadedConfigs);
    Assert.Equal(CaddyConfigApplyStatus.Failed, failedState?.Status);
    Assert.Equal(goodState?.EffectiveConfigHash, failedState?.EffectiveConfigHash);
    Assert.Equal(goodState?.LastSuccessfulReloadAtUtc, failedState?.LastSuccessfulReloadAtUtc);
  }

  [Fact]
  public async Task RegisterAsync_WhenRuntimeStartFails_ReportsStructuredRuntimeDiagnosticWithoutReload()
  {
    using var temp = TestCaddyfile.Create();
    var runtime = new RecordingRuntimeAdapter
    {
      EnsureRunning = _ => new RealCaddyRuntimeState(
          RealCaddyRuntimeStatus.Unhealthy,
          new RealCaddyBinaryIdentity("C:\\tools\\caddy-real.exe", "real-caddy"),
          "2.8.4",
          Diagnostics:
          [
              new CaddyRuntimeDiagnostic(
                    "runtime-start-failed",
                    "Injected runtime start failure.",
                    "run")
          ])
    };
    var adapter = new RecordingConfigAdapter();
    adapter.SetConfig(temp.Path, AdaptedJson(["api.example.localhost"]));
    var endpoint = CreateEndpoint(runtime, adapter);

    var response = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-1",
        CreateRegistration("nonce-1", temp.Path)));
    var snapshot = (await endpoint.QueryStateAsync(new QueryGuiStateRequest("state-1"))).Snapshot;

    Assert.True(response.Accepted);
    Assert.Empty(runtime.ReloadedConfigs);
    Assert.Equal(CaddyConfigApplyStatus.Failed, snapshot?.CaddyConfig?.Status);
    Assert.Equal("runtime-start-failed", snapshot?.CaddyConfig?.Diagnostics.Single().Code);
    Assert.Equal(RealCaddyRuntimeStatus.Unhealthy, snapshot?.RealCaddyRuntime.Status);
    Assert.Equal("runtime-start-failed", snapshot?.RealCaddyRuntime.Diagnostics?.Single().Code);
  }

  private static CadderIpcEndpoint CreateEndpoint(
      RecordingRuntimeAdapter runtime,
      RecordingConfigAdapter adapter)
  {
    var coordinator = new CaddyConfigCoordinator(runtime, adapter);
    return new CadderIpcEndpoint(new InMemoryRegistrationStore(), runtime, coordinator);
  }

  private static EntrypointRegistration CreateRegistration(
      string nonce,
      string sourceConfigPath,
      int processId = 1234)
  {
    var registrationId = $"shim-{nonce}";
    var logStream = new LogStreamIdentity($"entrypoint-{nonce}", null, "shim");

    return new EntrypointRegistration(
        registrationId,
        new EntrypointInstanceIdentity(registrationId, DateTimeOffset.Parse("2026-06-09T12:00:00Z"), nonce),
        new SourcePath(Path.GetDirectoryName(sourceConfigPath) ?? ".", Path.GetDirectoryName(sourceConfigPath)),
        new SourcePath(sourceConfigPath, sourceConfigPath),
        [],
        ActivationState.Registered,
        new OwnerProcessIdentity(processId, DateTimeOffset.Parse("2026-06-09T11:59:59Z"), nonce, "C:\\tools\\caddy.exe"),
        logStream,
        new ShimRunMetadata("caddyfile", ["run", "--config", sourceConfigPath], $"run --config {sourceConfigPath}"));
  }

  private static string AdaptedJson(string[] hosts)
  {
    var routes = new JsonArray();
    foreach (var host in hosts)
    {
      routes.Add(new JsonObject
      {
        ["match"] = new JsonArray
        {
          new JsonObject
          {
            ["host"] = new JsonArray(JsonValue.Create(host))
          }
        },
        ["handle"] = new JsonArray
        {
          new JsonObject
          {
            ["handler"] = "static_response",
            ["body"] = host
          }
        },
        ["terminal"] = true
      });
    }

    var config = new JsonObject
    {
      ["apps"] = new JsonObject
      {
        ["http"] = new JsonObject
        {
          ["servers"] = new JsonObject
          {
            ["srv0"] = new JsonObject
            {
              ["listen"] = new JsonArray(JsonValue.Create(":443")),
              ["routes"] = routes
            }
          }
        }
      }
    };

    return config.ToJsonString();
  }

  private sealed class RecordingConfigAdapter : ICaddyfileConfigAdapter
  {
    private readonly Dictionary<string, string> _configs = new(StringComparer.Ordinal);

    public void SetConfig(string sourceConfigPath, string json)
    {
      _configs[sourceConfigPath] = json;
    }

    public ValueTask<CaddyfileAdaptResult> AdaptAsync(
        SourcePath sourceConfigPath,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var path = sourceConfigPath.Canonical ?? sourceConfigPath.Raw;
      if (!_configs.TryGetValue(path, out var json))
      {
        return ValueTask.FromResult(CaddyfileAdaptResult.Failure(
            sourceConfigPath,
            [new CaddyConfigDiagnostic("adapt-missing-test-config", "No test config was registered.", null, [path])]));
      }

      return ValueTask.FromResult(CaddyfileAdaptResult.Success(sourceConfigPath, JsonNode.Parse(json)!.AsObject()));
    }
  }

  private sealed class RecordingRuntimeAdapter : IRealCaddyRuntimeAdapter
  {
    private RealCaddyRuntimeState _state = new(
        RealCaddyRuntimeStatus.Running,
        new RealCaddyBinaryIdentity("C:\\tools\\caddy-real.exe", "real-caddy"),
        "2.8.4",
        new RealCaddyProcessIdentity(2019, DateTimeOffset.Parse("2026-06-09T12:00:00Z"), true),
        "http://127.0.0.1:2019",
        []);

    public List<string> EnsuredConfigs { get; } = [];

    public List<string> ReloadedConfigs { get; } = [];

    public int IdleCount { get; private set; }

    public Func<string, bool> Validate { get; init; } = static _ => true;

    public Func<string, RealCaddyRuntimeState>? EnsureRunning { get; init; }

    public ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      return ValueTask.FromResult(_state);
    }

    public ValueTask<RealCaddyRuntimeState> EnsureRunningAsync(
        CaddyRuntimeConfig config,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      EnsuredConfigs.Add(config.Content);
      if (EnsureRunning is not null)
      {
        _state = EnsureRunning(config.Content);
        return ValueTask.FromResult(_state);
      }

      _state = _state with { Status = RealCaddyRuntimeStatus.Running };
      return ValueTask.FromResult(_state);
    }

    public ValueTask<CaddyRuntimeOperationResult> ValidateConfigAsync(
        CaddyRuntimeConfig config,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      return ValueTask.FromResult(Validate(config.Content)
          ? CaddyRuntimeOperationResult.Success("Valid.")
          : CaddyRuntimeOperationResult.Failure("Invalid config."));
    }

    public ValueTask<CaddyRuntimeOperationResult> ReloadConfigAsync(
        CaddyRuntimeConfig config,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      ReloadedConfigs.Add(config.Content);
      return ValueTask.FromResult(CaddyRuntimeOperationResult.Success("Reloaded."));
    }

    public ValueTask<RealCaddyRuntimeState> EnterIdleAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

      IdleCount++;
      _state = _state with
      {
        Status = RealCaddyRuntimeStatus.Idle,
        Process = null,
        Diagnostics = []
      };
      return ValueTask.FromResult(_state);
    }
  }

  private sealed class TestCaddyfile : IDisposable
  {
    private readonly string _directoryPath;

    private TestCaddyfile(string directoryPath, string path)
    {
      _directoryPath = directoryPath;
      Path = path;
    }

    public string Path { get; }

    public static TestCaddyfile Create()
    {
      var directoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cadder-tests-{Guid.NewGuid():N}");
      Directory.CreateDirectory(directoryPath);
      var path = System.IO.Path.Combine(directoryPath, "Caddyfile");
      File.WriteAllText(path, string.Empty);
      return new TestCaddyfile(directoryPath, path);
    }

    public void Dispose()
    {
      Directory.Delete(_directoryPath, recursive: true);
    }
  }
}
