using System.Globalization;
using System.Text;

namespace Cadder.Tray.WinUI;

public sealed class PanelStateStore
{
  private PanelState _currentState = PanelState.Loading(DateTimeOffset.UtcNow);
  private string _currentStateFingerprint = string.Empty;
  private string _filterText = string.Empty;

  public event EventHandler? Changed;

  public PanelState CurrentState => _currentState;

  public string FilterText => _filterText;

  public void SetLoading(DateTimeOffset capturedAtUtc)
  {
    SetState(PanelState.Loading(capturedAtUtc));
  }

  public void SetReady(PanelState state)
  {
    ArgumentNullException.ThrowIfNull(state);
    SetState(state);
  }

  public void SetDisconnected(DateTimeOffset capturedAtUtc, string message)
  {
    SetState(PanelState.Disconnected(capturedAtUtc, message));
  }

  public void SetFilter(string? filterText)
  {
    var normalized = filterText?.Trim() ?? string.Empty;
    if (string.Equals(_filterText, normalized, StringComparison.Ordinal))
    {
      return;
    }

    _filterText = normalized;
    Changed?.Invoke(this, EventArgs.Empty);
  }

  private void SetState(PanelState state)
  {
    var fingerprint = CreateRenderFingerprint(state);
    var shouldNotify = !string.Equals(_currentStateFingerprint, fingerprint, StringComparison.Ordinal);
    _currentState = state;
    _currentStateFingerprint = fingerprint;
    if (shouldNotify)
    {
      Changed?.Invoke(this, EventArgs.Empty);
    }
  }

  private static string CreateRenderFingerprint(PanelState state)
  {
    var builder = new StringBuilder();
    Append(builder, state.ConnectionState);
    Append(builder, state.ConnectionMessage);
    AppendSummary(builder, state.Summary);

    foreach (var instance in state.Instances)
    {
      Append(builder, instance.RegistrationId);
      Append(builder, instance.ShimSessionNonce);
      Append(builder, instance.ProjectName);
      Append(builder, instance.SourcePath);
      Append(builder, instance.ConfigPath);
      Append(builder, instance.ProcessStatus);
      Append(builder, instance.OwnerExecutablePath);
      Append(builder, instance.DomainCount);
      Append(builder, instance.ActiveDomainCount);
      Append(builder, instance.ActivationSummary);
      Append(builder, instance.ActivationState);
      Append(builder, instance.IsStaleOwner);
      AppendDiagnostics(builder, instance.Diagnostics);
      Append(builder, instance.AutomationId);
      Append(builder, instance.SearchText);
    }

    foreach (var group in state.DomainGroups)
    {
      Append(builder, group.RegistrationId);
      Append(builder, group.ProjectName);
      Append(builder, group.SourcePath);
      Append(builder, group.ConfigPath);
      Append(builder, group.AutomationId);
      Append(builder, group.SearchText);

      foreach (var domain in group.Domains)
      {
        Append(builder, domain.RegistrationId);
        Append(builder, domain.ShimSessionNonce);
        Append(builder, domain.DomainKey);
        Append(builder, domain.Hostname);
        Append(builder, domain.UpstreamTarget);
        Append(builder, domain.EnabledState);
        Append(builder, domain.ConflictState);
        Append(builder, domain.LastError);
        Append(builder, domain.ActivationState);
        Append(builder, domain.IsEnabled);
        Append(builder, domain.IsConflicted);
        Append(builder, domain.AutomationId);
        Append(builder, domain.SearchText);
      }
    }

    AppendDiagnostics(builder, state.Diagnostics);
    foreach (var record in state.SearchRecords)
    {
      Append(builder, record.Title);
      Append(builder, record.Subtitle);
      Append(builder, record.TargetPageTag);
      Append(builder, record.FilterText);
      Append(builder, record.SearchText);
      Append(builder, record.AutomationId);
    }

    return builder.ToString();
  }

  private static void AppendSummary(StringBuilder builder, PanelSummary summary)
  {
    Append(builder, summary.DaemonStatus);
    Append(builder, summary.DaemonDetail);
    Append(builder, summary.StatusKind);
    Append(builder, summary.RuntimeStatus);
    Append(builder, summary.ConfigStatus);
    Append(builder, summary.EntrypointCount);
    Append(builder, summary.ActiveEntrypointCount);
    Append(builder, summary.DomainCount);
    Append(builder, summary.ActiveDomainCount);
    Append(builder, summary.FirstDiagnostic);
  }

  private static void AppendDiagnostics(StringBuilder builder, IReadOnlyList<PanelDiagnosticRow> diagnostics)
  {
    foreach (var diagnostic in diagnostics)
    {
      Append(builder, diagnostic.Severity);
      Append(builder, diagnostic.Scope);
      Append(builder, diagnostic.Code);
      Append(builder, diagnostic.Message);
      Append(builder, diagnostic.DomainKey);
      foreach (var path in diagnostic.SourceConfigPaths)
      {
        Append(builder, path);
      }

      Append(builder, diagnostic.AutomationId);
    }
  }

  private static void Append(StringBuilder builder, object? value)
  {
    var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    builder.Append(text.Length);
    builder.Append(':');
    builder.Append(text);
    builder.Append('|');
  }
}

public sealed record PanelNavigationContext(PanelStateStore Store);

internal static class PanelStateStoreLocator
{
  public static PanelStateStore? Current { get; set; }
}
