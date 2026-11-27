using Android.App;
using Android.OS;

namespace VpsDesk.Android;

/// <summary>
/// Contenido de la burbuja de notificación del pase (Android 11+): el panel nativo con el estado y los botones.
/// La burbuja flotante propia (<see cref="DeploymentOverlay"/>) usa el mismo panel.
/// </summary>
[Activity(
    Exported = false,
    Label = "Pase en curso",
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    AllowEmbedded = true,
    ResizeableActivity = true,
    DocumentLaunchMode = global::Android.Content.PM.DocumentLaunchMode.Always)]
public sealed class BubbleActivity : Activity
{
    private DeploymentStatusPanel? _panel;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _panel = new DeploymentStatusPanel(this, () =>
        {
            var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
            if (launch is not null) StartActivity(launch);
        });
        SetContentView(_panel);
        _panel.Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
    }

    protected override void OnResume()
    {
        base.OnResume();
        DeploymentBubble.Changed += OnChanged;
        DeploymentBubble.ProgressChanged += OnProgress;
        _panel?.Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
    }

    protected override void OnPause()
    {
        DeploymentBubble.Changed -= OnChanged;
        DeploymentBubble.ProgressChanged -= OnProgress;
        base.OnPause();
    }

    private void OnProgress() => RunOnUiThread(() => _panel?.Apply(DeploymentBubble.Message, DeploymentBubble.Finished));

    private void OnChanged(string message, bool finished) => RunOnUiThread(() => _panel?.Apply(message, finished));
}
