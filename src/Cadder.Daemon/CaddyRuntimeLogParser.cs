using System.Text.Json;
using Cadder.Contracts;

namespace Cadder.Daemon;

public static class CaddyRuntimeLogParser
{
  public static readonly LogStreamIdentity RuntimeControlStream = new("runtime-control", null, "caddy-control");

  public static CaddyLogWriteRequest ParseRuntimeLine(
      string line,
      string channel,
      string sessionId,
      DateTimeOffset observedAtUtc)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(channel);
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    var timestampUtc = observedAtUtc;
    var severity = string.Equals(channel, "stderr", StringComparison.Ordinal)
        ? CaddyLogSeverity.Error
        : CaddyLogSeverity.Info;
    string? domainKey = null;

    if (!string.IsNullOrWhiteSpace(line))
    {
      TryReadJsonLogLine(line, ref timestampUtc, ref severity, out domainKey);
    }

    var stream = domainKey is null
        ? new LogStreamIdentity("runtime", null, channel)
        : new LogStreamIdentity($"domain-{domainKey}", domainKey, "caddy");

    return new CaddyLogWriteRequest(
        stream,
        severity,
        domainKey is null ? CaddyLogAttributionKind.Runtime : CaddyLogAttributionKind.Domain,
        CaddyLogEntryKind.Normal,
        string.IsNullOrWhiteSpace(line) ? "<empty runtime log line>" : line,
        timestampUtc,
        domainKey,
        SourceInstanceId: sessionId,
        Operation: "run");
  }

  public static CaddyLogWriteRequest RuntimeControl(
      CaddyLogSeverity severity,
      string operation,
      string message,
      DateTimeOffset timestampUtc)
  {
    return new CaddyLogWriteRequest(
        RuntimeControlStream,
        severity,
        CaddyLogAttributionKind.RuntimeControl,
        CaddyLogEntryKind.Lifecycle,
        message,
        timestampUtc,
        Operation: operation);
  }

  private static void TryReadJsonLogLine(
      string line,
      ref DateTimeOffset timestampUtc,
      ref CaddyLogSeverity severity,
      out string? domainKey)
  {
    domainKey = null;

    try
    {
      using var document = JsonDocument.Parse(line);
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object)
      {
        return;
      }

      if (root.TryGetProperty("ts", out var timestampElement)
          && TryReadTimestamp(timestampElement, out var parsedTimestamp))
      {
        timestampUtc = parsedTimestamp;
      }

      if (root.TryGetProperty("level", out var levelElement)
          && levelElement.ValueKind == JsonValueKind.String)
      {
        severity = ParseSeverity(levelElement.GetString(), severity);
      }

      domainKey = ReadDomainKey(root);
    }
    catch (JsonException)
    {
    }
  }

  private static bool TryReadTimestamp(JsonElement element, out DateTimeOffset timestampUtc)
  {
    if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var unixSeconds))
    {
      var seconds = Math.Truncate(unixSeconds);
      var fraction = unixSeconds - seconds;
      timestampUtc = DateTimeOffset.FromUnixTimeSeconds((long)seconds)
          .AddTicks((long)(fraction * TimeSpan.TicksPerSecond));
      return true;
    }

    if (element.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(element.GetString(), out var parsed))
    {
      timestampUtc = parsed.ToUniversalTime();
      return true;
    }

    timestampUtc = default;
    return false;
  }

  private static CaddyLogSeverity ParseSeverity(string? level, CaddyLogSeverity fallback)
  {
    return level?.Trim().ToLowerInvariant() switch
    {
      "trace" => CaddyLogSeverity.Trace,
      "debug" => CaddyLogSeverity.Debug,
      "info" => CaddyLogSeverity.Info,
      "warn" or "warning" => CaddyLogSeverity.Warn,
      "error" => CaddyLogSeverity.Error,
      "fatal" or "panic" => CaddyLogSeverity.Fatal,
      _ => fallback
    };
  }

  private static string? ReadDomainKey(JsonElement root)
  {
    if (TryReadHost(root, "host", out var host)
        || (root.TryGetProperty("request", out var request) && TryReadRequestHost(request, out host)))
    {
      return CaddyJsonConfigInspector.TryCanonicalizeHost(host, out var canonical)
          ? canonical
          : null;
    }

    return null;
  }

  private static bool TryReadRequestHost(JsonElement request, out string host)
  {
    if (TryReadHost(request, "host", out host))
    {
      return true;
    }

    if (request.TryGetProperty("headers", out var headers))
    {
      return TryReadHost(headers, "Host", out host)
          || TryReadHost(headers, "host", out host);
    }

    host = string.Empty;
    return false;
  }

  private static bool TryReadHost(JsonElement element, string propertyName, out string host)
  {
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(propertyName, out var hostElement))
    {
      host = string.Empty;
      return false;
    }

    if (hostElement.ValueKind == JsonValueKind.String)
    {
      host = hostElement.GetString() ?? string.Empty;
      return !string.IsNullOrWhiteSpace(host);
    }

    if (hostElement.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in hostElement.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(item.GetString()))
        {
          host = item.GetString()!;
          return true;
        }
      }
    }

    host = string.Empty;
    return false;
  }
}
