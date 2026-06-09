using System.ComponentModel;
using System.Diagnostics;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class ProcessRealCaddyRuntimeAdapter : IRealCaddyRuntimeAdapter
{
  private const string DefaultRuntimeCommand = "caddy-real";
  private readonly string _runtimeCommand;

  public ProcessRealCaddyRuntimeAdapter(string? runtimeCommand = null)
  {
    _runtimeCommand = string.IsNullOrWhiteSpace(runtimeCommand)
        ? Environment.GetEnvironmentVariable("CADDER_CADDY_REAL_COMMAND") ?? DefaultRuntimeCommand
        : runtimeCommand;
  }

  public async ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default)
  {
    var result = await RunCommandAsync(["version"], cancellationToken).ConfigureAwait(false);
    if (!result.Succeeded)
    {
      return new RealCaddyRuntimeState(RealCaddyRuntimeStatus.NotResolved, null, null);
    }

    return new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Resolved,
        new RealCaddyBinaryIdentity(_runtimeCommand, null),
        result.StandardOutput.Trim());
  }

  public async ValueTask<CaddyRuntimeOperationResult> ValidateConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);

    return await RunWithTempConfigAsync(
        config,
        static path => ["validate", "--config", path],
        "config-validation-failed",
        cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask<CaddyRuntimeOperationResult> ReloadConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);

    return await RunWithTempConfigAsync(
        config,
        static path => ["reload", "--config", path],
        "config-reload-failed",
        cancellationToken).ConfigureAwait(false);
  }

  private async ValueTask<CaddyRuntimeOperationResult> RunWithTempConfigAsync(
      CaddyRuntimeConfig config,
      Func<string, string[]> argumentsFactory,
      string failureCode,
      CancellationToken cancellationToken)
  {
    var tempPath = Path.Combine(Path.GetTempPath(), $"cadder-caddy-{Guid.NewGuid():N}.json");
    try
    {
      await File.WriteAllTextAsync(tempPath, config.Content, cancellationToken).ConfigureAwait(false);
      var result = await RunCommandAsync(argumentsFactory(tempPath), cancellationToken).ConfigureAwait(false);
      if (result.Succeeded)
      {
        return CaddyRuntimeOperationResult.Success(result.StandardOutput.Trim());
      }

      return CaddyRuntimeOperationResult.Failure(
          NormalizeMessage(result.StandardError, result.StandardOutput),
          [new CaddyConfigDiagnostic(failureCode, NormalizeMessage(result.StandardError, result.StandardOutput), null, [])]);
    }
    finally
    {
      try
      {
        File.Delete(tempPath);
      }
      catch
      {
      }
    }
  }

  private async ValueTask<CommandResult> RunCommandAsync(
      string[] arguments,
      CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = _runtimeCommand,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    try
    {
      using var process = Process.Start(startInfo);
      if (process is null)
      {
        return CommandResult.Failure($"Could not start '{_runtimeCommand}'.");
      }

      var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
      var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
      var stdout = await stdoutTask.ConfigureAwait(false);
      var stderr = await stderrTask.ConfigureAwait(false);

      return new CommandResult(process.ExitCode == 0, stdout, stderr);
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
      return CommandResult.Failure($"Could not run '{_runtimeCommand}': {ex.Message}");
    }
  }

  private static string NormalizeMessage(string stderr, string stdout)
  {
    var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
    message = message.Trim();
    return message.Length == 0 ? "<no process output>" : message;
  }

  private sealed record CommandResult(
      bool Succeeded,
      string StandardOutput,
      string StandardError)
  {
    public static CommandResult Failure(string message)
    {
      return new CommandResult(false, string.Empty, message);
    }
  }
}
