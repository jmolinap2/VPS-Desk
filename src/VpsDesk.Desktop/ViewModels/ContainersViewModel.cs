using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Containers;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class ContainersViewModel : ObservableObject
{
    private readonly IContainerService _containers;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private DockerContainerAction? _pendingAction;
    private string? _pendingContainerId;
    private string? _pendingContainerName;
    private DateTimeOffset? _lastRefreshUtc;

    public ObservableCollection<DockerContainerInfo> Items { get; } = new();

    [ObservableProperty] private DockerContainerInfo? _selectedContainer;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _autoRefreshEnabled;
    [ObservableProperty] private string _statusMessage = "Open Containers to inspect Docker on the active VPS.";
    [ObservableProperty] private string _lastUpdated = "Never";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _stoppedCount;
    [ObservableProperty] private int _unhealthyCount;
    [ObservableProperty] private int _projectCount;
    [ObservableProperty] private bool _hasPendingAction;
    [ObservableProperty] private string _pendingActionMessage = string.Empty;

    public bool HasSelection => SelectedContainer != null;
    public bool IsEmpty => !IsBusy && Items.Count == 0;
    public bool CanStartSelected => SelectedContainer is { IsRunning: false };
    public bool CanStopSelected => SelectedContainer is { IsRunning: true };
    public bool CanRestartSelected => SelectedContainer is { IsRunning: true };

    public ContainersViewModel(
        IContainerService containers,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _containers = containers;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

    partial void OnSelectedContainerChanged(DockerContainerInfo? value)
    {
        CancelPendingAction();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanStartSelected));
        OnPropertyChanged(nameof(CanStopSelected));
        OnPropertyChanged(nameof(CanRestartSelected));
    }

    public async Task RefreshIfNeededAsync()
    {
        if (_lastRefreshUtc == null || DateTimeOffset.UtcNow - _lastRefreshUtc > TimeSpan.FromSeconds(8))
        {
            await RefreshAsync();
        }
    }

    public void Reset()
    {
        Items.Clear();
        SelectedContainer = null;
        TotalCount = 0;
        RunningCount = 0;
        StoppedCount = 0;
        UnhealthyCount = 0;
        ProjectCount = 0;
        LastUpdated = "Never";
        StatusMessage = "Select an active server, then refresh Docker containers.";
        AutoRefreshEnabled = false;
        _lastRefreshUtc = null;
        CancelPendingAction();
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public async Task RefreshAsync()
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
        OnPropertyChanged(nameof(IsEmpty));
        StatusMessage = "Reading Docker containers and live stats...";
        var selectedName = SelectedContainer?.Name;

        try
        {
            var items = await _containers.ListAsync(server, _secretAccessor());

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            TotalCount = Items.Count;
            RunningCount = Items.Count(x => x.IsRunning);
            StoppedCount = TotalCount - RunningCount;
            UnhealthyCount = Items.Count(x => x.IsUnhealthy);
            ProjectCount = Items
                .Select(x => x.ComposeProject)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            SelectedContainer = selectedName == null
                ? Items.FirstOrDefault()
                : Items.FirstOrDefault(x => x.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                  ?? Items.FirstOrDefault();

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            LastUpdated = DateTimeOffset.Now.ToString("HH:mm:ss");
            AutoRefreshEnabled = true;
            StatusMessage = TotalCount == 0
                ? "Docker is reachable, but no containers were found."
                : $"Docker inventory updated · {RunningCount} running of {TotalCount}.";
        }
        catch (Exception ex)
        {
            AutoRefreshEnabled = false;
            StatusMessage = $"Docker unavailable: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void RequestStart() => RequestAction(DockerContainerAction.Start);

    [RelayCommand]
    private void RequestStop() => RequestAction(DockerContainerAction.Stop);

    [RelayCommand]
    private void RequestRestart() => RequestAction(DockerContainerAction.Restart);

    [RelayCommand]
    private async Task ConfirmActionAsync()
    {
        if (_pendingAction == null || _pendingContainerId == null) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "The active server changed. Action cancelled.";
            CancelPendingAction();
            return;
        }

        var action = _pendingAction.Value;
        var containerId = _pendingContainerId;
        var containerName = _pendingContainerName ?? containerId;
        IsBusy = true;
        AutoRefreshEnabled = false;
        StatusMessage = $"Running Docker {action.ToString().ToLowerInvariant()} on {containerName}...";

        try
        {
            var result = await _containers.ExecuteAsync(server, _secretAccessor(), containerId, action);
            if (!result.Succeeded)
            {
                StatusMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? $"Docker {action} failed."
                    : result.Error;
                return;
            }

            StatusMessage = $"{containerName}: {action} completed.";
            CancelPendingAction();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Container action failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void CancelAction() => CancelPendingAction();

    private void RequestAction(DockerContainerAction action)
    {
        var selected = SelectedContainer;
        if (selected == null) return;

        if (action == DockerContainerAction.Start && selected.IsRunning)
        {
            StatusMessage = $"{selected.Name} is already running.";
            return;
        }

        if (action is DockerContainerAction.Stop or DockerContainerAction.Restart && !selected.IsRunning)
        {
            StatusMessage = $"{selected.Name} is not running.";
            return;
        }

        _pendingAction = action;
        _pendingContainerId = selected.Id;
        _pendingContainerName = selected.Name;
        HasPendingAction = true;

        var environment = _serverAccessor()?.Environment ?? ServerEnvironment.Production;
        var productionWarning = environment == ServerEnvironment.Production
            ? " This is a Production server and the action can interrupt service."
            : string.Empty;
        PendingActionMessage = $"Confirm {action.ToString().ToLowerInvariant()} for container '{selected.Name}'.{productionWarning}";
    }

    private void CancelPendingAction()
    {
        _pendingAction = null;
        _pendingContainerId = null;
        _pendingContainerName = null;
        HasPendingAction = false;
        PendingActionMessage = string.Empty;
    }
}
