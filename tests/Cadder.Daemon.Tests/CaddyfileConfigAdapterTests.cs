using System.Text.Json.Nodes;
using Cadder.Contracts;

namespace Cadder.Daemon.Tests;

public sealed class CaddyfileConfigAdapterTests
{
  [Fact]
  public void ExtractHosts_WithAdaptedCaddyJson_ReturnsCanonicalHostMatchers()
  {
    var config = JsonNode.Parse(
        """
        {
          "apps": {
            "http": {
              "servers": {
                "srv0": {
                  "routes": [
                    {
                      "match": [
                        {
                          "host": [
                            "API.Example.Localhost",
                            "app.example.localhost"
                          ]
                        }
                      ],
                      "handle": [
                        {
                          "handler": "static_response",
                          "body": "ok"
                        }
                      ],
                      "terminal": true
                    }
                  ]
                }
              }
            }
          }
        }
        """)!.AsObject();
    var inspector = new CaddyJsonConfigInspector();

    var hosts = inspector.ExtractHosts(config);

    Assert.Equal(["api.example.localhost", "app.example.localhost"], hosts.Select(static host => host.Canonical).ToArray());
  }

  [Fact]
  public async Task ProcessAdapter_WithLocalSmarketingStyleReverseProxy_UsesCaddyAdaptAndExtractsExpectedDomains()
  {
    if (GetCommandPath("caddy-real") is null)
    {
      return;
    }

    var sourcePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SmarketingReverseProxy.Caddyfile");

    var adapter = new ProcessCaddyfileConfigAdapter("caddy-real");
    var inspector = new CaddyJsonConfigInspector();

    var result = await adapter.AdaptAsync(new SourcePath(sourcePath, sourcePath));

    Assert.True(result.Succeeded, result.Diagnostics.FirstOrDefault()?.Message);
    Assert.NotNull(result.Config);
    Assert.Equal(
        [
            "api.smarketing.localhost",
            "app.smarketing.localhost",
            "mailbox.smarketing.localhost",
            "storage.smarketing.localhost"
        ],
        inspector.ExtractHosts(result.Config).Select(static host => host.Canonical).OrderBy(static host => host, StringComparer.Ordinal).ToArray());
  }

  [Fact]
  public async Task ProcessRuntimeAdapter_WithEmptyJsonConfig_ValidatesThroughRealCaddy()
  {
    if (GetCommandPath("caddy-real") is null)
    {
      return;
    }

    var adapter = new ProcessRealCaddyRuntimeAdapter("caddy-real");

    var result = await adapter.ValidateConfigAsync(new CaddyRuntimeConfig("{}"));

    Assert.True(result.Succeeded, result.Diagnostics.FirstOrDefault()?.Message ?? result.Message);
  }

  private static string? GetCommandPath(string command)
  {
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var candidate = Path.Combine(directory, $"{command}.exe");
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    return null;
  }
}
