using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Cadder.CaddyShim;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class NamedPipeDaemonIpcServerTests
{
  [Fact]
  public async Task ClientDisconnect_RemovesShimOwnedRegistration()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      var connection = await new NamedPipeCadderDaemonConnector(
          pipeName,
          TimeSpan.FromSeconds(2)).ConnectAsync();
      var registration = CreateRegistration("nonce-1");

      var response = await connection.RegisterAsync(new RegisterEntrypointRequest(
          "request-1",
          registration));

      Assert.True(response.Accepted);
      Assert.Single(await store.ListAsync());

      await connection.DisposeAsync();

      await WaitUntilAsync(async () => (await store.ListAsync()).Count == 0);
    }
    finally
    {
      await server.StopAsync();
    }
  }

  [Fact]
  public async Task InvalidClientMessage_AfterRegister_RemovesShimOwnedRegistration()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      await using var pipe = new NamedPipeClientStream(
          ".",
          pipeName,
          PipeDirection.InOut,
          PipeOptions.Asynchronous);
      using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      await pipe.ConnectAsync(connectTimeout.Token);
      using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
      await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);

      var registration = CreateRegistration("nonce-1");
      await writer.WriteLineAsync(JsonSerializer.Serialize(
          new
          {
            type = CadderIpcMessageTypes.RegisterEntrypointRequest,
            payload = new RegisterEntrypointRequest("request-1", registration)
          },
          CadderIpcJson.SerializerOptions));
      await writer.FlushAsync();

      Assert.NotNull(await reader.ReadLineAsync());
      Assert.Single(await store.ListAsync());

      await writer.WriteLineAsync("{\"type\":\"unknown\",\"payload\":{}}");
      await writer.FlushAsync();

      await WaitUntilAsync(async () => (await store.ListAsync()).Count == 0);
    }
    finally
    {
      await server.StopAsync();
    }
  }

  [Fact]
  public async Task MalformedJson_AfterRegister_RemovesRegistrationAndDoesNotPoisonStop()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      await using var pipe = await ConnectRawPipeAsync(pipeName);
      using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
      await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);

      await WriteRegisterMessageAsync(writer, CreateRegistration("nonce-1"));
      Assert.NotNull(await reader.ReadLineAsync());
      Assert.Single(await store.ListAsync());

      await writer.WriteLineAsync("{not-json");
      await writer.FlushAsync();

      await WaitUntilAsync(async () => (await store.ListAsync()).Count == 0);
    }
    finally
    {
      await server.StopAsync();
    }
  }

  [Fact]
  public async Task Disconnect_RemovesOnlyRegistrationsOwnedByThatPipeSession()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      var firstConnection = await new NamedPipeCadderDaemonConnector(
          pipeName,
          TimeSpan.FromSeconds(2)).ConnectAsync();
      var secondConnection = await new NamedPipeCadderDaemonConnector(
          pipeName,
          TimeSpan.FromSeconds(2)).ConnectAsync();

      var firstResponse = await firstConnection.RegisterAsync(new RegisterEntrypointRequest(
          "request-1",
          CreateRegistration("nonce-1")));
      var secondResponse = await secondConnection.RegisterAsync(new RegisterEntrypointRequest(
          "request-2",
          CreateRegistration("nonce-2")));

      Assert.Equal("shim-nonce-1", firstResponse.RegistrationId);
      Assert.Equal("shim-nonce-2", secondResponse.RegistrationId);
      Assert.Equal(2, (await store.ListAsync()).Count);

      await secondConnection.DisposeAsync();
      await WaitUntilAsync(async () => (await store.ListAsync()).Count == 1);
      Assert.Equal("shim-nonce-1", (await store.ListAsync())[0].RegistrationId);

      await firstConnection.DisposeAsync();
      await WaitUntilAsync(async () => (await store.ListAsync()).Count == 0);
    }
    finally
    {
      await server.StopAsync();
    }
  }

  [Fact]
  public async Task ClientMessages_CoverRegistrationApi()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      await using var pipe = await ConnectRawPipeAsync(pipeName);
      using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
      await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);

      var register = await SendMessageAsync<RegisterEntrypointRequest, RegisterEntrypointResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.RegisterEntrypointRequest,
          CadderIpcMessageTypes.RegisterEntrypointResponse,
          new RegisterEntrypointRequest("register-1", CreateRegistration("nonce-1")));
      Assert.True(register.Accepted);

      var update = await SendMessageAsync<UpdateEntrypointRequest, UpdateEntrypointResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.UpdateEntrypointRequest,
          CadderIpcMessageTypes.UpdateEntrypointResponse,
          new UpdateEntrypointRequest(
              "update-1",
              "shim-nonce-1",
              "nonce-1",
              null,
              new SourcePath("Caddyfile.alt", "D:\\Projects\\Sample\\Caddyfile.alt"),
              [],
              ActivationState.Registered,
              null));
      Assert.True(update.Accepted);
      Assert.Equal("Caddyfile.alt", update.Registration?.SourceConfigPath.Raw);

      var list = await SendMessageAsync<ListEntrypointsRequest, ListEntrypointsResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.ListEntrypointsRequest,
          CadderIpcMessageTypes.ListEntrypointsResponse,
          new ListEntrypointsRequest("list-1"));
      Assert.Single(list.Registrations);

      var toggle = await SendMessageAsync<ToggleEntrypointRequest, ToggleEntrypointResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.ToggleEntrypointRequest,
          CadderIpcMessageTypes.ToggleEntrypointResponse,
          new ToggleEntrypointRequest("toggle-1", "shim-nonce-1", "nonce-1", false));
      Assert.Equal(ActivationState.Inactive, toggle.Registration?.ActivationState);

      var heartbeat = await SendMessageAsync<HeartbeatEntrypointRequest, HeartbeatEntrypointResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.HeartbeatEntrypointRequest,
          CadderIpcMessageTypes.HeartbeatEntrypointResponse,
          new HeartbeatEntrypointRequest("heartbeat-1", "shim-nonce-1", "nonce-1"));
      Assert.True(heartbeat.Accepted);

      var unregister = await SendMessageAsync<UnregisterEntrypointRequest, UnregisterEntrypointResponse>(
          writer,
          reader,
          CadderIpcMessageTypes.UnregisterEntrypointRequest,
          CadderIpcMessageTypes.UnregisterEntrypointResponse,
          new UnregisterEntrypointRequest("unregister-1", "shim-nonce-1", "nonce-1"));
      Assert.True(unregister.Accepted);
      Assert.Empty(await store.ListAsync());
    }
    finally
    {
      await server.StopAsync();
    }
  }

  [Fact]
  public async Task SubscribeGuiStateRequest_StreamsInitialSnapshotAndChanges()
  {
    var pipeName = $"Cadder.Tests.{Guid.NewGuid():N}";
    var store = new InMemoryRegistrationStore();
    var endpoint = new CadderIpcEndpoint(store, new NoopRealCaddyRuntimeAdapter());
    var server = new NamedPipeDaemonIpcServer(endpoint, pipeName);

    await server.StartAsync();
    try
    {
      await using var subscriptionPipe = await ConnectRawPipeAsync(pipeName);
      using var subscriptionReader = new StreamReader(subscriptionPipe, Encoding.UTF8, leaveOpen: true);
      await using var subscriptionWriter = new StreamWriter(subscriptionPipe, Encoding.UTF8, leaveOpen: true);

      await WriteMessageAsync(
          subscriptionWriter,
          CadderIpcMessageTypes.SubscribeGuiStateRequest,
          new SubscribeGuiStateRequest("subscribe-1"));

      var initial = await ReadMessageAsync<GuiStateChangedEvent>(
          subscriptionReader,
          CadderIpcMessageTypes.GuiStateChangedEvent);
      Assert.Equal(GuiStateChangeKind.Snapshot, initial.ChangeKind);
      Assert.Empty(initial.Snapshot.Registrations);

      await using var connection = await new NamedPipeCadderDaemonConnector(
          pipeName,
          TimeSpan.FromSeconds(2)).ConnectAsync();
      var register = await connection.RegisterAsync(new RegisterEntrypointRequest(
          "register-1",
          CreateRegistration("nonce-1")));
      Assert.True(register.Accepted);

      var changed = await ReadMessageAsync<GuiStateChangedEvent>(
          subscriptionReader,
          CadderIpcMessageTypes.GuiStateChangedEvent);
      Assert.Equal(GuiStateChangeKind.RegistrationsChanged, changed.ChangeKind);
      Assert.Equal("shim-nonce-1", changed.RegistrationId);
      Assert.Single(changed.Snapshot.Registrations);
    }
    finally
    {
      await server.StopAsync();
    }
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
        new ShimRunMetadata("caddyfile", ["run", "--adapter", "caddyfile"]));
  }

  private static async ValueTask<NamedPipeClientStream> ConnectRawPipeAsync(string pipeName)
  {
    var pipe = new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous);
    using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await pipe.ConnectAsync(connectTimeout.Token);
    return pipe;
  }

  private static async Task WriteRegisterMessageAsync(
      TextWriter writer,
      EntrypointRegistration registration)
  {
    await WriteMessageAsync(
        writer,
        CadderIpcMessageTypes.RegisterEntrypointRequest,
        new RegisterEntrypointRequest("request-1", registration));
  }

  private static async Task<TResponse> SendMessageAsync<TRequest, TResponse>(
      TextWriter writer,
      TextReader reader,
      string requestType,
      string responseType,
      TRequest request)
  {
    await WriteMessageAsync(writer, requestType, request);
    return await ReadMessageAsync<TResponse>(reader, responseType);
  }

  private static async Task WriteMessageAsync<TRequest>(
      TextWriter writer,
      string type,
      TRequest request)
  {
    await writer.WriteLineAsync(JsonSerializer.Serialize(
        new
        {
          type,
          payload = request
        },
        CadderIpcJson.SerializerOptions));
    await writer.FlushAsync();
  }

  private static async Task<TResponse> ReadMessageAsync<TResponse>(
      TextReader reader,
      string expectedType)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var line = await reader.ReadLineAsync(timeout.Token);
    Assert.NotNull(line);

    var message = JsonSerializer.Deserialize<CadderIpcMessage>(line, CadderIpcJson.SerializerOptions);
    Assert.NotNull(message);
    Assert.Equal(expectedType, message.Type);
    return CadderIpcProtocol.ReadPayload<TResponse>(message);
  }

  private static async Task WaitUntilAsync(Func<Task<bool>> condition)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    while (!timeout.IsCancellationRequested)
    {
      if (await condition())
      {
        return;
      }

      await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    }

    throw new TimeoutException("The expected IPC condition was not reached.");
  }
}
