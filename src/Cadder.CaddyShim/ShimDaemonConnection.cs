using System.IO.Pipes;
using System.Text;
using Cadder.Contracts;

namespace Cadder.CaddyShim;

public interface ICadderDaemonConnection : IAsyncDisposable
{
  ValueTask<RegisterEntrypointResponse> RegisterAsync(
      RegisterEntrypointRequest request,
      CancellationToken cancellationToken = default);

  ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
      UnregisterEntrypointRequest request,
      CancellationToken cancellationToken = default);
}

public interface ICadderDaemonConnector
{
  ValueTask<ICadderDaemonConnection> ConnectAsync(CancellationToken cancellationToken = default);
}

public sealed class NamedPipeCadderDaemonConnector : ICadderDaemonConnector
{
  private readonly string _pipeName;
  private readonly TimeSpan _connectTimeout;

  public NamedPipeCadderDaemonConnector(string? pipeName = null, TimeSpan? connectTimeout = null)
  {
    _pipeName = pipeName ?? CadderIpcPipeNames.CreatePerUserName();
    _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(500);
  }

  public async ValueTask<ICadderDaemonConnection> ConnectAsync(CancellationToken cancellationToken = default)
  {
    var pipe = new NamedPipeClientStream(
        ".",
        _pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);

    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(_connectTimeout);
      await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

      return new NamedPipeCadderDaemonConnection(pipe);
    }
    catch
    {
      await pipe.DisposeAsync().ConfigureAwait(false);
      throw;
    }
  }
}

public sealed class NamedPipeCadderDaemonConnection : ICadderDaemonConnection
{
  private readonly NamedPipeClientStream _pipe;
  private readonly StreamReader _reader;
  private readonly StreamWriter _writer;

  public NamedPipeCadderDaemonConnection(NamedPipeClientStream pipe)
  {
    _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
    _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
    _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true);
  }

  public async ValueTask<RegisterEntrypointResponse> RegisterAsync(
      RegisterEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    await CadderIpcProtocol.WriteAsync(
        _writer,
        CadderIpcMessageTypes.RegisterEntrypointRequest,
        request,
        cancellationToken).ConfigureAwait(false);

    var response = await ReadResponseAsync<RegisterEntrypointResponse>(
        CadderIpcMessageTypes.RegisterEntrypointResponse,
        cancellationToken).ConfigureAwait(false);

    return response;
  }

  public async ValueTask<UnregisterEntrypointResponse> UnregisterAsync(
      UnregisterEntrypointRequest request,
      CancellationToken cancellationToken = default)
  {
    await CadderIpcProtocol.WriteAsync(
        _writer,
        CadderIpcMessageTypes.UnregisterEntrypointRequest,
        request,
        cancellationToken).ConfigureAwait(false);

    var response = await ReadResponseAsync<UnregisterEntrypointResponse>(
        CadderIpcMessageTypes.UnregisterEntrypointResponse,
        cancellationToken).ConfigureAwait(false);

    return response;
  }

  public async ValueTask DisposeAsync()
  {
    await _writer.DisposeAsync().ConfigureAwait(false);
    _reader.Dispose();
    await _pipe.DisposeAsync().ConfigureAwait(false);
  }

  private async ValueTask<TResponse> ReadResponseAsync<TResponse>(
      string expectedType,
      CancellationToken cancellationToken)
  {
    var message = await CadderIpcProtocol.ReadAsync(_reader, cancellationToken).ConfigureAwait(false)
        ?? throw new IOException("The Cadder daemon closed the IPC connection before sending a response.");

    if (!string.Equals(message.Type, expectedType, StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
          $"Expected IPC response '{expectedType}' but received '{message.Type}'.");
    }

    return CadderIpcProtocol.ReadPayload<TResponse>(message);
  }
}
