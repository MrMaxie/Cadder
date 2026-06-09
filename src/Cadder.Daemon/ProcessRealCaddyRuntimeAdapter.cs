using System.ComponentModel;
using System.Diagnostics;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed class ProcessRealCaddyRuntimeAdapter : IRealCaddyRuntimeAdapter, ICadderOwnedRuntime
{
  private const string DefaultRuntimeCommand = "caddy-real";
  private const string DefaultAdminEndpoint = "http://127.0.0.1:2019";
  private readonly RealCaddyExecutableResolver _resolver;
  private readonly IManagedProcessFactory _processFactory;
  private readonly string _adminEndpoint;
  private readonly TimeSpan _startupObservationDelay;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private RealCaddyRuntimeState _state = new(
      RealCaddyRuntimeStatus.Unknown,
      null,
      null,
      Diagnostics: []);
  private IManagedProcess? _ownedProcess;
  private string? _ownedConfigPath;

  public ProcessRealCaddyRuntimeAdapter(
      string? runtimeCommand = null,
      string? adminEndpoint = null,
      RealCaddyExecutableResolver? resolver = null,
      IManagedProcessFactory? processFactory = null,
      TimeSpan? startupObservationDelay = null)
  {
    _resolver = resolver ?? new RealCaddyExecutableResolver(runtimeCommand);
    _adminEndpoint = string.IsNullOrWhiteSpace(adminEndpoint)
        ? Environment.GetEnvironmentVariable("CADDER_CADDY_ADMIN_ENDPOINT") ?? DefaultAdminEndpoint
        : adminEndpoint;
    _processFactory = processFactory ?? new SystemManagedProcessFactory();
    _startupObservationDelay = startupObservationDelay ?? TimeSpan.FromMilliseconds(750);
  }

  public async ValueTask<RealCaddyRuntimeState> InspectAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_ownedProcess is not null)
      {
        if (!_ownedProcess.HasExited)
        {
          _state = WithRunningProcess(_state.Binary, _state.Version, _ownedProcess, []);
          return _state;
        }

        _state = WithDiagnostics(
            RealCaddyRuntimeStatus.Unhealthy,
            _state.Binary,
            _state.Version,
            null,
            "runtime-exited",
            $"Cadder-owned Caddy runtime exited with code {_ownedProcess.ExitCode}.",
            "inspect");
        DisposeOwnedProcess();
        return _state;
      }

      if (_state.Status == RealCaddyRuntimeStatus.Idle)
      {
        return _state;
      }

      var resolution = _resolver.Resolve();
      if (!resolution.Succeeded)
      {
        _state = new RealCaddyRuntimeState(
            RealCaddyRuntimeStatus.NotResolved,
            null,
            null,
            Diagnostics: resolution.Diagnostics);
        return _state;
      }

      var version = await RunVersionAsync(resolution, cancellationToken).ConfigureAwait(false);
      _state = version.Succeeded
          ? new RealCaddyRuntimeState(
              RealCaddyRuntimeStatus.Resolved,
              resolution.Binary,
              version.StandardOutput.Trim(),
              null,
              _adminEndpoint,
              [])
          : new RealCaddyRuntimeState(
              RealCaddyRuntimeStatus.NotResolved,
              resolution.Binary,
              null,
              Diagnostics: [Diagnostic("runtime-version-failed", NormalizeMessage(version.StandardError, version.StandardOutput), "version")]);
      return _state;
    }
    finally
    {
      _gate.Release();
    }
  }

  public async ValueTask<RealCaddyRuntimeState> EnsureRunningAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(config);

    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_ownedProcess is { HasExited: false })
      {
        _state = WithRunningProcess(_state.Binary, _state.Version, _ownedProcess, []);
        return _state;
      }

      DisposeOwnedProcess();
      var resolution = _resolver.Resolve();
      if (!resolution.Succeeded)
      {
        _state = new RealCaddyRuntimeState(
            RealCaddyRuntimeStatus.NotResolved,
            null,
            null,
            Diagnostics: resolution.Diagnostics);
        return _state;
      }

      var version = await RunVersionAsync(resolution, cancellationToken).ConfigureAwait(false);
      if (!version.Succeeded)
      {
        _state = new RealCaddyRuntimeState(
            RealCaddyRuntimeStatus.Unhealthy,
            resolution.Binary,
            null,
            Diagnostics: [Diagnostic("runtime-version-failed", NormalizeMessage(version.StandardError, version.StandardOutput), "version")]);
        return _state;
      }

      _ownedConfigPath = await WriteOwnedConfigAsync(config, cancellationToken).ConfigureAwait(false);
      var startInfo = CreateStartInfo(
          resolution.CommandPath,
          ["run", "--config", _ownedConfigPath],
          redirectOutput: false);
      try
      {
        _ownedProcess = _processFactory.Start(startInfo);
      }
      catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
      {
        _state = WithDiagnostics(
            RealCaddyRuntimeStatus.Unhealthy,
            resolution.Binary,
            version.StandardOutput.Trim(),
            null,
            "runtime-start-failed",
            $"Could not start '{resolution.CommandPath}': {ex.Message}",
            "run");
        DeleteOwnedConfig();
        return _state;
      }

      if (_ownedProcess is null)
      {
        _state = WithDiagnostics(
            RealCaddyRuntimeStatus.Unhealthy,
            resolution.Binary,
            version.StandardOutput.Trim(),
            null,
            "runtime-start-failed",
            $"Could not start '{resolution.CommandPath}'.",
            "run");
        DeleteOwnedConfig();
        return _state;
      }

      await Task.Delay(_startupObservationDelay, cancellationToken).ConfigureAwait(false);
      if (_ownedProcess.HasExited)
      {
        _state = WithDiagnostics(
            RealCaddyRuntimeStatus.Unhealthy,
            resolution.Binary,
            version.StandardOutput.Trim(),
            null,
            "runtime-exited-during-start",
            $"Cadder-owned Caddy runtime exited during startup with code {_ownedProcess.ExitCode}.",
            "run");
        DisposeOwnedProcess();
        return _state;
      }

      _state = WithRunningProcess(resolution.Binary, version.StandardOutput.Trim(), _ownedProcess, []);
      return _state;
    }
    finally
    {
      _gate.Release();
    }
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
        path => ["reload", "--config", path, "--address", _adminEndpoint],
        "config-reload-failed",
        cancellationToken).ConfigureAwait(false);
  }

  public async ValueTask<RealCaddyRuntimeState> EnterIdleAsync(CancellationToken cancellationToken = default)
  {
    await StopAsync(cancellationToken).ConfigureAwait(false);
    return _state;
  }

  public async ValueTask StopAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_ownedProcess is { HasExited: false } process)
      {
        try
        {
          process.Kill(entireProcessTree: true);
          await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
          _state = WithDiagnostics(
              RealCaddyRuntimeStatus.Unhealthy,
              _state.Binary,
              _state.Version,
              null,
              "runtime-stop-failed",
              $"Could not stop Cadder-owned Caddy runtime: {ex.Message}",
              "stop");
          DisposeOwnedProcess();
          return;
        }
      }

      DisposeOwnedProcess();
      _state = new RealCaddyRuntimeState(
          RealCaddyRuntimeStatus.Idle,
          _state.Binary,
          _state.Version,
          null,
          _adminEndpoint,
          []);
    }
    finally
    {
      _gate.Release();
    }
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
      var resolution = _resolver.Resolve();
      if (!resolution.Succeeded)
      {
        await SetStateFromResolutionFailureAsync(resolution, cancellationToken).ConfigureAwait(false);
        return CaddyRuntimeOperationResult.Failure(
            resolution.Diagnostics.FirstOrDefault()?.Message ?? "Real Caddy runtime is not resolved.",
            [.. resolution.Diagnostics.Select(diagnostic => new CaddyConfigDiagnostic(diagnostic.Code, diagnostic.Message, null, []))]);
      }

      var result = await RunCommandAsync(resolution.CommandPath, argumentsFactory(tempPath), cancellationToken).ConfigureAwait(false);
      if (result.Succeeded)
      {
        return CaddyRuntimeOperationResult.Success(result.StandardOutput.Trim());
      }

      var message = NormalizeMessage(result.StandardError, result.StandardOutput);
      await SetUnhealthyAsync(resolution.Binary, failureCode, message, argumentsFactory(tempPath)[0], cancellationToken)
          .ConfigureAwait(false);
      return CaddyRuntimeOperationResult.Failure(
          message,
          [new CaddyConfigDiagnostic(failureCode, message, null, [])]);
    }
    finally
    {
      DeleteFileBestEffort(tempPath);
    }
  }

  private async ValueTask SetStateFromResolutionFailureAsync(
      RealCaddyExecutableResolution resolution,
      CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      _state = new RealCaddyRuntimeState(
          RealCaddyRuntimeStatus.NotResolved,
          null,
          null,
          Diagnostics: resolution.Diagnostics);
    }
    finally
    {
      _gate.Release();
    }
  }

  private async ValueTask SetUnhealthyAsync(
      RealCaddyBinaryIdentity? binary,
      string code,
      string message,
      string operation,
      CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      _state = WithDiagnostics(
          RealCaddyRuntimeStatus.Unhealthy,
          binary,
          _state.Version,
          _state.Process,
          code,
          message,
          operation);
    }
    finally
    {
      _gate.Release();
    }
  }

  private async ValueTask<string> WriteOwnedConfigAsync(
      CaddyRuntimeConfig config,
      CancellationToken cancellationToken)
  {
    DeleteOwnedConfig();

    var tempPath = Path.Combine(Path.GetTempPath(), $"cadder-owned-caddy-{Guid.NewGuid():N}.json");
    await File.WriteAllTextAsync(tempPath, config.Content, cancellationToken).ConfigureAwait(false);
    return tempPath;
  }

  private ValueTask<CommandResult> RunVersionAsync(
      RealCaddyExecutableResolution resolution,
      CancellationToken cancellationToken)
  {
    return RunCommandAsync(resolution.CommandPath, ["version"], cancellationToken);
  }

  private async ValueTask<CommandResult> RunCommandAsync(
      string commandPath,
      string[] arguments,
      CancellationToken cancellationToken)
  {
    var startInfo = CreateStartInfo(commandPath, arguments, redirectOutput: true);

    try
    {
      using var process = _processFactory.Start(startInfo);
      if (process is null)
      {
        return CommandResult.Failure($"Could not start '{commandPath}'.");
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
      return CommandResult.Failure($"Could not run '{commandPath}': {ex.Message}");
    }
  }

  private ProcessStartInfo CreateStartInfo(
      string commandPath,
      string[] arguments,
      bool redirectOutput)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = commandPath,
      RedirectStandardOutput = redirectOutput,
      RedirectStandardError = redirectOutput,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    return startInfo;
  }

  private RealCaddyRuntimeState WithRunningProcess(
      RealCaddyBinaryIdentity? binary,
      string? version,
      IManagedProcess process,
      CaddyRuntimeDiagnostic[] diagnostics)
  {
    return new RealCaddyRuntimeState(
        RealCaddyRuntimeStatus.Running,
        binary,
        version,
        new RealCaddyProcessIdentity(
            process.Id,
            new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            true),
        _adminEndpoint,
        diagnostics);
  }

  private RealCaddyRuntimeState WithDiagnostics(
      RealCaddyRuntimeStatus status,
      RealCaddyBinaryIdentity? binary,
      string? version,
      RealCaddyProcessIdentity? process,
      string code,
      string message,
      string operation)
  {
    return new RealCaddyRuntimeState(
        status,
        binary,
        version,
        process,
        _adminEndpoint,
        [Diagnostic(code, message, operation)]);
  }

  private void DisposeOwnedProcess()
  {
    _ownedProcess?.Dispose();
    _ownedProcess = null;
    DeleteOwnedConfig();
  }

  private void DeleteOwnedConfig()
  {
    if (_ownedConfigPath is null)
    {
      return;
    }

    DeleteFileBestEffort(_ownedConfigPath);
    _ownedConfigPath = null;
  }

  private static void DeleteFileBestEffort(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static CaddyRuntimeDiagnostic Diagnostic(string code, string message, string operation)
  {
    return new CaddyRuntimeDiagnostic(code, message, operation);
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

public sealed class RealCaddyExecutableResolver
{
  private const string DefaultRuntimeCommand = "caddy-real";
  private readonly string _runtimeCommand;
  private readonly string[] _knownShimPaths;

  public RealCaddyExecutableResolver(
      string? runtimeCommand = null,
      IEnumerable<string>? knownShimPaths = null)
  {
    _runtimeCommand = string.IsNullOrWhiteSpace(runtimeCommand)
        ? Environment.GetEnvironmentVariable("CADDER_CADDY_REAL_COMMAND") ?? DefaultRuntimeCommand
        : runtimeCommand;
    _knownShimPaths = [.. DefaultKnownShimPaths()
        .Concat(knownShimPaths ?? [])
        .Select(NormalizePath)
        .Where(static path => path is not null)
        .Select(static path => path!)];
  }

  public RealCaddyExecutableResolution Resolve()
  {
    var rejectedShimPaths = new List<string>();
    foreach (var candidate in EnumerateCandidates(_runtimeCommand))
    {
      if (!File.Exists(candidate))
      {
        continue;
      }

      var normalized = NormalizePath(candidate);
      if (normalized is null)
      {
        continue;
      }

      if (_knownShimPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
      {
        rejectedShimPaths.Add(normalized);
        continue;
      }

      return RealCaddyExecutableResolution.Success(
          normalized,
          new RealCaddyBinaryIdentity(normalized, CreateFileIdentity(normalized)));
    }

    if (rejectedShimPaths.Count > 0)
    {
      return RealCaddyExecutableResolution.Failure(
          [new CaddyRuntimeDiagnostic(
              "runtime-resolved-to-cadder-shim",
              $"Resolved Caddy candidate points at Cadder's caddy.exe shim: {rejectedShimPaths[0]}.",
              "resolve")]);
    }

    return RealCaddyExecutableResolution.Failure(
        [new CaddyRuntimeDiagnostic(
            "runtime-not-found",
            $"Real Caddy executable '{_runtimeCommand}' was not found.",
            "resolve")]);
  }

  private static IEnumerable<string> EnumerateCandidates(string command)
  {
    if (IsPathLike(command))
    {
      foreach (var candidate in ExpandCommandExtensions(command))
      {
        yield return candidate;
      }

      yield break;
    }

    foreach (var directory in PathEntries())
    {
      foreach (var candidate in ExpandCommandExtensions(Path.Combine(directory, command)))
      {
        yield return candidate;
      }
    }
  }

  private static IEnumerable<string> ExpandCommandExtensions(string path)
  {
    if (!string.IsNullOrWhiteSpace(Path.GetExtension(path)))
    {
      yield return path;
      yield break;
    }

    yield return path;
    foreach (var extension in PathExtensions())
    {
      yield return path + extension;
    }
  }

  private static IEnumerable<string> PathEntries()
  {
    var path = Environment.GetEnvironmentVariable("PATH");
    return string.IsNullOrWhiteSpace(path)
        ? []
        : path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }

  private static IEnumerable<string> PathExtensions()
  {
    var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
    return string.IsNullOrWhiteSpace(pathExt)
        ? [".exe", ".cmd", ".bat"]
        : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }

  private static IEnumerable<string> DefaultKnownShimPaths()
  {
    var configuredShimPath = Environment.GetEnvironmentVariable("CADDER_CADDY_SHIM_PATH");
    if (!string.IsNullOrWhiteSpace(configuredShimPath))
    {
      yield return configuredShimPath;
    }

    yield return Path.Combine(AppContext.BaseDirectory, "caddy.exe");
  }

  private static bool IsPathLike(string command)
  {
    return Path.IsPathFullyQualified(command)
        || command.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || command.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
  }

  private static string? NormalizePath(string path)
  {
    try
    {
      return Path.GetFullPath(path);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
      return null;
    }
  }

  private static string? CreateFileIdentity(string path)
  {
    try
    {
      var file = new FileInfo(path);
      return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }
}

public sealed record RealCaddyExecutableResolution(
    bool Succeeded,
    string CommandPath,
    RealCaddyBinaryIdentity? Binary,
    CaddyRuntimeDiagnostic[] Diagnostics)
{
  public static RealCaddyExecutableResolution Success(
      string commandPath,
      RealCaddyBinaryIdentity binary)
  {
    return new RealCaddyExecutableResolution(true, commandPath, binary, []);
  }

  public static RealCaddyExecutableResolution Failure(CaddyRuntimeDiagnostic[] diagnostics)
  {
    return new RealCaddyExecutableResolution(false, string.Empty, null, diagnostics);
  }
}

public interface IManagedProcess : IDisposable
{
  int Id { get; }

  DateTime StartTime { get; }

  bool HasExited { get; }

  int ExitCode { get; }

  StreamReader StandardOutput { get; }

  StreamReader StandardError { get; }

  ValueTask WaitForExitAsync(CancellationToken cancellationToken = default);

  void Kill(bool entireProcessTree);
}

public interface IManagedProcessFactory
{
  IManagedProcess? Start(ProcessStartInfo startInfo);
}

public sealed class SystemManagedProcessFactory : IManagedProcessFactory
{
  public IManagedProcess? Start(ProcessStartInfo startInfo)
  {
    var process = Process.Start(startInfo);
    return process is null ? null : new SystemManagedProcess(process);
  }

  private sealed class SystemManagedProcess(Process process) : IManagedProcess
  {
    public int Id => process.Id;

    public DateTime StartTime => process.StartTime;

    public bool HasExited => process.HasExited;

    public int ExitCode => process.ExitCode;

    public StreamReader StandardOutput => process.StandardOutput;

    public StreamReader StandardError => process.StandardError;

    public ValueTask WaitForExitAsync(CancellationToken cancellationToken = default)
    {
      return new ValueTask(process.WaitForExitAsync(cancellationToken));
    }

    public void Kill(bool entireProcessTree)
    {
      process.Kill(entireProcessTree);
    }

    public void Dispose()
    {
      process.Dispose();
    }
  }
}
