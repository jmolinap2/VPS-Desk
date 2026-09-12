using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Files;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class FilesViewModel : ObservableObject
{
    private readonly IRemoteFileService _files;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private DateTimeOffset? _lastRefreshUtc;

    public ObservableCollection<RemoteFileEntry> Entries { get; } = new();

    [ObservableProperty] private string _currentPath = "/";
    [ObservableProperty] private RemoteFileEntry? _selectedEntry;
    [ObservableProperty] private string _editorPath = string.Empty;
    [ObservableProperty] private string _editorContent = string.Empty;
    [ObservableProperty] private string _statusMessage = "Open Files to browse the active VPS over SFTP.";
    [ObservableProperty] private string _lastUpdated = "Never";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasOpenTextFile;
    [ObservableProperty] private bool _hasPendingSave;
    [ObservableProperty] private string _pendingSaveMessage = string.Empty;

    public bool CanGoParent => CurrentPath != "/";

    public FilesViewModel(
        IRemoteFileService files,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _files = files;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

    partial void OnCurrentPathChanged(string value) => OnPropertyChanged(nameof(CanGoParent));

    partial void OnSelectedEntryChanged(RemoteFileEntry? value)
    {
        if (HasPendingSave) CancelPendingSave();
    }

    public async Task RefreshIfNeededAsync()
    {
        if (_lastRefreshUtc == null || DateTimeOffset.UtcNow - _lastRefreshUtc > TimeSpan.FromSeconds(20))
        {
            await RefreshAsync();
        }
    }

    public void Reset()
    {
        CurrentPath = "/";
        Entries.Clear();
        SelectedEntry = null;
        EditorPath = string.Empty;
        EditorContent = string.Empty;
        HasOpenTextFile = false;
        LastUpdated = "Never";
        StatusMessage = "Select an active server, then browse files.";
        _lastRefreshUtc = null;
        CancelPendingSave();
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
        StatusMessage = $"Reading {CurrentPath} over SFTP...";
        var selectedPath = SelectedEntry?.FullPath;

        try
        {
            var entries = await _files.ListAsync(server, CurrentPath, _secretAccessor());
            Entries.Clear();
            foreach (var entry in entries) Entries.Add(entry);

            SelectedEntry = selectedPath == null
                ? Entries.FirstOrDefault()
                : Entries.FirstOrDefault(x => x.FullPath == selectedPath) ?? Entries.FirstOrDefault();

            _lastRefreshUtc = DateTimeOffset.UtcNow;
            LastUpdated = DateTimeOffset.Now.ToString("HH:mm:ss");
            StatusMessage = $"{Entries.Count} entries · SFTP · read-only browsing until a text file is explicitly saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to browse files: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NavigatePathAsync()
    {
        CurrentPath = NormalizePath(CurrentPath);
        _lastRefreshUtc = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task GoParentAsync()
    {
        if (!CanGoParent) return;
        var normalized = NormalizePath(CurrentPath);
        var lastSlash = normalized.LastIndexOf('/');
        CurrentPath = lastSlash <= 0 ? "/" : normalized[..lastSlash];
        _lastRefreshUtc = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenSelectedAsync()
    {
        var entry = SelectedEntry;
        if (entry == null) return;

        if (entry.IsDirectory)
        {
            CurrentPath = NormalizePath(entry.FullPath);
            _lastRefreshUtc = null;
            await RefreshAsync();
            return;
        }

        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "No active server.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Reading {entry.FullPath}...";
        try
        {
            var content = await _files.ReadTextAsync(server, entry.FullPath, _secretAccessor());
            if (content == null)
            {
                StatusMessage = "The file no longer exists.";
                return;
            }

            EditorPath = entry.FullPath;
            EditorContent = content;
            HasOpenTextFile = true;
            StatusMessage = $"Opened {entry.Name}. Text editing is limited to UTF-8 files up to 2 MB.";
            CancelPendingSave();
        }
        catch (Exception ex)
        {
            HasOpenTextFile = false;
            StatusMessage = $"Unable to open file: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RequestSave()
    {
        if (!HasOpenTextFile || string.IsNullOrWhiteSpace(EditorPath))
        {
            StatusMessage = "Open a text file before saving.";
            return;
        }

        HasPendingSave = true;
        var productionWarning = _serverAccessor()?.Environment == ServerEnvironment.Production
            ? " This is a Production server; saving replaces the remote file contents."
            : " Saving replaces the remote file contents.";
        PendingSaveMessage = $"Confirm save to '{EditorPath}'.{productionWarning}";
    }

    [RelayCommand]
    private async Task ConfirmSaveAsync()
    {
        if (!HasPendingSave || !HasOpenTextFile || string.IsNullOrWhiteSpace(EditorPath)) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "The active server changed. Save cancelled.";
            CancelPendingSave();
            return;
        }

        IsBusy = true;
        StatusMessage = $"Saving {EditorPath} over SFTP...";
        try
        {
            await _files.WriteTextAsync(server, EditorPath, EditorContent, _secretAccessor());
            StatusMessage = $"Saved {EditorPath}.";
            CancelPendingSave();
            _lastRefreshUtc = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to save file: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelSave() => CancelPendingSave();

    [RelayCommand]
    private void CloseEditor()
    {
        EditorPath = string.Empty;
        EditorContent = string.Empty;
        HasOpenTextFile = false;
        CancelPendingSave();
        StatusMessage = "Editor closed. No remote changes were made.";
    }

    private void CancelPendingSave()
    {
        HasPendingSave = false;
        PendingSaveMessage = string.Empty;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Trim().Replace('\\', '/');
        if (!path.StartsWith('/')) path = "/" + path;
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
