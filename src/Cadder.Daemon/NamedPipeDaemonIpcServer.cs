using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class NamedPipeDaemonIpcServer : IDaemonIpcServer
{
  private readonly ICadderIpcEndpoint _endpoint;
  private readonly string _pipeName;
  private readonly ConcurrentDictionary<Task, byte> _clientTasks = [];
  private CancellationTokenSource? _stopping;
  private Task? _acceptLoop;

  public NamedPipeDaemonIpcServer(ICadderIpcEndpoint endpoint, string? pipeName = null)
  {
    _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    _pipeName = pipeName ?? CadderIpcPipeNames.CreatePerUserName();
  }

  public ValueTask StartAsync(CancellationToken cancellationToken = default)
  {
    if (_acceptLoop is not null)
    {
      return ValueTask.CompletedTask;
    }

    _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _acceptLoop = AcceptClientsAsync(_stopping.Token);
    return ValueTask.CompletedTask;
  }

  public async ValueTask StopAsync(CancellationToken cancellationToken = default)
  {
    if (_stopping is null)
    {
      return;
    }

    await _stopping.CancelAsync().ConfigureAwait(false);

    if (_acceptLoop is not null)
    {
      await _acceptLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    var clientTasks = _clientTasks.Keys.ToArray();
    if (clientTasks.Length > 0)
    {
      await Task.WhenAll(clientTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    _stopping.Dispose();
    _stopping = null;
    _acceptLoop = null;
  }

  private async Task AcceptClientsAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      var pipe = CreateServerStream();

      try
      {
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        await pipe.DisposeAsync().ConfigureAwait(false);
        return;
      }

      var clientPipe = pipe;
      var clientTask = HandleClientAsync(clientPipe, cancellationToken);
      _clientTasks.TryAdd(clientTask, 0);
      _ = clientTask.ContinueWith(
          task => _clientTasks.TryRemove(task, out _),
          CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
    }
  }

  private NamedPipeServerStream CreateServerStream()
  {
    return new NamedPipeServerStream(
        _pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);
  }

  private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken serverStoppingToken)
  {
    HashSet<EntrypointRegistration> ownedRegistrations = [];

    try
    {
      var connectionId = Guid.NewGuid().ToString("N");

      await using (pipe.ConfigureAwait(false))
      using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
      await using (var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true))
      {
        while (!serverStoppingToken.IsCancellationRequested && pipe.IsConnected)
        {
          CadderIpcMessage? message;
          try
          {
            message = await CadderIpcProtocol.ReadAsync(reader, serverStoppingToken).ConfigureAwait(false);
          }
          catch (IOException)
          {
            break;
          }
          catch (JsonException)
          {
            break;
          }
          catch (OperationCanceledException) when (serverStoppingToken.IsCancellationRequested)
          {
            break;
          }

          if (message is null)
          {
            break;
          }

          try
          {
            await DispatchAsync(
                message,
                writer,
                ownedRegistrations,
                connectionId,
                serverStoppingToken).ConfigureAwait(false);
          }
          catch (IOException)
          {
            break;
          }
          catch (JsonException)
          {
            break;
          }
          catch (InvalidOperationException)
          {
            break;
          }
        }
      }
    }
    finally
    {
      await RemoveOwnedRegistrationsAsync(ownedRegistrations, CancellationToken.None).ConfigureAwait(false);
    }
  }

  private async ValueTask DispatchAsync(
      CadderIpcMessage message,
      TextWriter writer,
      HashSet<EntrypointRegistration> ownedRegistrations,
      string connectionId,
      CancellationToken cancellationToken)
  {
    switch (message.Type)
    {
      case CadderIpcMessageTypes.RegisterEntrypointRequest:
        var registerRequest = CadderIpcProtocol.ReadPayload<RegisterEntrypointRequest>(message);
        var serverOwnedRegistration = CreateServerOwnedRegistration(registerRequest.Registration, connectionId);
        var registerResponse = await _endpoint.RegisterAsync(
            registerRequest with { Registration = serverOwnedRegistration },
            cancellationToken)
            .ConfigureAwait(false);
        if (registerResponse.Accepted)
        {
          ownedRegistrations.Add(serverOwnedRegistration);
        }

        await CadderIpcProtocol.WriteAsync(
            writer,
            CadderIpcMessageTypes.RegisterEntrypointResponse,
            registerResponse,
            cancellationToken).ConfigureAwait(false);
        break;

      case CadderIpcMessageTypes.UnregisterEntrypointRequest:
        var unregisterRequest = CadderIpcProtocol.ReadPayload<UnregisterEntrypointRequest>(message);
        var unregisterResponse = await _endpoint.UnregisterAsync(unregisterRequest, cancellationToken)
            .ConfigureAwait(false);
        ownedRegistrations.RemoveWhere(
            registration => string.Equals(
                registration.RegistrationId,
                unregisterRequest.RegistrationId,
                StringComparison.Ordinal));
        await CadderIpcProtocol.WriteAsync(
            writer,
            CadderIpcMessageTypes.UnregisterEntrypointResponse,
            unregisterResponse,
            cancellationToken).ConfigureAwait(false);
        break;

      case CadderIpcMessageTypes.QueryGuiStateRequest:
        var queryRequest = CadderIpcProtocol.ReadPayload<QueryGuiStateRequest>(message);
        var queryResponse = await _endpoint.QueryStateAsync(queryRequest, cancellationToken)
            .ConfigureAwait(false);
        await CadderIpcProtocol.WriteAsync(
            writer,
            CadderIpcMessageTypes.QueryGuiStateResponse,
            queryResponse,
            cancellationToken).ConfigureAwait(false);
        break;

      default:
        throw new InvalidOperationException($"Unsupported IPC message type '{message.Type}'.");
    }
  }

  private static EntrypointRegistration CreateServerOwnedRegistration(
      EntrypointRegistration registration,
      string connectionId)
  {
    return registration with
    {
      RegistrationId = $"{registration.RegistrationId}-{connectionId}"
    };
  }

  private async ValueTask RemoveOwnedRegistrationsAsync(
      HashSet<EntrypointRegistration> registrations,
      CancellationToken cancellationToken)
  {
    foreach (var registration in registrations)
    {
      await _endpoint.UnregisterAsync(
          new UnregisterEntrypointRequest(
              Guid.NewGuid().ToString("N"),
              registration.RegistrationId,
              registration.EntrypointInstance.ShimSessionNonce),
          cancellationToken).ConfigureAwait(false);
    }
  }
}
