using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Android;

/// <summary>
/// Vista nativa y compacta del pase en curso: estado actual y botones «Abortar pase» y «Abrir VPS Desk».
/// La comparten la burbuja de notificación (<see cref="BubbleActivity"/>) y la burbuja flotante propia
/// (<see cref="DeploymentOverlay"/>). Es nativa a propósito: la interfaz de Avalonia vive en <see cref="MainActivity"/>
/// y no puede incrustarse en una ventana flotante.
/// </summary>
internal sealed class DeploymentStatusPanel : LinearLayout
{
    private readonly TextView _status;
    private readonly Button _abort;
    private readonly ProgressBar _progress;

    public DeploymentStatusPanel(Context context, Action openApp) : base(context)
    {
        Orientation = Orientation.Vertical;
        var padding = Dp(20);
        SetPadding(padding, padding, padding, padding);
        SetBackgroundColor(Color.ParseColor("#0F1722"));

        var title = new TextView(context) { Text = "VPS Desk · Pase" };
        title.SetTextColor(Color.White);
        title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
        title.TextSize = 18;
        AddView(title);

        _status = new TextView(context);
        _status.SetTextColor(Color.ParseColor("#C9D4E3"));
        _status.TextSize = 15;
        _status.SetPadding(0, Dp(12), 0, Dp(12));
        AddView(_status);

        _progress = new ProgressBar(context, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Indeterminate = true };
        AddView(_progress, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _abort = new Button(context) { Text = "Abortar pase" };
        _abort.SetTextColor(Color.White);
        _abort.SetBackgroundColor(Color.ParseColor("#B91C1C"));
        _abort.Click += (_, _) =>
        {
            _abort.Enabled = false;
            _abort.Text = "Cancelando…";
            DeploymentBackgroundExecution.RequestAbort?.Invoke();
        };
        AddView(_abort, new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)) { TopMargin = Dp(16) });

        var open = new Button(context) { Text = "Abrir VPS Desk" };
        open.Click += (_, _) => openApp();
        AddView(open, new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)) { TopMargin = Dp(8) });
    }

    public void Apply(string message, bool finished)
    {
        _status.Text = string.IsNullOrWhiteSpace(message) ? "No hay un pase en curso." : message;
        var running = !finished && !string.IsNullOrWhiteSpace(message);
        _progress.Visibility = running ? ViewStates.Visible : ViewStates.Gone;
        _abort.Visibility = running ? ViewStates.Visible : ViewStates.Gone;
        if (running && !_abort.Enabled && !message.StartsWith("Cancelando", StringComparison.OrdinalIgnoreCase))
        {
            // Un pase nuevo en la misma vista vuelve a ofrecer el botón.
            _abort.Enabled = true;
            _abort.Text = "Abortar pase";
        }
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
