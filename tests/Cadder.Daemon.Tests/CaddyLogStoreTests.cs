using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class CaddyLogStoreTests
{
  [Fact]
  public void Query_WithStreamLimitAndRetention_ReturnsRecentEntriesAndGapMetadata()
  {
    var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-09T12:00:00Z"));
    var store = new InMemoryCaddyLogStore(
        maxEntries: 10,
        maxEntriesPerStream: 2,
        timeProvider: timeProvider);
    var stream = new LogStreamIdentity("domain-example.com", "example.com", "caddy");

    store.TryWrite(CreateRequest(stream, "first"));
    store.TryWrite(CreateRequest(stream, "second"));
    store.TryWrite(CreateRequest(stream, "third"));

    var result = store.Query(new CaddyLogQuery(stream, 10, 0, null, null, null));

    Assert.Equal(["second", "third"], result.Entries.Select(static entry => entry.RawMessage).ToArray());
    Assert.True(result.HasGap);
    Assert.True(result.TruncatedByRetention);
  }

  [Fact]
  public void Query_WithSeverityTimeAndCursorFilters_ReturnsForwardPage()
  {
    var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-09T12:00:00Z"));
    var store = new InMemoryCaddyLogStore(timeProvider: timeProvider);
    var stream = new LogStreamIdentity("runtime", null, "stderr");
    store.TryWrite(CreateRequest(stream, "debug", CaddyLogSeverity.Debug));
    timeProvider.Advance(TimeSpan.FromSeconds(1));
    store.TryWrite(CreateRequest(stream, "first-error", CaddyLogSeverity.Error));
    timeProvider.Advance(TimeSpan.FromSeconds(1));
    store.TryWrite(CreateRequest(stream, "second-error", CaddyLogSeverity.Error));

    var result = store.Query(new CaddyLogQuery(
        stream,
        Limit: 1,
        AfterSequence: 1,
        MinimumSeverity: CaddyLogSeverity.Error,
        SinceUtc: DateTimeOffset.Parse("2026-06-09T12:00:01Z"),
        UntilUtc: null));

    var entry = Assert.Single(result.Entries);
    Assert.Equal("first-error", entry.RawMessage);
    Assert.Equal(2, result.NextSequence);
  }

  [Fact]
  public void TryWrite_RedactsSensitiveValuesBeforeQuery()
  {
    var store = new InMemoryCaddyLogStore();
    var stream = new LogStreamIdentity("runtime-control", null, "caddy-control");

    store.TryWrite(CreateRequest(stream, "reload failed token=super-secret Authorization: Bearer abc123"));

    var result = store.Query(new CaddyLogQuery(stream, 10, null, null, null, null));
    var entry = Assert.Single(result.Entries);
    Assert.DoesNotContain("super-secret", entry.RawMessage, StringComparison.Ordinal);
    Assert.DoesNotContain("abc123", entry.RawMessage, StringComparison.Ordinal);
    Assert.Contains("<redacted>", entry.RawMessage, StringComparison.Ordinal);
  }

  private static CaddyLogWriteRequest CreateRequest(
      LogStreamIdentity stream,
      string message,
      CaddyLogSeverity severity = CaddyLogSeverity.Info)
  {
    return new CaddyLogWriteRequest(
        stream,
        severity,
        CaddyLogAttributionKind.Domain,
        CaddyLogEntryKind.Normal,
        message,
        DomainKey: stream.DomainKey);
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
