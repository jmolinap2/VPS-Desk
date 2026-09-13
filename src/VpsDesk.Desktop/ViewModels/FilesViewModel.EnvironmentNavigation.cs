namespace VpsDesk.Desktop.ViewModels;

public partial class FilesViewModel
{
    public async Task OpenEnvironmentFileAsync(string remoteRepositoryPath, string environmentFileName)
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(remoteRepositoryPath))
        {
            StatusMessage = "No hay una ruta de proyecto configurada. Ve a Despliegues y detecta o indica la ruta del proyecto primero.";
            return;
        }

        var projectPath = NormalizePath(remoteRepositoryPath);
        var envName = string.IsNullOrWhiteSpace(environmentFileName) ? ".env" : environmentFileName.Trim();
        var fullPath = envName.StartsWith('/', StringComparison.Ordinal)
            ? NormalizePath(envName)
            : NormalizePath($"{projectPath}/{envName}");

        var lastSlash = fullPath.LastIndexOf('/');
        var directory = lastSlash <= 0 ? "/" : fullPath[..lastSlash];
        var fileName = lastSlash < 0 ? fullPath : fullPath[(lastSlash + 1)..];

        CurrentPath = directory;
        _lastRefreshUtc = null;
        await RefreshAsync();

        var entry = Entries.FirstOrDefault(item =>
            !item.IsDirectory &&
            (string.Equals(NormalizePath(item.FullPath), fullPath, StringComparison.Ordinal) ||
             string.Equals(item.Name, fileName, StringComparison.Ordinal)));

        if (entry == null)
        {
            StatusMessage = $"No se encontró '{fileName}' en {directory}. Puedes abrir Archivos para revisar la ruta o crear el archivo fuera de VPS Desk por ahora.";
            return;
        }

        SelectedEntry = entry;
        await OpenFileAsync(entry, readOnly: false);
        StatusMessage = $"Administrando variables de {fullPath}. Los cambios se aplican al VPS solo cuando confirmas Guardar.";
    }
}
