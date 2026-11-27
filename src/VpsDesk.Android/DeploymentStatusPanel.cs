using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Android;

/// <summary>
/// Vista nativa del pase en curso: paso actual, avance real, tiempo transcurrido, últimas líneas de la salida y los
/// botones «Abortar pase» (pide confirmar) y «Abrir VPS Desk». La comparten la burbuja de notificación
/// (<see cref="BubbleActivity"/>) y la burbuja flotante propia (<see cref="DeploymentOverlay"/>).
/// Es nativa a propósito: la interfaz de Avalonia vive en <see cref="MainActivity"/> y no puede incrustarse en una
/// ventana flotante. Lee el estado de <see cref="DeploymentBubble"/>; quien la usa solo llama a <see cref="Apply"/>.
/// </summary>
internal sealed class DeploymentStatusPanel : LinearLayout
{
    private readonly TextView _elapsed;
    private readonly TextView _step;
    private readonly TextView _percent;
    private readonly ProgressBar _progress;
    private readonly TextView _status;
    private readonly TextView _tail;
    private readonly Button _abort;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _tick;
    private readonly Action _resetAbort;
    private bool _ticking;
    private bool _confirming;

    public DeploymentStatusPanel(Context context, Action openApp) : base(context)
    {
        Orientation = global::Android.Widget.Orientation.Vertical;
        var padding = Dp(20);
        SetPadding(padding, Dp(18), padding, padding);
        SetBackgroundColor(Color.ParseColor("#0F1722"));

        var header = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        var title = new TextView(context) { Text = "VPS Desk · Pase" };
        title.SetTextColor(Color.White);
        title.SetTypeface(title.Typeface, TypefaceStyle.Bold);
        title.TextSize = 18;
        header.AddView(title, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _elapsed = new TextView(context);
        _elapsed.SetTextColor(Color.ParseColor("#8FA0B8"));
        _elapsed.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _elapsed.TextSize = 14;
        header.AddView(_elapsed);
        AddView(header);

        _step = new TextView(context);
        _step.SetTextColor(Color.ParseColor("#E2E8F0"));
        _step.TextSize = 15;
        _step.SetPadding(0, Dp(12), 0, Dp(8));
        AddView(_step);

        var progressRow = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        progressRow.SetGravity(GravityFlags.CenterVertical);
        _progress = new ProgressBar(context, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
        _progress.ProgressTintList = ColorStateList.ValueOf(Color.ParseColor("#38BDF8"));
        progressRow.AddView(_progress, new LayoutParams(0, Dp(10), 1f));
        _percent = new TextView(context);
        _percent.SetTextColor(Color.ParseColor("#C9D4E3"));
        _percent.SetTypeface(Typeface.Monospace, TypefaceStyle.Bold);
        _percent.TextSize = 13;
        _percent.SetPadding(Dp(10), 0, 0, 0);
        progressRow.AddView(_percent);
        AddView(progressRow);

        _status = new TextView(context);
        _status.SetTextColor(Color.ParseColor("#8FA0B8"));
        _status.TextSize = 13;
        _status.SetPadding(0, Dp(8), 0, 0);
        AddView(_status);

        _tail = new TextView(context);
        _tail.SetTextColor(Color.ParseColor("#9FB0C6"));
        _tail.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _tail.TextSize = 11.5f;
        _tail.SetMaxLines(6);
        _tail.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        _tail.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        var tailBackground = new GradientDrawable();
        tailBackground.SetColor(Color.ParseColor("#070A0F"));
        tailBackground.SetCornerRadius(Dp(10));
        _tail.Background = tailBackground;
        AddView(_tail, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(12) });

        var buttons = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        _abort = new Button(context) { Text = "Abortar pase" };
        _abort.SetTextColor(Color.White);
        _abort.SetBackgroundColor(Color.ParseColor("#B91C1C"));
        _abort.Click += (_, _) => OnAbortClicked();
        buttons.AddView(_abort, new LayoutParams(0, Dp(48), 1f) { RightMargin = Dp(4) });
        var open = new Button(context) { Text = "Abrir VPS Desk" };
        open.Click += (_, _) => openApp();
        buttons.AddView(open, new LayoutParams(0, Dp(48), 1f) { LeftMargin = Dp(4) });
        AddView(buttons, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(16) });

        _tick = Tick;
        _resetAbort = () => ResetAbort();
    }

