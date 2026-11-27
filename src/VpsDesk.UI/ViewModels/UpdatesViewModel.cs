using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Runtime;
using VpsDesk.Application.Updates;
using VpsDesk.Desktop.Services;
using VpsDesk.Infrastructure.Updates;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>Busca, descarga e instala actualizaciones publicadas (solo en plataformas que lo soportan).</summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IAppUpdatePlatform _platform;
    private readonly AppUpdateService _service;
    private readonly string? _manifestUrl;
    private AppUpdateManifest? _available;

    public UpdatesViewModel(IAppUpdatePlatform platform, AppUpdateService service)
    {
        _platform = platform;
        _service = service;
        _manifestUrl = Environment.GetEnvironmentVariable("VPSDESK_UPDATE_URL") is { Length: > 0 } fromEnv
            ? fromEnv
            : platform.DefaultManifestUrl;

        // Si la app acaba de actualizarse, el APK descargado ya no sirve.
        if (IsAvailableOnPlatform) AppUpdateService.CleanDirectory(platform.DownloadDirectory);
        StatusMessage = IsAvailableOnPlatform
            ? $"Versión instalada: {platform.CurrentVersionName} ({platform.CurrentVersionCode})."
            : string.Empty;
    }

    /// <summary>Controla la visibilidad de la tarjeta: solo si la plataforma instala APK y hay canal configurado.</summary>
    public bool IsAvailableOnPlatform => _platform.IsSupported && !string.IsNullOrWhiteSpace(_manifestUrl);

    public string InstalledVersion => $"{_platform.CurrentVersionName} ({_platform.CurrentVersionCode})";

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _releaseNotes = string.Empty;

    /// <summary>Las novedades, una por línea y sin viñeta, para mostrarlas como lista en la pantalla.</summary>
    public IReadOnlyList<string> ReleaseNoteItems { get; private set; } = [];

    public bool HasReleaseNotes => ReleaseNoteItems.Count > 0;

    public string ReleaseNotesTitle => _available is null ? string.Empty : $"Novedades de la versión {_available.VersionName}";

    partial void OnReleaseNotesChanged(string value)
    {
        ReleaseNoteItems = value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('•', '-', '*', ' '))
            .Where(line => line.Length > 0)
            .ToArray();
        OnPropertyChanged(nameof(ReleaseNoteItems));
        OnPropertyChanged(nameof(HasReleaseNotes));
        OnPropertyChanged(nameof(ReleaseNotesTitle));
    }
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private bool _isDownloading;

    /// <summary>La hoja con las novedades que se abre al detectar una versión nueva.</summary>
    [ObservableProperty] private bool _isPromptOpen;

    /// <summary>
    /// La hoja solo se abre si la app está en reposo (p. ej. sin un pase corriendo). Si es null o devuelve false,
    /// queda el aviso breve de siempre y la tarjeta de Ajustes.
    /// </summary>
    public Func<bool>? IsIdle { get; set; }

    private static string SkippedVersionPath => Path.Combine(VpsDeskDataPaths.RoamingRoot, "skipped-update.txt");

    private static int ReadSkippedVersion()
    {
        try { return int.TryParse(File.ReadAllText(SkippedVersionPath).Trim(), out var code) ? code : 0; }
        catch (Exception) { return 0; }
    }

    // Una sola vez por sesión: cerrar la hoja sin elegir no la vuelve a abrir hasta el siguiente inicio.
    private bool _promptShownThisSession;

    private bool TryOpenPrompt()
    {
        if (_available is null || _promptShownThisSession) return false;
        if (_available.VersionCode <= ReadSkippedVersion() || IsIdle?.Invoke() != true) return false;
        _promptShownThisSession = true;
        IsPromptOpen = true;
        return true;
    }

    /// <summary>«Atrás» del sistema: cierra la hoja si está abierta.</summary>
    public bool TryDismissTopLayer()
    {
        if (!IsPromptOpen || IsDownloading) return false;
        IsPromptOpen = false;
        return true;
    }

    [RelayCommand]
    private void DismissPrompt()
    {
        if (!IsDownloading) IsPromptOpen = false;
    }

    [RelayCommand]
    private void SkipVersion()
    {
        if (_available is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SkippedVersionPath)!);
                File.WriteAllText(SkippedVersionPath, _available.VersionCode.ToString());
            }
            catch (Exception) { /* sin poder recordarlo, la hoja volverá a salir: molesto pero inofensivo */ }
        }
        IsPromptOpen = false;
    }

    [RelayCommand]
    private async Task InstallFromPromptAsync()
    {
        // La hoja sigue abierta durante la descarga para mostrar el avance, y se cierra al terminar.
        await InstallCommand.ExecuteAsync(null);
        IsPromptOpen = false;
    }

    [NotifyPropertyChangedFor(nameof(InstallLabel))]
    [ObservableProperty] private bool _isUpdateAvailable;

    public string InstallLabel => _available is null ? "Instalar actualización" : $"Instalar {_available.VersionName}";

    /// <summary>Consulta silenciosa al abrir la app: avisa con un toast solo si hay algo nuevo.</summary>
    public async Task CheckOnStartupAsync()
    {
        if (!IsAvailableOnPlatform) return;
        await CheckCoreAsync(silent: true);
    }

    [RelayCommand]
    private Task CheckAsync() => CheckCoreAsync(silent: false);

    private async Task CheckCoreAsync(bool silent)
    {
        if (!IsAvailableOnPlatform || IsBusy) return;
        IsBusy = true;
        try
        {
            if (!silent) StatusMessage = "Buscando actualizaciones…";
            _available = await _service.CheckAsync(_manifestUrl!, _platform.CurrentVersionCode);
            IsUpdateAvailable = _available is not null;
            OnPropertyChanged(nameof(InstallLabel));
            ReleaseNotes = _available?.Notes ?? string.Empty;
            OnPropertyChanged(nameof(ReleaseNotesTitle));

            if (_available is null)
            {
                if (!silent) StatusMessage = $"Estás al día. Versión instalada: {InstalledVersion}.";
                return;
            }

            StatusMessage = $"Hay una versión nueva: {_available.VersionName} (instalada: {_platform.CurrentVersionName}).";
            if (silent && !TryOpenPrompt())
            {
                Toast.Info($"Versión {_available.VersionName} disponible. Instálala desde Configuración.", "Actualización disponible");
            }
        }
        catch (Exception ex)
        {
            // En el chequeo silencioso (sin red, sitio caído) no molestamos al usuario.
            if (!silent)
            {
                StatusMessage = $"No se pudo comprobar: {UserFacingErrors.Describe(ex)}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_available is null || IsBusy) return;
        IsBusy = true;
        IsDownloading = true;
        DownloadPercent = 0;
        try
        {
            StatusMessage = $"Descargando {_available.VersionName}…";
            var progress = new Progress<double>(p => DownloadPercent = Math.Round(p * 100));
            var apk = await _service.DownloadAsync(_available, _platform.DownloadDirectory, progress);

            var problem = _platform.Install(apk);
            StatusMessage = problem ?? "Descarga completa. Confirma la instalación en el diálogo de Android.";
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = $"No se pudo actualizar: {friendly}";
            Toast.Error(friendly, "No se pudo actualizar");
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
        }
    }
}
