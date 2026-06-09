using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class ProcessCaddyfileConfigAdapter : ICaddyfileConfigAdapter
{
  private const string DefaultAdaptCommand = "caddy-real";
  private readonly string _adaptCommand;

  public ProcessCaddyfileConfigAdapter(string? adaptCommand = null)
  {
    _adaptCommand = string.IsNullOrWhiteSpace(adaptCommand)
        ? Environment.GetEnvironmentVariable("CADDER_CADDY_ADAPT_COMMAND") ?? DefaultAdaptCommand
        : adaptCommand;
  }

  public async ValueTask<CaddyfileAdaptResult> AdaptAsync(
      SourcePath sourceConfigPath,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(sourceConfigPath);
    cancellationToken.ThrowIfCancellationRequested();

    var path = sourceConfigPath.Canonical ?? sourceConfigPath.Raw;
    if (string.IsNullOrWhiteSpace(path))
    {
      return Failure(sourceConfigPath, "config-path-missing", "The registration does not include a Caddyfile path.");
    }

    if (!File.Exists(path))
    {
      return Failure(sourceConfigPath, "config-not-found", $"The Caddyfile '{path}' does not exist.");
    }

    var startInfo = new ProcessStartInfo
    {
      FileName = _adaptCommand,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("adapt");
    startInfo.ArgumentList.Add("--config");
    startInfo.ArgumentList.Add(path);
    startInfo.ArgumentList.Add("--adapter");
    startInfo.ArgumentList.Add("caddyfile");

    try
    {
      using var process = Process.Start(startInfo);
      if (process is null)
      {
        return Failure(sourceConfigPath, "adapt-start-failed", $"Could not start '{_adaptCommand}'.");
      }

      var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
      var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
      var stdout = await stdoutTask.ConfigureAwait(false);
      var stderr = await stderrTask.ConfigureAwait(false);

      if (process.ExitCode != 0)
      {
        return Failure(
            sourceConfigPath,
            "adapt-failed",
            $"Caddy adapt failed for '{path}': {NormalizeMessage(stderr)}");
      }

      var config = JsonNode.Parse(stdout)?.AsObject();
      if (config is null)
      {
        return Failure(sourceConfigPath, "adapt-invalid-json", $"Caddy adapt did not return a JSON object for '{path}'.");
      }

      return CaddyfileAdaptResult.Success(sourceConfigPath, config);
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
      return Failure(
          sourceConfigPath,
          "adapt-command-unavailable",
          $"Could not run '{_adaptCommand} adapt': {ex.Message}");
    }
    catch (System.Text.Json.JsonException ex)
    {
      return Failure(
          sourceConfigPath,
          "adapt-invalid-json",
          $"Caddy adapt returned invalid JSON for '{path}': {ex.Message}");
    }
  }

  private static CaddyfileAdaptResult Failure(
      SourcePath sourceConfigPath,
      string code,
      string message)
  {
    return CaddyfileAdaptResult.Failure(
        sourceConfigPath,
        [
            new CaddyConfigDiagnostic(
                code,
                message,
                null,
                [sourceConfigPath.Canonical ?? sourceConfigPath.Raw])
        ]);
  }

  private static string NormalizeMessage(string message)
  {
    var normalized = message.Trim();
    return normalized.Length == 0 ? "<no stderr>" : normalized;
  }
}
