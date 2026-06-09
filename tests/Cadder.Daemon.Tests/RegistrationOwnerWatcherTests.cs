using System.ComponentModel;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class RegistrationOwnerWatcherTests
{
  [Fact]
  public async Task SweepOnceAsync_RemovesOnlyRegistrationsOwnedByDeadProcess()
  {
    var store = new InMemoryRegistrationStore();
    var deadOwnerStartedAtUtc = DateTimeOffset.Parse("2026-06-09T11:59:59Z");
    var reusedPidStartedAtUtc = DateTimeOffset.Parse("2026-06-09T12:30:00Z");
    var deadRegistration = await store.RegisterAsync(
        CreateRegistration("dead", 1234, deadOwnerStartedAtUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:00Z"));
    var reusedPidRegistration = await store.RegisterAsync(
        CreateRegistration("reused", 1234, reusedPidStartedAtUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:01Z"));
    var aliveRegistration = await store.RegisterAsync(
        CreateRegistration("alive", 5678, deadOwnerStartedAtUtc),
        DateTimeOffset.Parse("2026-06-09T12:00:02Z"));
    var probe = new FakeOwnerProcessProbe
    {
      [deadRegistration.OwnerProcess] = OwnerProcessLiveness.Dead,
      [reusedPidRegistration.OwnerProcess] = OwnerProcessLiveness.Alive,
      [aliveRegistration.OwnerProcess] = OwnerProcessLiveness.Alive
    };
    var publishedCounts = new List<int>();
    var watcher = new RegistrationOwnerWatcher(
        store,
        probe,
        new NoopRealCaddyRuntimeAdapter(),
        new GuiStateProjector(),
        new InMemoryGuiStateChangeBroadcaster(),
        (count, _) =>
        {
          publishedCounts.Add(count);
          return ValueTask.CompletedTask;
        });

    var removed = await watcher.SweepOnceAsync();

    var remaining = await store.ListAsync();
    Assert.Equal(1, removed);
    Assert.Equal(2, remaining.Count);
    Assert.Equal(["shim-alive", "shim-reused"],
        remaining.Select(static registration => registration.RegistrationId).ToArray());
    Assert.Equal([2], publishedCounts);
  }

  [Fact]
  public void SystemOwnerProcessProbe_WhenStartTimeReadFailsWithInvalidOperation_ReturnsDead()
  {
    var owner = new OwnerProcessIdentity(
        1234,
        DateTimeOffset.Parse("2026-06-09T11:59:59Z"),
        "nonce-1",
        "C:\\tools\\caddy.exe");
    var probe = new SystemOwnerProcessProbe(_ => new ThrowingOwnerProcessHandle(
        new InvalidOperationException("Process exited.")));

    var liveness = probe.GetLiveness(owner);

    Assert.Equal(OwnerProcessLiveness.Dead, liveness);
  }

  [Fact]
  public void SystemOwnerProcessProbe_WhenStartTimeReadFailsWithAccessError_ReturnsUnknown()
  {
    var owner = new OwnerProcessIdentity(
        1234,
        DateTimeOffset.Parse("2026-06-09T11:59:59Z"),
        "nonce-1",
        "C:\\tools\\caddy.exe");
    var probe = new SystemOwnerProcessProbe(_ => new ThrowingOwnerProcessHandle(
        new Win32Exception("Access denied.")));

    var liveness = probe.GetLiveness(owner);

    Assert.Equal(OwnerProcessLiveness.Unknown, liveness);
  }

  private static EntrypointRegistration CreateRegistration(
      string nonce,
      int processId,
      DateTimeOffset processStartTimeUtc)
  {
    var logStream = new LogStreamIdentity($"entrypoint-{nonce}", null, "shim");

    return new EntrypointRegistration(
        $"shim-{nonce}",
        new EntrypointInstanceIdentity($"shim-{nonce}", DateTimeOffset.Parse("2026-06-09T12:00:00Z"), nonce),
        new SourcePath("D:\\Projects\\Sample", "D:\\Projects\\Sample"),
        new SourcePath("Caddyfile", "D:\\Projects\\Sample\\Caddyfile"),
        [],
        ActivationState.Registered,
        new OwnerProcessIdentity(processId, processStartTimeUtc, nonce, "C:\\tools\\caddy.exe"),
        logStream,
        new ShimRunMetadata("caddyfile", ["run", "--config", "Caddyfile"], "run --config Caddyfile"));
  }

  private sealed class FakeOwnerProcessProbe : Dictionary<OwnerProcessIdentity, OwnerProcessLiveness>, IOwnerProcessProbe
  {
    public OwnerProcessLiveness GetLiveness(OwnerProcessIdentity owner)
    {
      return TryGetValue(owner, out var liveness)
          ? liveness
          : OwnerProcessLiveness.Unknown;
    }
  }

  private sealed class ThrowingOwnerProcessHandle(Exception exception) : IOwnerProcessHandle
  {
    public DateTime StartTime => throw exception;

    public void Dispose()
    {
    }
  }
}
