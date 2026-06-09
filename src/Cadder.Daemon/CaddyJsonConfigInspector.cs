using System.Text.Json.Nodes;

namespace Cadder.Daemon;

public sealed class CaddyJsonConfigInspector
{
  public AdaptedCaddyHost[] ExtractHosts(JsonNode? config)
  {
    if (config is null)
    {
      return [];
    }

    var hosts = new Dictionary<string, AdaptedCaddyHost>(StringComparer.Ordinal);
    foreach (var hostNode in EnumerateHostNodes(config))
    {
      if (hostNode.GetValueKind() != System.Text.Json.JsonValueKind.String)
      {
        continue;
      }

      var raw = hostNode.GetValue<string>();
      if (TryCanonicalizeHost(raw, out var canonical) && !hosts.ContainsKey(canonical))
      {
        hosts[canonical] = new AdaptedCaddyHost(raw, canonical);
      }
    }

    return [.. hosts.Values.OrderBy(static host => host.Canonical, StringComparer.Ordinal)];
  }

  public static bool TryCanonicalizeHost(string rawHost, out string canonical)
  {
    canonical = string.Empty;
    if (string.IsNullOrWhiteSpace(rawHost))
    {
      return false;
    }

    var host = rawHost.Trim().TrimEnd('.');
    if (host.Length == 0
        || host.Any(static ch => char.IsWhiteSpace(ch) || ch is '/' or ':' or '{' or '}'))
    {
      return false;
    }

    canonical = host.ToLowerInvariant();
    return true;
  }

  private static IEnumerable<JsonValue> EnumerateHostNodes(JsonNode node)
  {
    if (node is JsonObject jsonObject)
    {
      foreach (var pair in jsonObject)
      {
        if (string.Equals(pair.Key, "host", StringComparison.Ordinal)
            && pair.Value is JsonArray hostArray)
        {
          foreach (var hostNode in hostArray.OfType<JsonValue>())
          {
            yield return hostNode;
          }
        }

        if (pair.Value is not null)
        {
          foreach (var nestedHost in EnumerateHostNodes(pair.Value))
          {
            yield return nestedHost;
          }
        }
      }
    }
    else if (node is JsonArray jsonArray)
    {
      foreach (var child in jsonArray)
      {
        if (child is null)
        {
          continue;
        }

        foreach (var nestedHost in EnumerateHostNodes(child))
        {
          yield return nestedHost;
        }
      }
    }
  }
}
