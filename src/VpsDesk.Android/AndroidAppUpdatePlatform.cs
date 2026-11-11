using Android.Content;
using Android.Content.PM;
using Android.Provider;
using AndroidX.Core.Content;
using VpsDesk.Application.Updates;

namespace VpsDesk.Android;

/// <summary>
/// Actualización in-app: el APK se descarga a la caché de la app y se entrega al instalador de
/// Android mediante un FileProvider. Android siempre pide confirmación al usuario, y solo acepta
/// el APK si está firmado con la MISMA clave que la versión instalada.
/// </summary>
internal sealed class AndroidAppUpdatePlatform : IAppUpdatePlatform
{
    internal const string Authority = "com.vpsdesk.app.fileprovider";

    private readonly Context _context;

    public AndroidAppUpdatePlatform(Context context)
    {
        _context = context;
        var info = context.PackageManager!.GetPackageInfo(context.PackageName!, 0)!;
        CurrentVersionName = info.VersionName ?? "?";
        CurrentVersionCode = (int)info.LongVersionCode;
        DownloadDirectory = Path.Combine(context.CacheDir!.AbsolutePath, "updates");
        DefaultManifestUrl = typeof(AndroidAppUpdatePlatform).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "VpsDesk.UpdateUrl")?.Value;
    }

    public bool IsSupported => true;
    public int CurrentVersionCode { get; }
    public string CurrentVersionName { get; }
    public string? DefaultManifestUrl { get; }
    public string DownloadDirectory { get; }

    public string? Install(string apkPath)
    {
        // Android 8+: instalar APK desde una app exige que el usuario lo autorice una vez
        // ("Instalar apps desconocidas" para VPS Desk).
        if (!_context.PackageManager!.CanRequestPackageInstalls())
        {
            var settings = new Intent(Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + _context.PackageName))
                .AddFlags(ActivityFlags.NewTask);
            _context.StartActivity(settings);
            return "Activa «Permitir de esta fuente» para VPS Desk en los ajustes que se abrieron y vuelve a pulsar Instalar.";
        }

        var uri = FileProvider.GetUriForFile(_context, Authority, new Java.IO.File(apkPath));
        var intent = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        _context.StartActivity(intent);
        return null;
    }
}
