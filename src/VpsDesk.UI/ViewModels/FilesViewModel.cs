using VpsDesk.Desktop.Localization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Files;
using VpsDesk.Domain.Servers;
using VpsDesk.Desktop.Services;

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
    [ObservableProperty] private string _statusMessage = LocalizationService.T("Files_StatusInitial");
    [ObservableProperty] private string _lastUpdated = LocalizationService.T("Common_Never");
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
    [ObservableProperty] private bool _isEditorReadOnly;
    [ObservableProperty] private Bitmap? _imagePreview;
    [ObservableProperty] private bool _hasImagePreview;
    [ObservableProperty] private string _imagePreviewName = string.Empty;
    [ObservableProperty] private string _imagePreviewMeta = string.Empty;

    public bool CanGoParent => CurrentPath != "/";
    public bool HasSelection => SelectedEntry is not null;
    public bool CanSaveEditor => HasOpenTextFile && IsEditorDirty && IsEditorValid && !IsBusy && !IsEditorReadOnly;

    public FilesViewModel(
        IRemoteFileService files,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _files = files;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoEntries));
    }

    public bool HasNoEntries => Entries.Count == 0;

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
    partial void OnIsEditorReadOnlyChanged(bool value) => OnPropertyChanged(nameof(CanSaveEditor));

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
        CloseImagePreview();
        LastUpdated = LocalizationService.T("Common_Never");
        StatusMessage = LocalizationService.T("Files_StatusSelectServer");
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
            StatusMessage = LocalizationService.T("Common_NoActiveServer");
            return;
        }

        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Files_StatusReading", CurrentPath);
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
            StatusMessage = LocalizationService.Current.Format("Files_StatusEntries", Entries.Count, CurrentPath);
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Files_StatusBrowseFailed", friendly);
            Toast.Error(friendly, "No se pudieron listar los archivos");
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

        if (entry.IsDirectory) { await OpenDirectoryAsync(entry); return; }
        if (entry.IsImageFile) { await LoadImagePreviewAsync(entry); return; }
        if (entry.CanEdit) { await OpenFileAsync(entry, readOnly: false); return; }
        StatusMessage = $"{entry.Name} no tiene vista previa disponible en VPS Desk.";
    }

    [RelayCommand]
    private async Task PreviewEntryAsync(RemoteFileEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null) return;

        if (entry.IsDirectory) { await OpenDirectoryAsync(entry); return; }
        if (entry.IsImageFile) { await LoadImagePreviewAsync(entry); return; }
        if (!entry.CanPreview)
        {
            StatusMessage = $"{entry.Name} no tiene vista previa disponible en VPS Desk.";
            return;
        }

        await OpenFileAsync(entry, readOnly: true);
    }

    [RelayCommand]
    private async Task EditEntryAsync(RemoteFileEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null) return;

        if (entry.IsDirectory) { await OpenDirectoryAsync(entry); return; }
        if (!entry.CanEdit)
        {
            StatusMessage = $"{entry.Name} no se puede editar como texto.";
            return;
        }

        await OpenFileAsync(entry, readOnly: false);
    }

    [RelayCommand]
    private void SwitchToEditMode()
    {
        if (!HasOpenTextFile || !IsEditorReadOnly) return;
        IsEditorReadOnly = false;
        StatusMessage = $"Editando {Path.GetFileName(EditorPath)}.";
    }

    [RelayCommand]
    private void CloseImagePreview()
    {
        HasImagePreview = false;
        ImagePreview?.Dispose();
        ImagePreview = null;
    }

    private async Task OpenDirectoryAsync(RemoteFileEntry entry)
    {
        CurrentPath = NormalizePath(entry.FullPath);
        _lastRefreshUtc = null;
        await RefreshAsync();
    }

    private async Task LoadImagePreviewAsync(RemoteFileEntry entry)
    {
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = LocalizationService.T("Common_NoActiveServerShort");
            return;
        }

        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Files_StatusReadingFile", entry.FullPath);
        try
        {
            var bytes = await _files.ReadBytesAsync(server, entry.FullPath, _secretAccessor());
            if (bytes == null)
            {
                StatusMessage = LocalizationService.T("Files_StatusGone");
                return;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            ImagePreview?.Dispose();
            ImagePreview = bitmap;
            ImagePreviewName = entry.Name;
            ImagePreviewMeta = $"{entry.SizeLabel} · {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height}";
            HasImagePreview = true;
            StatusMessage = $"Previsualizando {entry.Name}.";
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Files_StatusPreviewFailed", friendly);
            Toast.Error(friendly, "No se pudo previsualizar la imagen");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFileAsync(RemoteFileEntry entry, bool readOnly)
    {
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = LocalizationService.T("Common_NoActiveServerShort");
            return;
        }

        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Files_StatusReadingFile", entry.FullPath);
        try
        {
            var content = await _files.ReadTextAsync(server, entry.FullPath, _secretAccessor());
            if (content == null)
            {
                StatusMessage = LocalizationService.T("Files_StatusGone");
                return;
            }

            EditorPath = entry.FullPath;
            EditorFileType = DetermineFileType(entry.FullPath);
            IsSensitiveFile = entry.IsSensitive;
            IsEditorReadOnly = readOnly;
            _originalEditorContent = content;
            HasOpenTextFile = true;
            EditorContent = content;
            IsEditorDirty = false;
            LastBackupPath = string.Empty;
            ValidateEditorContentCore();
            StatusMessage = readOnly
                ? $"Viendo {entry.Name} en modo solo lectura."
                : $"Opened {entry.Name}. Changes remain local until you confirm Save.";
            CancelPendingSave();
        }
        catch (Exception ex)
        {
            HasOpenTextFile = false;
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Files_StatusOpenFailed", friendly);
            Toast.Error(friendly, "No se pudo abrir el archivo");
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
            StatusMessage = LocalizationService.T("Files_StatusReloadBlocked");
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
                StatusMessage = LocalizationService.T("Files_StatusRemoteGone");
                return;
            }

            _originalEditorContent = content;
            EditorContent = content;
            IsEditorDirty = false;
            ValidateEditorContentCore();
            StatusMessage = LocalizationService.Current.Format("Files_StatusReloaded", EditorPath);
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Files_StatusReloadFailed", friendly);
            Toast.Error(friendly, "No se pudo recargar el archivo");
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
            StatusMessage = LocalizationService.T("Files_StatusOpenFirst");
            return;
        }

        if (!IsEditorDirty)
        {
            StatusMessage = LocalizationService.T("Files_StatusNoChanges");
            return;
        }

        ValidateEditorContentCore();
        if (!IsEditorValid)
        {
            StatusMessage = LocalizationService.Current.Format("Files_StatusSaveBlocked", EditorValidationMessage);
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
            StatusMessage = LocalizationService.T("Files_StatusServerChangedSave");
            CancelPendingSave();
            return;
        }

        ValidateEditorContentCore();
        if (!IsEditorValid)
        {
            StatusMessage = LocalizationService.Current.Format("Files_StatusSaveBlocked", EditorValidationMessage);
            CancelPendingSave();
            return;
        }

        IsBusy = true;
        StatusMessage = LocalizationService.Current.Format("Files_StatusCheckingRemote", EditorPath);
        try
        {
            // Optimistic concurrency: do not silently overwrite a file modified on the VPS
            // after the user opened it in VPS Desk.
            var remoteCurrent = await _files.ReadTextAsync(server, EditorPath, _secretAccessor());
            if (remoteCurrent == null)
            {
                StatusMessage = LocalizationService.T("Files_StatusSaveGone");
                CancelPendingSave();
                return;
            }

            if (!string.Equals(remoteCurrent, _originalEditorContent, StringComparison.Ordinal))
            {
                StatusMessage = LocalizationService.T("Files_StatusSaveConflict");
                CancelPendingSave();
                return;
            }

            LastBackupPath = string.Empty;
            if (CreateBackupBeforeSave)
            {
                LastBackupPath = await _files.CreateBackupAsync(server, EditorPath, _secretAccessor()) ?? string.Empty;
            }

            StatusMessage = LocalizationService.Current.Format("Files_StatusSaving", EditorPath);
            await _files.WriteTextAsync(server, EditorPath, EditorContent, _secretAccessor());
            _originalEditorContent = EditorContent;
            IsEditorDirty = false;
            StatusMessage = string.IsNullOrWhiteSpace(LastBackupPath)
                ? $"Saved {EditorPath}."
                : $"Saved {EditorPath}. Backup: {LastBackupPath}";
            Toast.Success(
                string.IsNullOrWhiteSpace(LastBackupPath)
                    ? $"{EditorPath} se guardó en el servidor."
                    : $"{EditorPath} se guardó en el servidor. Copia previa: {LastBackupPath}",
                "Archivo guardado");
            CancelPendingSave();
            _lastRefreshUtc = null;
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = LocalizationService.Current.Format("Files_StatusSaveFailed", friendly);
            Toast.Error(friendly, "No se pudo guardar el archivo");
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
            StatusMessage = LocalizationService.T("Files_StatusUnsaved");
            return;
        }

        ClearEditorState();
        CancelPendingSave();
        StatusMessage = LocalizationService.T("Files_StatusEditorClosed");
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
        IsEditorReadOnly = false;
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
