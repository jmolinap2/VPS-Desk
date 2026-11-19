using CommunityToolkit.Mvvm.Input;
using VpsDesk.Domain.Files;

namespace VpsDesk.Desktop.ViewModels;

public partial class FilesViewModel
{
    private string? _environmentProjectPath;
    private string? _environmentFileName;

    [RelayCommand]
    private Task RetryEnvironmentAsync() => OpenEnvironmentFileAsync(_environmentProjectPath ?? string.Empty, _environmentFileName ?? ".env");

    public async Task OpenEnvironmentFileAsync(string remoteRepositoryPath, string environmentFileName)
    {
        if (IsBusy) return;
        _environmentProjectPath = remoteRepositoryPath;
        _environmentFileName = environmentFileName;

        if (string.IsNullOrWhiteSpace(remoteRepositoryPath))
        {
            StatusMessage = "No hay una ruta de proyecto configurada. Ve a Despliegues y detecta o indica la ruta del proyecto primero.";
            return;
        }

        var projectPath = NormalizePath(remoteRepositoryPath);
        var envName = string.IsNullOrWhiteSpace(environmentFileName) ? ".env" : environmentFileName.Trim();
        var fullPath = envName.StartsWith("/", StringComparison.Ordinal)
            ? NormalizePath(envName)
            : NormalizePath($"{projectPath}/{envName}");

        if (HasOpenTextFile && string.Equals(EditorPath, fullPath, StringComparison.Ordinal)) return;
        if (IsEditorDirty)
        {
            StatusMessage = "Hay cambios sin guardar en el archivo abierto. Guárdalos antes de abrir otro archivo de entorno.";
            return;
        }

        var serverId = _serverAccessor()?.Id;
        var lastSlash = fullPath.LastIndexOf('/');
        var directory = lastSlash <= 0 ? "/" : fullPath[..lastSlash];
        var fileName = lastSlash < 0 ? fullPath : fullPath[(lastSlash + 1)..];
        CurrentPath = directory;
        var entry = new RemoteFileEntry(fileName, fullPath, false, 0, DateTimeOffset.MinValue);
        await OpenFileAsync(entry, readOnly: false, forceEnvironment: true);
        if (_serverAccessor()?.Id == serverId && HasOpenTextFile && EditorPath == fullPath)
            StatusMessage = $"Administrando variables de {fullPath}. Los cambios se aplican al VPS solo cuando confirmas Guardar.";
    }
}
