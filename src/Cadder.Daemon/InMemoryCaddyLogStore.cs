using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class InMemoryCaddyLogStore : ICaddyLogStore
{
  public const int DefaultMaxEntries = 2_000;
  public const int DefaultMaxEntriesPerStream = 500;
  public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(30);

  private readonly object _gate = new();
  private readonly TimeProvider _timeProvider;
  private readonly ICaddyLogRedactor _redactor;
  private readonly int _maxEntries;
  private readonly int _maxEntriesPerStream;
  private readonly TimeSpan _maxAge;
  private readonly List<CaddyLogEntry> _entries = [];
  private long _nextSequence = 1;
  private long _firstDroppedSequence;

  public InMemoryCaddyLogStore(
      int maxEntries = DefaultMaxEntries,
      int maxEntriesPerStream = DefaultMaxEntriesPerStream,
      TimeSpan? maxAge = null,
      TimeProvider? timeProvider = null,
      ICaddyLogRedactor? redactor = null)
  {
    if (maxEntries <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(maxEntries), "The global log entry limit must be positive.");
    }

    if (maxEntriesPerStream <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(maxEntriesPerStream), "The per-stream log entry limit must be positive.");
    }

    _maxEntries = maxEntries;
    _maxEntriesPerStream = maxEntriesPerStream;
    _maxAge = maxAge ?? DefaultMaxAge;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _redactor = redactor ?? new CaddyLogRedactor();
  }

  public bool TryWrite(CaddyLogWriteRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Stream);

    lock (_gate)
    {
      var timestamp = request.TimestampUtc ?? _timeProvider.GetUtcNow();
      var entry = new CaddyLogEntry(
          _nextSequence++,
          timestamp,
          request.Severity,
          request.Stream,
          request.AttributionKind,
          request.EntryKind,
          _redactor.Redact(request.RawMessage),
          request.DomainKey,
          request.SourceRegistrationId,
          request.SourceInstanceId,
          request.Operation);

      _entries.Add(entry);
      PruneLocked(_timeProvider.GetUtcNow());
    }

    return true;
  }

  public CaddyLogQueryResult Query(CaddyLogQuery query)
  {
    ArgumentNullException.ThrowIfNull(query);
    ArgumentNullException.ThrowIfNull(query.Stream);

    lock (_gate)
    {
      PruneLocked(_timeProvider.GetUtcNow());

      var matching = _entries
          .Where(entry => StreamEquals(entry.Stream, query.Stream))
          .Where(entry => query.AfterSequence is null || entry.SequenceNumber > query.AfterSequence.Value)
          .Where(entry => query.MinimumSeverity is null || entry.Severity >= query.MinimumSeverity.Value)
          .Where(entry => query.SinceUtc is null || entry.TimestampUtc >= query.SinceUtc.Value)
          .Where(entry => query.UntilUtc is null || entry.TimestampUtc <= query.UntilUtc.Value)
          .OrderBy(static entry => entry.SequenceNumber)
          .ToArray();

      var hasMoreBefore = query.AfterSequence is null && matching.Length > query.Limit;
      var entries = query.AfterSequence is null
          ? (hasMoreBefore ? matching[^query.Limit..] : matching)
          : matching.Take(query.Limit).ToArray();
      var nextSequence = entries.Length == 0 ? query.AfterSequence : entries[^1].SequenceNumber;
      var firstRetainedSequence = _entries.Count == 0 ? _nextSequence : _entries[0].SequenceNumber;
      var hasGap = query.AfterSequence is not null
          && query.AfterSequence.Value < firstRetainedSequence - 1
          && _firstDroppedSequence > 0;

      return new CaddyLogQueryResult(
          entries,
          nextSequence,
          hasGap,
          hasMoreBefore,
          hasGap || _firstDroppedSequence > 0);
    }
  }

  private void PruneLocked(DateTimeOffset now)
  {
    if (_maxAge > TimeSpan.Zero)
    {
      var cutoff = now - _maxAge;
      RemoveWhereLocked(entry => entry.TimestampUtc < cutoff);
    }

    while (_entries.Count > _maxEntries)
    {
      DropAtLocked(0);
    }

    var countsByStream = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var index = _entries.Count - 1; index >= 0; index--)
    {
      var streamId = _entries[index].Stream.StreamId;
      countsByStream.TryGetValue(streamId, out var count);
      count++;
      countsByStream[streamId] = count;
      if (count > _maxEntriesPerStream)
      {
        DropAtLocked(index);
      }
    }
  }

  private void RemoveWhereLocked(Func<CaddyLogEntry, bool> predicate)
  {
    for (var index = _entries.Count - 1; index >= 0; index--)
    {
      if (predicate(_entries[index]))
      {
        DropAtLocked(index);
      }
    }
  }

  private void DropAtLocked(int index)
  {
    _firstDroppedSequence = Math.Max(_firstDroppedSequence, _entries[index].SequenceNumber);
    _entries.RemoveAt(index);
  }

  private static bool StreamEquals(LogStreamIdentity left, LogStreamIdentity right)
  {
    return string.Equals(left.StreamId, right.StreamId, StringComparison.Ordinal)
        && string.Equals(left.Channel, right.Channel, StringComparison.Ordinal)
        && string.Equals(left.DomainKey, right.DomainKey, StringComparison.Ordinal);
  }
}
