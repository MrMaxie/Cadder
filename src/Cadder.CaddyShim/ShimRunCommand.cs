namespace Cadder.CaddyShim;

public enum ShimCommandKind
{
  Run = 0,
  Info = 1
}

public sealed record ShimCommandParseResult(
    bool Succeeded,
    ShimCommandKind? CommandKind,
    ShimRunCommand? RunCommand,
    string? ErrorMessage)
{
  public static ShimCommandParseResult Success(ShimCommandKind commandKind, ShimRunCommand? runCommand = null)
  {
    return new ShimCommandParseResult(true, commandKind, runCommand, null);
  }

  public static ShimCommandParseResult Failure(string errorMessage)
  {
    return new ShimCommandParseResult(false, null, null, errorMessage);
  }
}

public sealed record ShimRunCommand(
    string WorkingDirectory,
    string RawConfigPath,
    string CanonicalConfigPath,
    string? Adapter,
    string[] RawArguments);

public static class ShimCommandParser
{
  public const string SupportedCommandSet =
      "Supported Cadder command set: caddy run [--config <path>] [--adapter <name>] and --cadder-shim-info.";

  public static ShimCommandParseResult Parse(string[] args, string workingDirectory)
  {
    ArgumentNullException.ThrowIfNull(args);

    if (string.IsNullOrWhiteSpace(workingDirectory))
    {
      throw new ArgumentException("A working directory is required.", nameof(workingDirectory));
    }

    if (args.Any(static arg => string.Equals(arg, "--cadder-shim-info", StringComparison.OrdinalIgnoreCase)))
    {
      return ShimCommandParseResult.Success(ShimCommandKind.Info);
    }

    if (args.Length == 0 || !string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
    {
      var command = args.Length == 0 ? "<none>" : args[0];
      return ShimCommandParseResult.Failure($"Unsupported caddy command '{command}'. {SupportedCommandSet}");
    }

    var rawConfigPath = "Caddyfile";
    string? adapter = null;

    for (var index = 1; index < args.Length; index++)
    {
      var arg = args[index];

      if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase))
      {
        if (!TryReadOptionValue(args, ref index, "--config", out rawConfigPath, out var error))
        {
          return ShimCommandParseResult.Failure(error);
        }

        continue;
      }

      if (TryReadEqualsOption(arg, "--config", out var configValue))
      {
        if (string.IsNullOrWhiteSpace(configValue))
        {
          return ShimCommandParseResult.Failure("The --config option requires a non-empty value.");
        }

        rawConfigPath = configValue;
        continue;
      }

      if (string.Equals(arg, "--adapter", StringComparison.OrdinalIgnoreCase))
      {
        if (!TryReadOptionValue(args, ref index, "--adapter", out adapter, out var error))
        {
          return ShimCommandParseResult.Failure(error);
        }

        continue;
      }

      if (TryReadEqualsOption(arg, "--adapter", out var adapterValue))
      {
        if (string.IsNullOrWhiteSpace(adapterValue))
        {
          return ShimCommandParseResult.Failure("The --adapter option requires a non-empty value.");
        }

        adapter = adapterValue;
        continue;
      }

      return ShimCommandParseResult.Failure(
          $"Unsupported caddy run argument '{arg}'. {SupportedCommandSet}");
    }

    var canonicalConfigPath = Path.GetFullPath(
        Path.IsPathFullyQualified(rawConfigPath)
            ? rawConfigPath
            : Path.Combine(workingDirectory, rawConfigPath));

    return ShimCommandParseResult.Success(
        ShimCommandKind.Run,
        new ShimRunCommand(
            workingDirectory,
            rawConfigPath,
            canonicalConfigPath,
            adapter,
            [.. args]));
  }

  private static bool TryReadOptionValue(
      string[] args,
      ref int index,
      string optionName,
      out string value,
      out string error)
  {
    if (index + 1 >= args.Length
        || string.IsNullOrWhiteSpace(args[index + 1])
        || args[index + 1].StartsWith("-", StringComparison.Ordinal))
    {
      value = string.Empty;
      error = $"The {optionName} option requires a non-empty value.";
      return false;
    }

    value = args[++index];
    error = string.Empty;
    return true;
  }

  private static bool TryReadEqualsOption(string arg, string optionName, out string value)
  {
    var prefix = $"{optionName}=";
    if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
      value = string.Empty;
      return false;
    }

    value = arg[prefix.Length..];
    return true;
  }
}
