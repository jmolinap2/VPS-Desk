using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Android;

/// <summary>
/// Contenido de la burbuja flotante del pase: estado actual y botones «Abortar pase» y «Abrir VPS Desk».
/// Es una pantalla nativa y mínima a propósito: la interfaz de Avalonia vive en <see cref="MainActivity"/> y no debe
/// duplicarse dentro de una burbuja.
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
    private TextView? _status;
    private Button? _abort;
    private ProgressBar? _progress;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var padding = Dp(20);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(padding, padding, padding, padding);
        root.SetBackgroundColor(Color.ParseColor("#0F1722"));

        var title = new TextView(this) { Text = "VPS Desk · Pase" };
        title.SetTextColor(Color.White);
        title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
        title.TextSize = 18;
        root.AddView(title);

        _status = new TextView(this);
        _status.SetTextColor(Color.ParseColor("#C9D4E3"));
        _status.TextSize = 15;
        _status.SetPadding(0, Dp(12), 0, Dp(12));
        root.AddView(_status);

        _progress = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Indeterminate = true };
        root.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _abort = new Button(this) { Text = "Abortar pase" };
        _abort.SetTextColor(Color.White);
        _abort.SetBackgroundColor(Color.ParseColor("#B91C1C"));
        _abort.Click += (_, _) =>
        {
            _abort.Enabled = false;
            _abort.Text = "Cancelando…";
            DeploymentBackgroundExecution.RequestAbort?.Invoke();
        };
        root.AddView(_abort, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)) { TopMargin = Dp(16) });

        var open = new Button(this) { Text = "Abrir VPS Desk" };
        open.Click += (_, _) =>
        {
            var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
            if (launch is not null) StartActivity(launch);
        };
        root.AddView(open, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)) { TopMargin = Dp(8) });

        SetContentView(root);
        Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
    }

    protected override void OnResume()
    {
        base.OnResume();
        DeploymentBubble.Changed += OnChanged;
        Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
    }

    protected override void OnPause()
    {
        DeploymentBubble.Changed -= OnChanged;
        base.OnPause();
    }

    private void OnChanged(string message, bool finished) => RunOnUiThread(() => Apply(message, finished));

    private void Apply(string message, bool finished)
    {
        if (_status is null || _abort is null || _progress is null) return;
        _status.Text = string.IsNullOrWhiteSpace(message) ? "No hay un pase en curso." : message;
        var running = !finished && !string.IsNullOrWhiteSpace(message);
        _progress.Visibility = running ? ViewStates.Visible : ViewStates.Gone;
        _abort.Visibility = running ? ViewStates.Visible : ViewStates.Gone;
        if (running && !_abort.Enabled && !message.StartsWith("Cancelando", StringComparison.OrdinalIgnoreCase))
        {
            // Un pase nuevo en la misma burbuja vuelve a ofrecer el botón.
            _abort.Enabled = true;
            _abort.Text = "Abortar pase";
        }
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
