using System.Text.Json;
using System.Text.Json.Nodes;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed record CaddyConfigComposition(
    bool Succeeded,
    string Content,
    CaddyConfigDiagnostic[] Diagnostics)
{
  public static CaddyConfigComposition Success(string content)
  {
    return new CaddyConfigComposition(true, content, []);
  }

  public static CaddyConfigComposition Failure(CaddyConfigDiagnostic[] diagnostics)
  {
    return new CaddyConfigComposition(false, string.Empty, diagnostics);
  }
}

public sealed class CaddyJsonConfigComposer
{
  private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
  private readonly CaddyJsonConfigInspector _inspector;

  public CaddyJsonConfigComposer(CaddyJsonConfigInspector? inspector = null)
  {
    _inspector = inspector ?? new CaddyJsonConfigInspector();
  }

  public CaddyConfigComposition Compose(
      IReadOnlyList<EntrypointRegistration> registrations,
      IReadOnlyDictionary<string, CaddyfileAdaptResult> adaptResults)
  {
    ArgumentNullException.ThrowIfNull(registrations);
    ArgumentNullException.ThrowIfNull(adaptResults);

    var diagnostics = new List<CaddyConfigDiagnostic>();
    foreach (var registration in ActiveRegistrations(registrations))
    {
      if (!adaptResults.TryGetValue(registration.RegistrationId, out var adaptResult))
      {
        diagnostics.Add(new CaddyConfigDiagnostic(
            "adapt-result-missing",
            $"No adapted Caddy JSON is available for registration '{registration.RegistrationId}'.",
            null,
            [SourcePathFor(registration)]));
        continue;
      }

      diagnostics.AddRange(adaptResult.Diagnostics);
    }

    diagnostics.AddRange(DetectConflicts(registrations, adaptResults));
    if (diagnostics.Count > 0)
    {
      return CaddyConfigComposition.Failure([.. diagnostics]);
    }

    var outputRoutes = new JsonArray();
    var outputListen = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var registration in ActiveRegistrations(registrations))
    {
      var adaptResult = adaptResults[registration.RegistrationId];
      if (adaptResult.Config is null)
      {
        continue;
      }

      var activeDomainKeys = ActiveDomainKeys(registration);
      var hasRegisteredDomainState = registration.RegisteredDomains.Length > 0;
      foreach (var server in EnumerateServers(adaptResult.Config))
      {
        foreach (var listen in ReadStringArray(server["listen"]))
        {
          outputListen.Add(listen);
        }

        if (server["routes"] is not JsonArray routes)
        {
          continue;
        }

        foreach (var routeNode in routes)
        {
          var filteredRoute = FilterRoute(routeNode, activeDomainKeys, hasRegisteredDomainState);
          if (filteredRoute is not null)
          {
            outputRoutes.Add(filteredRoute);
          }
        }
      }
    }

    if (outputRoutes.Count == 0)
    {
      return CaddyConfigComposition.Success("{}");
    }

    var output = new JsonObject
    {
      ["apps"] = new JsonObject
      {
        ["http"] = new JsonObject
        {
          ["servers"] = new JsonObject
          {
            ["srv0"] = new JsonObject
            {
              ["listen"] = new JsonArray([.. outputListen.Select(static listen => JsonValue.Create(listen))]),
              ["routes"] = outputRoutes
            }
          }
        }
      }
    };

