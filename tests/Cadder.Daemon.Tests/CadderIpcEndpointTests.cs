using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class CadderIpcEndpointTests
{
  [Fact]
  public async Task RegistrationApi_CanRegisterUpdateListToggleHeartbeatAndUnregister()
  {
    var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-09T12:00:00Z"));
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(
        store,
        new NoopRealCaddyRuntimeAdapter(),
        timeProvider: timeProvider);
    var registration = CreateRegistration("nonce-1");

    var register = await endpoint.RegisterAsync(new RegisterEntrypointRequest("register-1", registration));

    Assert.True(register.Accepted);
    Assert.Equal("shim-nonce-1", register.RegistrationId);

    timeProvider.Advance(TimeSpan.FromSeconds(1));
    var update = await endpoint.UpdateAsync(new UpdateEntrypointRequest(
        "update-1",
        "shim-nonce-1",
        "nonce-1",
        null,
        new SourcePath("Caddyfile.alt", "D:\\Projects\\Sample\\Caddyfile.alt"),
        [],
        ActivationState.Registered,
        new ShimRunMetadata("json", ["run", "--config", "Caddyfile.alt"], "run --config Caddyfile.alt")));

    Assert.True(update.Accepted);
    Assert.Equal("Caddyfile.alt", update.Registration?.SourceConfigPath.Raw);
    Assert.Equal("json", update.Registration?.ShimRun?.Adapter);

    var list = await endpoint.ListAsync(new ListEntrypointsRequest("list-1"));

    Assert.True(list.Accepted);
    Assert.Single(list.Registrations);

    timeProvider.Advance(TimeSpan.FromSeconds(1));
    var toggle = await endpoint.ToggleAsync(new ToggleEntrypointRequest(
        "toggle-1",
        "shim-nonce-1",
        "nonce-1",
        false));

    Assert.True(toggle.Accepted);
    Assert.Equal(ActivationState.Inactive, toggle.Registration?.ActivationState);

    timeProvider.Advance(TimeSpan.FromSeconds(1));
    var heartbeat = await endpoint.HeartbeatAsync(new HeartbeatEntrypointRequest(
        "heartbeat-1",
        "shim-nonce-1",
        "nonce-1"));

    Assert.True(heartbeat.Accepted);
    Assert.Equal(DateTimeOffset.Parse("2026-06-09T12:00:03Z"), heartbeat.Registration?.LastHeartbeatUtc);

    var unregister = await endpoint.UnregisterAsync(new UnregisterEntrypointRequest(
        "unregister-1",
        "shim-nonce-1",
        "nonce-1"));

    Assert.True(unregister.Accepted);
    Assert.Empty(await store.ListAsync());
  }

  [Fact]
  public async Task SubscribeGuiStateAsync_EmitsInitialSnapshotAndRegistrationChanges()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    await using var subscription = endpoint
        .SubscribeGuiStateAsync(new SubscribeGuiStateRequest("subscribe-1"), timeout.Token)
        .GetAsyncEnumerator(timeout.Token);

    Assert.True(await subscription.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
    Assert.Equal(GuiStateChangeKind.Snapshot, subscription.Current.ChangeKind);
    Assert.Empty(subscription.Current.Snapshot.Registrations);

    var register = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-1",
        CreateRegistration("nonce-1")));

    Assert.True(register.Accepted);
    Assert.True(await subscription.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
    Assert.Equal(GuiStateChangeKind.RegistrationsChanged, subscription.Current.ChangeKind);
    Assert.Equal("shim-nonce-1", subscription.Current.RegistrationId);
    Assert.Single(subscription.Current.Snapshot.Registrations);
  }

  [Fact]
  public async Task RegisterAsync_WithMismatchedOwnerNonce_RejectsRegistration()
  {
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var registration = CreateRegistration("nonce-1") with
    {
      OwnerProcess = new OwnerProcessIdentity(
          1234,
          DateTimeOffset.Parse("2026-06-09T11:59:59Z"),
          "other-nonce",
          "C:\\tools\\caddy.exe")
    };

    var response = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-1",
        registration));

    Assert.False(response.Accepted);
    Assert.Null(response.RegistrationId);
    Assert.Empty(await store.ListAsync());
  }

  [Fact]
  public async Task RegisterAsync_WithMismatchedRegistrationAndInstanceIds_RejectsRegistration()
  {
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var registration = CreateRegistration("nonce-1") with
    {
      EntrypointInstance = new EntrypointInstanceIdentity(
          "different-instance",
          DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
          "nonce-1")
    };

    var response = await endpoint.RegisterAsync(new RegisterEntrypointRequest(
        "register-1",
        registration));

    Assert.False(response.Accepted);
    Assert.Null(response.RegistrationId);
    Assert.Empty(await store.ListAsync());
  }

  [Fact]
  public async Task SubscribeAsync_DoesNotPublishDeltasBeforeInitialSnapshotCompletes()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var broadcaster = new InMemoryGuiStateChangeBroadcaster();
    var initialFactoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseInitialFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var subscription = broadcaster
        .SubscribeAsync(
            "subscribe-1",
            async cancellationToken =>
            {
              initialFactoryStarted.TrySetResult();
              await releaseInitialFactory.Task.WaitAsync(cancellationToken);
              return CreateSnapshot([]);
            },
            timeout.Token)
        .GetAsyncEnumerator(timeout.Token);

    var initialMove = subscription.MoveNextAsync().AsTask();
    await initialFactoryStarted.Task.WaitAsync(timeout.Token);

    var publish = broadcaster.PublishAsync(
        GuiStateChangeKind.RegistrationsChanged,
        CreateSnapshot([CreateRegistration("nonce-1")]),
        "shim-nonce-1",
        timeout.Token).AsTask();

    await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    Assert.False(publish.IsCompleted);

    releaseInitialFactory.TrySetResult();
    Assert.True(await initialMove.WaitAsync(timeout.Token));
    Assert.Equal(GuiStateChangeKind.Snapshot, subscription.Current.ChangeKind);
    Assert.Empty(subscription.Current.Snapshot.Registrations);

    await publish.WaitAsync(timeout.Token);
    Assert.True(await subscription.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
    Assert.Equal(GuiStateChangeKind.RegistrationsChanged, subscription.Current.ChangeKind);
    Assert.Single(subscription.Current.Snapshot.Registrations);
  }

  private static EntrypointRegistration CreateRegistration(string nonce)
  {
    var logStream = new LogStreamIdentity($"entrypoint-{nonce}", null, "shim");

    return new EntrypointRegistration(
        $"shim-{nonce}",
        new EntrypointInstanceIdentity($"shim-{nonce}", DateTimeOffset.Parse("2026-06-09T12:00:00Z"), nonce),
        new SourcePath("D:\\Projects\\Sample", "D:\\Projects\\Sample"),
        new SourcePath("Caddyfile", "D:\\Projects\\Sample\\Caddyfile"),
        [],
        ActivationState.Registered,
        new OwnerProcessIdentity(1234, DateTimeOffset.Parse("2026-06-09T11:59:59Z"), nonce, "C:\\tools\\caddy.exe"),
        logStream,
        new ShimRunMetadata("caddyfile", ["run", "--config", "Caddyfile"], "run --config Caddyfile"));
  }

  private static GuiStateSnapshot CreateSnapshot(EntrypointRegistration[] registrations)
  {
    return new GuiStateSnapshot(
        DateTimeOffset.Parse("2026-06-09T12:00:00Z"),
        registrations,
        new RealCaddyRuntimeState(RealCaddyRuntimeStatus.NotResolved, null, null));
  }

  private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow()
    {
      return _utcNow;
    }

    public void Advance(TimeSpan interval)
    {
      _utcNow += interval;
    }
  }
}