    /// <summary>Refresca la vista con el estado actual del pase.</summary>
    public void Apply(string message, bool finished)
    {
        var hasMessage = !string.IsNullOrWhiteSpace(message);
        var running = !finished && hasMessage;
        var step = DeploymentBubble.Step;
        var percent = (int)DeploymentBubble.Percent;

        // Un pase en marcha muestra el paso; el resultado final (o el último estado) va en la línea de estado.
        _step.Text = running && step.Length > 0 ? step : hasMessage ? message : "No hay un pase en curso.";
        _status.Visibility = running && step.Length > 0 && message != step ? ViewStates.Visible : ViewStates.Gone;
        _status.Text = message;

        _progress.Visibility = hasMessage ? ViewStates.Visible : ViewStates.Gone;
        _progress.Indeterminate = running && percent <= 0;
        if (!_progress.Indeterminate) _progress.Progress = finished ? 100 : percent;
        _percent.Text = running && percent <= 0 ? string.Empty : $"{(finished ? 100 : percent)}%";

        var tail = DeploymentBubble.Tail;
        _tail.Visibility = tail.Length > 0 ? ViewStates.Visible : ViewStates.Gone;
        _tail.Text = tail;

        _abort.Visibility = running ? ViewStates.Visible : ViewStates.Gone;
        if (running && !_abort.Enabled && !message.StartsWith("Cancelando", StringComparison.OrdinalIgnoreCase))
        {
            // Un pase nuevo en la misma vista vuelve a ofrecer el botón.
            _abort.Enabled = true;
            ResetAbort();
        }

        UpdateElapsed();
        if (running) StartTicking();
    }

    /// <summary>Pulsación larga en la burbuja: deja «Abortar» a un toque de confirmarse.</summary>
    public void ArmAbort()
    {
        if (_abort.Visibility == ViewStates.Visible && _abort.Enabled && !_confirming) OnAbortClicked();
    }

    // Abortar un pase es irreversible y el botón queda al alcance del pulgar: el primer toque pide confirmar.
    private void OnAbortClicked()
    {
        if (!_confirming)
        {
            _confirming = true;
            _abort.Text = "¿Seguro? Toca otra vez";
            _abort.SetBackgroundColor(Color.ParseColor("#92400E"));
            _handler.PostDelayed(_resetAbort, 4000);
            return;
        }

        _confirming = false;
        _abort.Enabled = false;
        _abort.Text = "Cancelando…";
        DeploymentBackgroundExecution.RequestAbort?.Invoke();
    }

    private void ResetAbort()
    {
        _confirming = false;
        if (!_abort.Enabled) return;
        _abort.Text = "Abortar pase";
        _abort.SetBackgroundColor(Color.ParseColor("#B91C1C"));
    }

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _handler.PostDelayed(_tick, 1000);
    }

    private void Tick()
    {
        if (!_ticking) return;
        UpdateElapsed();
        if (DeploymentBubble.Finished || DeploymentBubble.FinishedAtMs > 0)
        {
            _ticking = false;
            return;
        }
        _handler.PostDelayed(_tick, 1000);
    }

    private void UpdateElapsed()
    {
        var started = DeploymentBubble.StartedAtMs;
        if (started <= 0)
        {
            _elapsed.Text = string.Empty;
            return;
        }
        var end = DeploymentBubble.FinishedAtMs > 0 ? DeploymentBubble.FinishedAtMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var span = TimeSpan.FromMilliseconds(Math.Max(0, end - started));
        _elapsed.Text = span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
    }

    protected override void OnDetachedFromWindow()
    {
        _ticking = false;
        base.OnDetachedFromWindow();
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
