using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace VpsDesk.Android;

[Application]
public sealed class MainApplication : AvaloniaAndroidApplication<VpsDeskAndroidApp>
{
    public MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
