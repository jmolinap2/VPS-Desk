namespace VpsDesk.Application.Updates;

/// <summary>Contenido de version.json publicado junto al APK.</summary>
public sealed record AppUpdateManifest(
    int VersionCode,
    string VersionName,
    string ApkUrl,
    string? Sha256,
    string? Notes);

/// <summary>
/// Lo que cada plataforma sabe hacer con las actualizaciones. Android descarga e instala el APK;
/// el escritorio no declara soporte (IsSupported = false) y la interfaz oculta la sección.
/// </summary>
public interface IAppUpdatePlatform
{
    bool IsSupported { get; }
    int CurrentVersionCode { get; }
    string CurrentVersionName { get; }

    /// <summary>URL de version.json compilada en la app (puede ser null en builds sin canal de actualización).</summary>
    string? DefaultManifestUrl { get; }

    /// <summary>Carpeta donde se guarda el APK descargado (debe estar expuesta por el FileProvider).</summary>
    string DownloadDirectory { get; }

    /// <summary>
    /// Entrega el APK al instalador del sistema. Devuelve un mensaje para el usuario cuando falta
    /// un permiso (p. ej. "instalar apps desconocidas"); null si el instalador se abrió.
    /// </summary>
    string? Install(string apkPath);
}

public static class AppUpdatePlatform
{
    private sealed class Unsupported : IAppUpdatePlatform
    {
        public bool IsSupported => false;
        public int CurrentVersionCode => 0;
        public string CurrentVersionName => string.Empty;
        public string? DefaultManifestUrl => null;
        public string DownloadDirectory => Path.GetTempPath();
        public string? Install(string apkPath) => "Esta plataforma no admite actualización automática.";
    }

    /// <summary>Escritorio no lo cambia; Android lo sustituye al arrancar.</summary>
    public static IAppUpdatePlatform Current { get; set; } = new Unsupported();
}
