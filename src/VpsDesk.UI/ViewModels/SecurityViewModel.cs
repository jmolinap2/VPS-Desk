using VpsDesk.Desktop.Localization;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Activity;
using VpsDesk.Domain.Activity;
using VpsDesk.Domain.Security;
using VpsDesk.Domain.Servers;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

public partial class SecurityViewModel : ObservableObject
{
    private readonly ISecurityAuditService _security;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private readonly IOperationHistoryStore? _historyStore;
    private DateTimeOffset? _lastAuditUtc;

    public ObservableCollection<SecurityCheck> Checks { get; } = new();
    public event EventHandler? HistoryChanged;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _passedCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private string _lastChecked = LocalizationService.T("Common_Never");
    [ObservableProperty] private string _statusMessage = LocalizationService.T("Security_StatusInitial");

    public SecurityViewModel(
        ISecurityAuditService security,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor,
        IOperationHistoryStore? historyStore = null)
    {
        _security = security;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        _historyStore = historyStore;
        Checks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChecks));
    }

    public bool HasChecks => Checks.Count > 0;

    public async Task AuditIfNeededAsync()
    {
        if (_lastAuditUtc == null || DateTimeOffset.UtcNow - _lastAuditUtc > TimeSpan.FromMinutes(2))
        {
            await RunAuditAsync();
        }
    }

    public void Reset()
    {
        Checks.Clear();
        PassedCount = 0;
        WarningCount = 0;
        CriticalCount = 0;
        LastChecked = LocalizationService.T("Common_Never");
        StatusMessage = LocalizationService.T("Security_StatusInitial");
        _lastAuditUtc = null;
    }

    [RelayCommand]
    public async Task RunAuditAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            Reset();
            StatusMessage = LocalizationService.T("Common_NoActiveServer");
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        IsBusy = true;
        StatusMessage = LocalizationService.T("Security_StatusRunning");

        try
        {
            var snapshot = await _security.AuditAsync(server, _secretAccessor());
            Checks.Clear();
            foreach (var check in snapshot.Checks
                         .OrderByDescending(x => x.Severity == SecurityCheckSeverity.Critical)
                         .ThenByDescending(x => x.Severity == SecurityCheckSeverity.Warning)
                         .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
            {
                Checks.Add(check);
            }

            PassedCount = snapshot.PassedCount;
            WarningCount = snapshot.WarningCount;
            CriticalCount = snapshot.CriticalCount;
            _lastAuditUtc = DateTimeOffset.UtcNow;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = CriticalCount > 0
                ? $"Audit found {CriticalCount} critical exposure(s). Review before changing anything on the VPS."
                : WarningCount > 0
                    ? $"Audit completed with {WarningCount} warning(s)."
                    : "Audit completed without critical or warning findings.";

            await RecordAuditAsync(server, startedAt, null);
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Security_StatusFailed", friendly);
            Toast.Error(friendly, "No se pudo completar la auditoría de seguridad");
            await RecordAuditAsync(server, startedAt, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecordAuditAsync(ServerProfile server, DateTimeOffset startedAt, Exception? exception)
    {
        if (_historyStore is null) return;

        var details = new StringBuilder();
        foreach (var check in Checks)
        {
            details.AppendLine($"[{check.Severity}] {check.Label}: {check.Detail}");
        }

        var outcome = exception is not null
            ? OperationOutcome.Failed
            : CriticalCount > 0 || WarningCount > 0
                ? OperationOutcome.Warning
                : OperationOutcome.Success;

        var summary = exception is not null
            ? "Auditoría de seguridad fallida"
            : $"Auditoría de seguridad · {CriticalCount} críticos · {WarningCount} advertencias";

        var entry = new OperationHistoryEntry(
            Guid.NewGuid(),
            server.Id,
            server.Name,
            server.Environment.ToString(),
            OperationKind.SecurityAudit,
            "Security audit",
            summary,
            outcome,
            startedAt,
            DateTimeOffset.UtcNow,
            Details: exception?.Message ?? details.ToString().TrimEnd());

        await _historyStore.AddAsync(entry);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
