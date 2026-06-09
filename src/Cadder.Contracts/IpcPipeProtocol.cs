using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadder.Contracts;

public static class CadderIpcMessageTypes
{
  public const string RegisterEntrypointRequest = "register-entrypoint-request";
  public const string RegisterEntrypointResponse = "register-entrypoint-response";
  public const string UnregisterEntrypointRequest = "unregister-entrypoint-request";
  public const string UnregisterEntrypointResponse = "unregister-entrypoint-response";
  public const string UpdateEntrypointRequest = "update-entrypoint-request";
  public const string UpdateEntrypointResponse = "update-entrypoint-response";
  public const string ListEntrypointsRequest = "list-entrypoints-request";
  public const string ListEntrypointsResponse = "list-entrypoints-response";
  public const string ToggleEntrypointRequest = "toggle-entrypoint-request";
  public const string ToggleEntrypointResponse = "toggle-entrypoint-response";
  public const string HeartbeatEntrypointRequest = "heartbeat-entrypoint-request";
  public const string HeartbeatEntrypointResponse = "heartbeat-entrypoint-response";
  public const string QueryGuiStateRequest = "query-gui-state-request";
  public const string QueryGuiStateResponse = "query-gui-state-response";
  public const string SubscribeGuiStateRequest = "subscribe-gui-state-request";
  public const string GuiStateChangedEvent = "gui-state-changed-event";
}

public sealed record CadderIpcMessage(
    string Type,
    JsonElement Payload);

public static class CadderIpcPipeNames
{
  public static string CreatePerUserName(string appKey = "Cadder.Ipc")
  {
    if (string.IsNullOrWhiteSpace(appKey))
    {
      throw new ArgumentException("An app key is required.", nameof(appKey));
    }

    var userIdentity = $"{Environment.UserDomainName}\\{Environment.UserName}";
    var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity)))[..16];

    return $"{appKey}.{userHash}";
  }
}

public static class CadderIpcJson
{
  public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}

public static class CadderIpcProtocol
{
  public static async ValueTask WriteAsync<TPayload>(
      TextWriter writer,
      string type,
      TPayload payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(writer);
    ArgumentException.ThrowIfNullOrWhiteSpace(type);
    ArgumentNullException.ThrowIfNull(payload);

    var envelope = JsonSerializer.Serialize(
        new CadderIpcWriteEnvelope<TPayload>(type, payload),
        CadderIpcJson.SerializerOptions);

    await writer.WriteLineAsync(envelope).ConfigureAwait(false);
    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  public static async ValueTask<CadderIpcMessage?> ReadAsync(
      TextReader reader,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(reader);

    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    if (line is null)
    {
      return null;
    }

    return JsonSerializer.Deserialize<CadderIpcMessage>(line, CadderIpcJson.SerializerOptions)
        ?? throw new InvalidOperationException("The IPC message envelope could not be deserialized.");
  }

  public static TPayload ReadPayload<TPayload>(CadderIpcMessage message)
  {
    ArgumentNullException.ThrowIfNull(message);

    return message.Payload.Deserialize<TPayload>(CadderIpcJson.SerializerOptions)
        ?? throw new InvalidOperationException($"The IPC payload for '{message.Type}' could not be deserialized.");
  }

  private sealed record CadderIpcWriteEnvelope<TPayload>(
      string Type,
      TPayload Payload);
}