    return CaddyConfigComposition.Success(output.ToJsonString(s_jsonOptions));
  }

  private static IEnumerable<EntrypointRegistration> ActiveRegistrations(
      IReadOnlyList<EntrypointRegistration> registrations)
  {
    return registrations
        .Where(static registration => registration.ActivationState is not ActivationState.Inactive and not ActivationState.Faulted)
        .OrderBy(static registration => registration.RegistrationId, StringComparer.Ordinal);
  }

  private CaddyConfigDiagnostic[] DetectConflicts(
      IReadOnlyList<EntrypointRegistration> registrations,
      IReadOnlyDictionary<string, CaddyfileAdaptResult> adaptResults)
  {
    var domainSources = new Dictionary<string, List<EntrypointRegistration>>(StringComparer.Ordinal);
    foreach (var registration in ActiveRegistrations(registrations))
    {
      foreach (var domainKey in EnabledHostKeys(registration, adaptResults))
      {
        if (!domainSources.TryGetValue(domainKey, out var sources))
        {
          sources = [];
          domainSources[domainKey] = sources;
        }

        sources.Add(registration);
      }
    }

    return [.. domainSources
        .Where(static pair => pair.Value.Select(static registration => registration.RegistrationId).Distinct(StringComparer.Ordinal).Count() > 1)
        .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new CaddyConfigDiagnostic(
            "domain-conflict",
            $"Domain '{pair.Key}' is registered by multiple entrypoint instances.",
            pair.Key,
            [.. pair.Value.Select(SourcePathFor).Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal)]))];
  }

  private IEnumerable<string> EnabledHostKeys(
      EntrypointRegistration registration,
      IReadOnlyDictionary<string, CaddyfileAdaptResult> adaptResults)
  {
    if (!adaptResults.TryGetValue(registration.RegistrationId, out var adaptResult)
        || adaptResult.Config is null)
    {
      return [];
    }

    var adaptedHostKeys = _inspector.ExtractHosts(adaptResult.Config).Select(static host => host.Canonical).ToArray();
    if (registration.RegisteredDomains.Length == 0)
    {
      return adaptedHostKeys;
    }

    var activeDomainKeys = ActiveDomainKeys(registration);
    return adaptedHostKeys.Where(activeDomainKeys.Contains);
  }

  private JsonNode? FilterRoute(
      JsonNode? routeNode,
      HashSet<string> activeDomainKeys,
      bool hasRegisteredDomainState)
  {
    if (routeNode is null)
    {
      return null;
    }

    var route = routeNode.DeepClone().AsObject();
    if (route["match"] is not JsonArray matchArray)
    {
      return null;
    }

    var filteredMatches = new JsonArray();
    foreach (var matchNode in matchArray)
    {
      if (matchNode is not JsonObject match)
      {
        continue;
      }

      var clonedMatch = match.DeepClone().AsObject();
      if (clonedMatch["host"] is not JsonArray hostArray)
      {
        continue;
      }

      var filteredHosts = ReadStringArray(hostArray)
          .Where(host => !hasRegisteredDomainState
              || (CaddyJsonConfigInspector.TryCanonicalizeHost(host, out var canonical) && activeDomainKeys.Contains(canonical)))
          .Select(static host => JsonValue.Create(host))
          .ToArray();
      if (filteredHosts.Length == 0)
      {
        continue;
      }

      clonedMatch["host"] = new JsonArray(filteredHosts);
      filteredMatches.Add(clonedMatch);
    }

    if (filteredMatches.Count == 0)
    {
      return null;
    }

    route["match"] = filteredMatches;
    return route;
  }

  private static IEnumerable<JsonObject> EnumerateServers(JsonObject config)
  {
    var servers = config["apps"]?["http"]?["servers"] as JsonObject;
    if (servers is null)
    {
      yield break;
    }

    foreach (var server in servers.Select(static pair => pair.Value).OfType<JsonObject>())
    {
      yield return server;
    }
  }

  private static string[] ReadStringArray(JsonNode? node)
  {
    return node is JsonArray array
        ? [.. array.OfType<JsonValue>()
            .Where(static value => value.GetValueKind() == JsonValueKind.String)
            .Select(static value => value.GetValue<string>())]
        : [];
  }

  private static HashSet<string> ActiveDomainKeys(EntrypointRegistration registration)
  {
    return [.. registration.RegisteredDomains
        .Where(static domain => domain.ActivationState is not ActivationState.Inactive and not ActivationState.Faulted)
        .Select(static domain => domain.Name.Canonical ?? domain.Name.Raw.ToLowerInvariant())];
  }

  private static string SourcePathFor(EntrypointRegistration registration)
  {
    return registration.SourceConfigPath.Canonical ?? registration.SourceConfigPath.Raw;
  }
}
