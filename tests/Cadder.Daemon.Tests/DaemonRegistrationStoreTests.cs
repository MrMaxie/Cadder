using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class DaemonRegistrationStoreTests
{
  [Fact]
  public async Task ListAsync_WhenEmpty_ReturnsNoRegistrations()
  {
    var store = new InMemoryRegistrationStore();

    var registrations = await store.ListAsync();

    Assert.Empty(registrations);
  }

  [Fact]
  public async Task RegisterAsync_RecordsLifecycleMetadata()
  {
    var store = new InMemoryRegistrationStore();
    var observedAtUtc = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

    var registration = await store.RegisterAsync(CreateRegistration("nonce-1"), observedAtUtc);

    Assert.Equal("shim-nonce-1", registration.RegistrationId);
    Assert.Equal(observedAtUtc, registration.CreatedAtUtc);
    Assert.Equal(observedAtUtc, registration.LastHeartbeatUtc);
    Assert.Equal(1234, registration.OwnerProcess.ProcessId);
    Assert.Equal(DateTimeOffset.Parse("2026-06-09T11:59:59Z"), registration.OwnerProcess.ProcessStartTimeUtc);
    Assert.Equal("C:\\tools\\caddy.exe", registration.OwnerProcess.ExecutablePath);
    Assert.Equal("D:\\Projects\\Sample", registration.SourceWorkingDirectory.Canonical);
    Assert.Equal("D:\\Projects\\Sample\\Caddyfile", registration.SourceConfigPath.Canonical);
    Assert.Equal("run --config Caddyfile", registration.ShimRun?.CommandLine);
  }

  [Fact]
  public async Task StoreOperations_WithTenConcurrentRegistrations_DoNotCorruptState()
  {
    var store = new InMemoryRegistrationStore();
    var startedAtUtc = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

    await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
        store.RegisterAsync(
            CreateRegistration($"nonce-{index}", processId: 2000 + index),
            startedAtUtc.AddSeconds(index)).AsTask()));

    Assert.Equal(10, (await store.ListAsync()).Count);

    await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
        store.UpdateAsync(
            new EntrypointRegistrationPatch(
                $"shim-nonce-{index}",
                $"nonce-{index}",
                null,
                new SourcePath($"Caddyfile.{index}", $"D:\\Projects\\Sample\\Caddyfile.{index}"),
                null,
                ActivationState.Registered,
                null),
            startedAtUtc.AddMinutes(1).AddSeconds(index)).AsTask()));
    await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
        store.ToggleAsync(
            $"shim-nonce-{index}",
            $"nonce-{index}",
            enabled: index % 2 == 0,
            startedAtUtc.AddMinutes(2).AddSeconds(index)).AsTask()));
    await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
        store.HeartbeatAsync(
            $"shim-nonce-{index}",
            $"nonce-{index}",
            startedAtUtc.AddMinutes(3).AddSeconds(index)).AsTask()));
    await Task.WhenAll(Enumerable.Range(0, 5).Select(index =>
        store.RemoveAsync($"shim-nonce-{index}", $"nonce-{index}").AsTask()));

    var remaining = await store.ListAsync();

    Assert.Equal(5, remaining.Count);
    Assert.Equal(["shim-nonce-5", "shim-nonce-6", "shim-nonce-7", "shim-nonce-8", "shim-nonce-9"],
        remaining.Select(static registration => registration.RegistrationId).ToArray());
    Assert.All(remaining, registration => Assert.Equal(startedAtUtc.AddMinutes(3).AddSeconds(
        int.Parse(registration.EntrypointInstance.ShimSessionNonce["nonce-".Length..])), registration.LastHeartbeatUtc));
  }

  [Fact]
  public async Task RemoveByOwnerAsync_RemovesOnlyMatchingProcessIdentity()
  {
    var store = new InMemoryRegistrationStore();
    var ownerStartedAtUtc = DateTimeOffset.Parse("2026-06-09T11:59:59Z");
    var samePidReusedStartUtc = DateTimeOffset.Parse("2026-06-09T12:30:00Z");

    var first = await store.RegisterAsync(
        CreateRegistration("nonce-1", processId: 1234, processStartTimeUtc: ownerStartedAtUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:00Z"));
    await store.RegisterAsync(
        CreateRegistration("nonce-2", processId: 1234, processStartTimeUtc: samePidReusedStartUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:01Z"));
    await store.RegisterAsync(
        CreateRegistration("nonce-3", processId: 5678, processStartTimeUtc: ownerStartedAtUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:02Z"));

    var removed = await store.RemoveByOwnerAsync(first.OwnerProcess);

    var remaining = await store.ListAsync();
    Assert.Equal(1, removed);
    Assert.Equal(["shim-nonce-2", "shim-nonce-3"],
        remaining.Select(static registration => registration.RegistrationId).ToArray());
  }

  private static EntrypointRegistration CreateRegistration(
      string nonce,
      int processId = 1234,
      DateTimeOffset? processStartTimeUtc = null)
  {
    var logStream = new LogStreamIdentity($"entrypoint-{nonce}", null, "shim");

    return new EntrypointRegistration(
        $"shim-{nonce}",
        new EntrypointInstanceIdentity($"shim-{nonce}", DateTimeOffset.Parse("2026-06-09T12:00:00Z"), nonce),
        new SourcePath("D:\\Projects\\Sample", "D:\\Projects\\Sample"),
        new SourcePath("Caddyfile", "D:\\Projects\\Sample\\Caddyfile"),
        [],
        ActivationState.Registered,
        new OwnerProcessIdentity(
            processId,
            processStartTimeUtc ?? DateTimeOffset.Parse("2026-06-09T11:59:59Z"),
            nonce,
            "C:\\tools\\caddy.exe"),
        logStream,
        new ShimRunMetadata("caddyfile", ["run", "--config", "Caddyfile"], "run --config Caddyfile"));
  }
}
