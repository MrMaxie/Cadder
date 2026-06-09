using System.Threading.Channels;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class BoundedCaddyLogSink : ICaddyLogSink, IAsyncDisposable
{
  private readonly ICaddyLogSink _inner;
  private readonly Channel<CaddyLogWriteRequest> _channel;
  private readonly CancellationTokenSource _stopping = new();
  private readonly Task _consumerTask;

  public BoundedCaddyLogSink(ICaddyLogSink inner, int capacity = 1_024)
  {
    if (capacity <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(capacity), "The log sink capacity must be positive.");
    }

    _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    _channel = Channel.CreateBounded<CaddyLogWriteRequest>(new BoundedChannelOptions(capacity)
    {
      FullMode = BoundedChannelFullMode.DropWrite,
      SingleReader = true,
      SingleWriter = false
    });
    _consumerTask = ConsumeAsync(_stopping.Token);
  }

  public bool TryWrite(CaddyLogWriteRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    if (_channel.Writer.TryWrite(request))
    {
      return true;
    }

    _inner.TryWrite(new CaddyLogWriteRequest(
        CaddyRuntimeLogParser.RuntimeControlStream,
        CaddyLogSeverity.Warn,
        CaddyLogAttributionKind.RuntimeControl,
        CaddyLogEntryKind.IngestionOverflow,
        $"Caddy log ingestion queue overflowed while writing stream '{request.Stream.StreamId}'.",
        Operation: request.Operation));
    return false;
  }

  public async ValueTask DisposeAsync()
  {
    await _stopping.CancelAsync().ConfigureAwait(false);
    _channel.Writer.TryComplete();

    try
    {
      await _consumerTask.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
    }

    _stopping.Dispose();
  }

  private async Task ConsumeAsync(CancellationToken cancellationToken)
  {
    try
    {
      while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
      {
        while (_channel.Reader.TryRead(out var request))
        {
          _inner.TryWrite(request);
        }
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      while (_channel.Reader.TryRead(out var request))
      {
        _inner.TryWrite(request);
      }
    }
  }
}
