using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Security;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class SecurityViewModel : ObservableObject
{
    private readonly ISecurityAuditService _security;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private DateTimeOffset? _lastAuditUtc;

    public ObservableCollection<SecurityCheck> Checks { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _passedCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private string _lastChecked = "Never";
    [ObservableProperty] private string _statusMessage = "Run a read-only audit to inspect SSH posture and public listeners.";

    public SecurityViewModel(
        ISecurityAuditService security,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _security = security;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

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
        LastChecked = "Never";
        StatusMessage = "Run a read-only audit to inspect SSH posture and public listeners.";
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
            StatusMessage = "No active server. Choose one in Servers first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Running read-only security checks...";

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to complete security audit: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
