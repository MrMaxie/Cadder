using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class InMemoryGuiStateChangeBroadcaster : IGuiStateChangeBroadcaster
{
  private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = [];
  private readonly SemaphoreSlim _gate = new(1, 1);
  private long _nextSequenceNumber;

  public async IAsyncEnumerable<GuiStateChangedEvent> SubscribeAsync(
      string requestId,
      Func<CancellationToken, ValueTask<GuiStateSnapshot>> initialSnapshotFactory,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
    ArgumentNullException.ThrowIfNull(initialSnapshotFactory);

    var subscriberId = Guid.Empty;
    Channel<GuiStateChangedEvent>? channel = null;
    long initialSequenceNumber = 0;
    GuiStateSnapshot? initialSnapshot = null;
    var subscribed = false;

    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      subscriberId = Guid.NewGuid();
      initialSequenceNumber = Interlocked.Increment(ref _nextSequenceNumber);
      channel = Channel.CreateBounded<GuiStateChangedEvent>(new BoundedChannelOptions(128)
      {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
      });
      var subscriber = new Subscriber(requestId, channel);
      _subscribers[subscriberId] = subscriber;
      subscribed = true;
      initialSnapshot = await initialSnapshotFactory(cancellationToken).ConfigureAwait(false);
    }
    catch
    {
      if (subscribed)
      {
        _subscribers.TryRemove(subscriberId, out _);
        channel?.Writer.TryComplete();
      }

      throw;
    }
    finally
    {
      _gate.Release();
    }

    try
    {
      yield return new GuiStateChangedEvent(
          requestId,
          initialSequenceNumber,
          GuiStateChangeKind.Snapshot,
          initialSnapshot!,
          null);

      await foreach (var change in channel!.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
      {
        yield return change;
      }
    }
    finally
    {
      if (_subscribers.TryRemove(subscriberId, out var removed))
      {
        removed.Channel.Writer.TryComplete();
      }
    }
  }

  public ValueTask PublishAsync(
      GuiStateChangeKind changeKind,
      GuiStateSnapshot snapshot,
      string? registrationId = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    cancellationToken.ThrowIfCancellationRequested();

    return PublishCoreAsync(changeKind, snapshot, registrationId, cancellationToken);
  }

  private async ValueTask PublishCoreAsync(
      GuiStateChangeKind changeKind,
      GuiStateSnapshot snapshot,
      string? registrationId,
      CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var sequenceNumber = Interlocked.Increment(ref _nextSequenceNumber);
      foreach (var subscriber in _subscribers.Values)
      {
        subscriber.Channel.Writer.TryWrite(new GuiStateChangedEvent(
            subscriber.RequestId,
            sequenceNumber,
            changeKind,
            snapshot,
            registrationId));
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private sealed record Subscriber(
      string RequestId,
      Channel<GuiStateChangedEvent> Channel);
}
