using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private const string DockerSource = "Docker container";
    private const string SystemdSource = "systemd service";

    private readonly IContainerService _containers;
    private readonly IRemoteLogService _logs;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private string _rawLogText = string.Empty;
    private bool _sourcesLoaded;

    public IReadOnlyList<string> SourceTypes { get; } = [DockerSource, SystemdSource];

    private string SourceTypeLabel => LocalizationService.T(SelectedSourceType == DockerSource ? "Logs_SourceDocker" : "Logs_SourceSystemd");
    public ObservableCollection<string> Sources { get; } = new();

    [ObservableProperty] private string _selectedSourceType = DockerSource;
    [ObservableProperty] private string? _selectedSource;
    [ObservableProperty] private int _tailLines = 250;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private string _statusMessage = LocalizationService.T("Logs_StatusChoose");
    [ObservableProperty] private string _lastLoaded = LocalizationService.T("Logs_NeverLoaded");
    [ObservableProperty] private bool _isBusy;

    public bool HasSources => Sources.Count > 0;
    public bool HasLogs => !string.IsNullOrEmpty(LogText);

    public LogsViewModel(
        IContainerService containers,
        IRemoteLogService logs,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _containers = containers;
        _logs = logs;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

    partial void OnSelectedSourceTypeChanged(string value)
    {
        _sourcesLoaded = false;
        _ = RefreshSourcesAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task RefreshIfNeededAsync()
    {
        if (!_sourcesLoaded)
        {
            await RefreshSourcesAsync();
        }
    }

    public void Reset()
    {
        Sources.Clear();
        SelectedSource = null;
        _rawLogText = string.Empty;
        LogText = string.Empty;
        LastLoaded = LocalizationService.T("Logs_NeverLoaded");
        StatusMessage = LocalizationService.T("Logs_StatusChoose");
        _sourcesLoaded = false;
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasLogs));
    }

    [RelayCommand]
    public async Task RefreshSourcesAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            Reset();
            StatusMessage = LocalizationService.T("Logs_StatusNoServer");
            return;
        }

        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Logs_StatusLoadingSources", SourceTypeLabel);
        var previous = SelectedSource;

        try
        {
            IReadOnlyList<string> sources;
            if (SelectedSourceType == DockerSource)
            {
                var containers = await _containers.ListAsync(server, _secretAccessor());
                sources = containers.Select(x => x.Name).ToArray();
            }
            else
            {
                sources = await _logs.ListSystemdServicesAsync(server, _secretAccessor());
            }

            Sources.Clear();
            foreach (var source in sources)
            {
                Sources.Add(source);
            }

            SelectedSource = previous != null && Sources.Contains(previous)
                ? previous
                : Sources.FirstOrDefault();
            _sourcesLoaded = true;
            StatusMessage = Sources.Count == 0
                ? LocalizationService.Current.Format("Logs_StatusNoSources", SourceTypeLabel)
                : LocalizationService.Current.Format("Logs_StatusSources", Sources.Count);
            OnPropertyChanged(nameof(HasSources));
        }
        catch (Exception ex)
        {
            _sourcesLoaded = false;
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Logs_StatusSourcesError", friendly);
            Toast.Error(friendly, "No se pudieron cargar las fuentes de logs");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoadLogsAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = LocalizationService.T("Logs_StatusNoServer");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSource))
        {
            StatusMessage = LocalizationService.T("Logs_StatusSelectSource");
            return;
        }

        TailLines = Math.Clamp(TailLines, 20, 5000);
        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Logs_StatusReading", TailLines, SelectedSource);

        try
        {
            _rawLogText = SelectedSourceType == DockerSource
                ? await _logs.ReadContainerLogsAsync(server, _secretAccessor(), SelectedSource, TailLines)
                : await _logs.ReadSystemdLogsAsync(server, _secretAccessor(), SelectedSource, TailLines);

            ApplyFilter();
            LastLoaded = DateTimeOffset.Now.ToString("HH:mm:ss");
            var lineCount = string.IsNullOrEmpty(_rawLogText)
                ? 0
                : _rawLogText.Split('\n').Length;
            StatusMessage = LocalizationService.Current.Format("Logs_StatusLoaded", lineCount, SelectedSource);
            OnPropertyChanged(nameof(HasLogs));
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Logs_StatusReadError", friendly);
            Toast.Error(friendly, "No se pudieron leer los logs");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _rawLogText = string.Empty;
        LogText = string.Empty;
        StatusMessage = LocalizationService.T("Logs_StatusCleared");
        OnPropertyChanged(nameof(HasLogs));
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            LogText = _rawLogText;
        }
        else
        {
            var matches = _rawLogText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            LogText = string.Join(Environment.NewLine, matches);
        }

        OnPropertyChanged(nameof(HasLogs));
    }
}
