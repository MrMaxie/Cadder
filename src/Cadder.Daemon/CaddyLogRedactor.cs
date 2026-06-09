using System.Text.RegularExpressions;
using Cadder.Contracts;

namespace Cadder.Daemon;

public sealed partial class CaddyLogRedactor : ICaddyLogRedactor
{
  public string Redact(string value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return value;
    }

    var redacted = SecretAssignmentPattern().Replace(value, "$1=<redacted>");
    redacted = AuthorizationPattern().Replace(redacted, "$1 <redacted>");
    redacted = BearerPattern().Replace(redacted, "$1<redacted>");
    return redacted;
  }

  public CaddyRuntimeDiagnostic Redact(CaddyRuntimeDiagnostic diagnostic)
  {
    ArgumentNullException.ThrowIfNull(diagnostic);

    return diagnostic with { Message = Redact(diagnostic.Message) };
  }

  public CaddyConfigDiagnostic Redact(CaddyConfigDiagnostic diagnostic)
  {
    ArgumentNullException.ThrowIfNull(diagnostic);

    return diagnostic with { Message = Redact(diagnostic.Message) };
  }

  public ShimRunMetadata Redact(ShimRunMetadata shimRun)
  {
    ArgumentNullException.ThrowIfNull(shimRun);

    return shimRun with
    {
      RawArguments = shimRun.RawArguments.Length == 0 ? [] : ["<redacted arguments>"],
      CommandLine = string.IsNullOrWhiteSpace(shimRun.CommandLine)
          ? string.Empty
          : "<redacted command line>"
    };
  }

  [GeneratedRegex(@"(?i)\b(token|secret|password|passwd|pwd|api[_-]?key|authorization)\s*=\s*([^\s,;]+)")]
  private static partial Regex SecretAssignmentPattern();

  [GeneratedRegex(@"(?i)\b(authorization)\s*:\s*([^\s,;]+(?:\s+[^\s,;]+)?)")]
  private static partial Regex AuthorizationPattern();

  [GeneratedRegex(@"(?i)\b(bearer\s+)[a-z0-9._~+/=-]+")]
  private static partial Regex BearerPattern();
}
