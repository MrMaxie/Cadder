using System.Diagnostics;

namespace Cadder.CaddyShim;

public interface IDaemonExecutableResolver
{
  string ResolveDaemonExecutablePath();
}

public interface IDaemonProcessLauncher
{
  ValueTask StartAsync(CancellationToken cancellationToken = default);
}

public sealed class EnvironmentDaemonExecutableResolver : IDaemonExecutableResolver
{
  public const string OverrideEnvironmentVariableName = "CADDER_DAEMON_EXE";

  public string ResolveDaemonExecutablePath()
  {
    var overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariableName);
    if (!string.IsNullOrWhiteSpace(overridePath))
    {
      return Path.GetFullPath(overridePath);
    }

    var candidate = FindRepositoryLayoutCandidate();
    if (candidate is not null)
    {
      return candidate;
    }

    var appDirectoryCandidate = Path.Combine(AppContext.BaseDirectory, "Cadder.Tray.WinUI.exe");
    if (File.Exists(appDirectoryCandidate))
    {
      return appDirectoryCandidate;
    }

    throw new FileNotFoundException(
        $"Cadder daemon executable was not found. Set {OverrideEnvironmentVariableName} to the Cadder.Tray.WinUI.exe path.");
  }

  private static string? FindRepositoryLayoutCandidate()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
      var solutionPath = Path.Combine(directory.FullName, "Cadder.slnx");
      if (File.Exists(solutionPath))
      {
        var candidate = Path.Combine(
            directory.FullName,
            "src",
            "Cadder.Tray.WinUI",
            "bin",
            "x64",
            "Debug",
            "net10.0-windows10.0.22621.0",
            "win-x64",
            "Cadder.Tray.WinUI.exe");

        return File.Exists(candidate) ? candidate : null;
      }

      directory = directory.Parent;
    }

    return null;
  }
}

public sealed class ProcessDaemonLauncher : IDaemonProcessLauncher
{
  private readonly IDaemonExecutableResolver _resolver;

  public ProcessDaemonLauncher(IDaemonExecutableResolver? resolver = null)
  {
    _resolver = resolver ?? new EnvironmentDaemonExecutableResolver();
  }

  public ValueTask StartAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var executablePath = _resolver.ResolveDaemonExecutablePath();
    Process.Start(new ProcessStartInfo(executablePath)
    {
      UseShellExecute = false,
      WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
    });

    return ValueTask.CompletedTask;
  }
}
