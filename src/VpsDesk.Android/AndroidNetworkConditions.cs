using Android.Content;
using Android.Net;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Android;

/// <summary>
/// Red medida según Android: datos móviles o un wifi que el usuario marcó como "de uso medido".
/// Requiere ACCESS_NETWORK_STATE, ya declarado en el manifiesto.
/// </summary>
internal sealed class AndroidNetworkConditions(Context context) : INetworkConditions
{
    public bool IsMetered
    {
        get
        {
            try
            {
                var manager = (ConnectivityManager?)context.GetSystemService(Context.ConnectivityService);
                return manager?.IsActiveNetworkMetered ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
