using System.Text.Json.Nodes;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed record AdaptedCaddyHost(
    string Raw,
    string Canonical);

public sealed record CaddyfileAdaptResult(
    bool Succeeded,
    SourcePath SourceConfigPath,
    JsonObject? Config,
    CaddyConfigDiagnostic[] Diagnostics)
{
  public static CaddyfileAdaptResult Success(SourcePath sourceConfigPath, JsonObject config)
  {
    return new CaddyfileAdaptResult(true, sourceConfigPath, config, []);
  }

  public static CaddyfileAdaptResult Failure(SourcePath sourceConfigPath, CaddyConfigDiagnostic[] diagnostics)
  {
    return new CaddyfileAdaptResult(false, sourceConfigPath, null, diagnostics);
  }
}

public interface ICaddyfileConfigAdapter
{
  ValueTask<CaddyfileAdaptResult> AdaptAsync(
      SourcePath sourceConfigPath,
      CancellationToken cancellationToken = default);
}
