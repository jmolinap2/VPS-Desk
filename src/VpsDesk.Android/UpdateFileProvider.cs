using Android.App;
using Android.Content;
using AndroidX.Core.Content;

namespace VpsDesk.Android;

/// <summary>Expone a la caché de la app (carpeta "updates") para que el instalador de Android lea el APK.</summary>
[ContentProvider(
    new[] { AndroidAppUpdatePlatform.Authority },
    Name = "com.vpsdesk.app.UpdateFileProvider",
    Exported = false,
    GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_file_paths")]
public class UpdateFileProvider : FileProvider
{
}
