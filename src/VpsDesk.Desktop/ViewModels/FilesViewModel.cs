using System.Collections.ObjectModel;
using System.Text.Json;
using System.Xml.Linq;
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
    private string _originalEditorContent = string.Empty;

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
    [ObservableProperty] private bool _isEditorDirty;
    [ObservableProperty] private bool _isEditorValid = true;
    [ObservableProperty] private string _editorValidationMessage = string.Empty;
    [ObservableProperty] private string _editorFileType = "TEXT";
    [ObservableProperty] private int _editorLineCount;
    [ObservableProperty] private int _editorCharacterCount;
    [ObservableProperty] private bool _createBackupBeforeSave = true;
    [ObservableProperty] private string _lastBackupPath = string.Empty;
    [ObservableProperty] private bool _isSensitiveFile;

    public bool CanGoParent => CurrentPath != "/";
    public bool HasSelection => SelectedEntry is not null;
    public bool CanSaveEditor => HasOpenTextFile && IsEditorDirty && IsEditorValid && !IsBusy;

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
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnEditorContentChanged(string value)
    {
        EditorCharacterCount = value.Length;
        EditorLineCount = value.Length == 0 ? 1 : value.Count(character => character == '\n') + 1;
        IsEditorDirty = HasOpenTextFile && !string.Equals(_originalEditorContent, value, StringComparison.Ordinal);
        ValidateEditorContentCore();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSaveEditor));
    partial void OnHasOpenTextFileChanged(bool value) => OnPropertyChanged(nameof(CanSaveEditor));
    partial void OnIsEditorDirtyChanged(bool value) => OnPropertyChanged(nameof(CanSaveEditor));
    partial void OnIsEditorValidChanged(bool value) => OnPropertyChanged(nameof(CanSaveEditor));

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
        ClearEditorState();
        LastUpdated = "Never";
        StatusMessage = "Select an active server, then browse files.";
        _lastRefreshUtc = null;
        CancelPendingSave();
        OnPropertyChanged(nameof(HasSelection));
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
            StatusMessage = $"{Entries.Count} entries · SFTP · {CurrentPath}";
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
    private Task GoRootAsync() => NavigateQuickAsync("/");

    [RelayCommand]
    private Task GoHomeAsync()
    {
        var username = _serverAccessor()?.Username?.Trim();
        var path = string.Equals(username, "root", StringComparison.OrdinalIgnoreCase)
            ? "/root"
            : string.IsNullOrWhiteSpace(username) ? "/home" : $"/home/{username}";
        return NavigateQuickAsync(path);
    }

    [RelayCommand]
    private Task GoOptAsync() => NavigateQuickAsync("/opt");

    [RelayCommand]
    private Task GoEtcAsync() => NavigateQuickAsync("/etc");

    [RelayCommand]
    private Task GoVarLogAsync() => NavigateQuickAsync("/var/log");

    [RelayCommand]
    private Task GoSrvAsync() => NavigateQuickAsync("/srv");

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
            EditorFileType = DetermineFileType(entry.FullPath);
            IsSensitiveFile = IsPotentiallySensitive(entry.FullPath);
            _originalEditorContent = content;
            HasOpenTextFile = true;
            EditorContent = content;
            IsEditorDirty = false;
            LastBackupPath = string.Empty;
            ValidateEditorContentCore();
            StatusMessage = $"Opened {entry.Name}. Changes remain local until you confirm Save.";
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
    private void ValidateEditor()
    {
        ValidateEditorContentCore();
        StatusMessage = IsEditorValid
            ? $"Validation passed for {Path.GetFileName(EditorPath)}."
            : EditorValidationMessage;
    }

    [RelayCommand]
    private async Task ReloadEditorAsync()
    {
        if (!HasOpenTextFile || string.IsNullOrWhiteSpace(EditorPath) || IsBusy) return;
        if (IsEditorDirty)
        {
            StatusMessage = "Reload blocked because there are unsaved local changes. Save or close the editor first.";
            return;
        }

        var server = _serverAccessor();
        if (server == null) return;

        IsBusy = true;
        try
        {
            var content = await _files.ReadTextAsync(server, EditorPath, _secretAccessor());
            if (content == null)
            {
                StatusMessage = "The remote file no longer exists.";
                return;
            }

            _originalEditorContent = content;
            EditorContent = content;
            IsEditorDirty = false;
            ValidateEditorContentCore();
            StatusMessage = $"Reloaded {EditorPath} from the VPS.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to reload file: {ex.Message}";
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

        if (!IsEditorDirty)
        {
            StatusMessage = "There are no local changes to save.";
            return;
        }

        ValidateEditorContentCore();
        if (!IsEditorValid)
        {
            StatusMessage = $"Save blocked: {EditorValidationMessage}";
            return;
        }

        HasPendingSave = true;
        var productionWarning = _serverAccessor()?.Environment == ServerEnvironment.Production
            ? " This is a Production server; saving replaces the remote file contents."
            : " Saving replaces the remote file contents.";
        var backupNote = CreateBackupBeforeSave
            ? " A timestamped .bak copy will be created first."
            : " Backup creation is disabled for this save.";
        PendingSaveMessage = $"Confirm save to '{EditorPath}'.{productionWarning}{backupNote}";
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

        ValidateEditorContentCore();
        if (!IsEditorValid)
        {
            StatusMessage = $"Save blocked: {EditorValidationMessage}";
            CancelPendingSave();
            return;
        }

        IsBusy = true;
        StatusMessage = $"Checking remote version of {EditorPath}...";
        try
        {
            // Optimistic concurrency: do not silently overwrite a file modified on the VPS
            // after the user opened it in VPS Desk.
            var remoteCurrent = await _files.ReadTextAsync(server, EditorPath, _secretAccessor());
            if (remoteCurrent == null)
            {
                StatusMessage = "Save cancelled because the remote file no longer exists.";
                CancelPendingSave();
                return;
            }

            if (!string.Equals(remoteCurrent, _originalEditorContent, StringComparison.Ordinal))
            {
                StatusMessage = "Save blocked: the file changed on the VPS after you opened it. Close/reopen or reload it before editing again.";
                CancelPendingSave();
                return;
            }

            LastBackupPath = string.Empty;
            if (CreateBackupBeforeSave)
            {
                LastBackupPath = await _files.CreateBackupAsync(server, EditorPath, _secretAccessor()) ?? string.Empty;
            }

            StatusMessage = $"Saving {EditorPath} over SFTP...";
            await _files.WriteTextAsync(server, EditorPath, EditorContent, _secretAccessor());
            _originalEditorContent = EditorContent;
            IsEditorDirty = false;
            StatusMessage = string.IsNullOrWhiteSpace(LastBackupPath)
                ? $"Saved {EditorPath}."
                : $"Saved {EditorPath}. Backup: {LastBackupPath}";
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
        if (IsEditorDirty)
        {
            StatusMessage = "Editor has unsaved changes. Save them before closing, or undo the edits manually.";
            return;
        }

        ClearEditorState();
        CancelPendingSave();
        StatusMessage = "Editor closed. No remote changes were made.";
    }

    private async Task NavigateQuickAsync(string path)
    {
        if (IsBusy) return;
        CurrentPath = NormalizePath(path);
        _lastRefreshUtc = null;
        await RefreshAsync();
    }

    private void ValidateEditorContentCore()
    {
        if (!HasOpenTextFile)
        {
            IsEditorValid = true;
            EditorValidationMessage = string.Empty;
            return;
        }

        try
        {
            switch (EditorFileType)
            {
                case "JSON":
                    using (JsonDocument.Parse(EditorContent, new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    }))
                    {
                    }
                    EditorValidationMessage = "JSON válido.";
                    break;
                case "XML":
                    XDocument.Parse(EditorContent, LoadOptions.PreserveWhitespace);
                    EditorValidationMessage = "XML válido.";
                    break;
                default:
                    EditorValidationMessage = "Texto UTF-8. Este tipo no requiere validación estructural.";
                    break;
            }

            IsEditorValid = true;
        }
        catch (JsonException ex)
        {
            IsEditorValid = false;
            EditorValidationMessage = $"JSON inválido · línea {(ex.LineNumber ?? 0) + 1}, posición {(ex.BytePositionInLine ?? 0) + 1}: {ex.Message}";
        }
        catch (Exception ex) when (EditorFileType == "XML")
        {
            IsEditorValid = false;
            EditorValidationMessage = $"XML inválido: {ex.Message}";
        }
    }

    private void ClearEditorState()
    {
        EditorPath = string.Empty;
        _originalEditorContent = string.Empty;
        HasOpenTextFile = false;
        EditorContent = string.Empty;
        EditorFileType = "TEXT";
        EditorValidationMessage = string.Empty;
        IsEditorValid = true;
        IsEditorDirty = false;
        EditorLineCount = 0;
        EditorCharacterCount = 0;
        LastBackupPath = string.Empty;
        IsSensitiveFile = false;
    }

    private void CancelPendingSave()
    {
        HasPendingSave = false;
        PendingSaveMessage = string.Empty;
    }

    private static string DetermineFileType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => "JSON",
            ".xml" or ".config" => "XML",
            ".yml" or ".yaml" => "YAML",
            ".ini" => "INI",
            ".env" => "ENV",
            ".cs" => "C#",
            ".js" => "JS",
            ".ts" => "TS",
            ".html" => "HTML",
            ".css" => "CSS",
            ".md" => "Markdown",
            _ => "TEXT"
        };
    }

    private static bool IsPotentiallySensitive(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
               || fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase);
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
