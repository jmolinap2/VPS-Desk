using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private bool _isDownloading;

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

            if (_available is null)
            {
                if (!silent) StatusMessage = $"Estás al día. Versión instalada: {InstalledVersion}.";
                return;
            }

            StatusMessage = $"Hay una versión nueva: {_available.VersionName} (instalada: {_platform.CurrentVersionName}).";
            if (silent)
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
