using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using VpsDesk.Application.Logging;
using VpsDesk.Desktop.Services;
using Settings = Android.Provider.Settings;

namespace VpsDesk.Android;

/// <summary>
/// Burbuja flotante propia del pase, al estilo de las «cabezas de chat» de Messenger: un círculo arrastrable sobre
/// cualquier otra app que, al tocarlo, abre un panel compacto con el estado, «Abortar» y «Abrir VPS Desk».
/// Se ve mientras VPS Desk no está en primer plano y hay un pase corriendo (o recién terminado y sin atender).
/// Necesita el permiso «Mostrar sobre otras apps»; sin él no hace nada y queda la burbuja de notificación
/// (<see cref="DeploymentBubble"/>). Todo el manejo de ventanas ocurre en el hilo principal.
/// </summary>
internal static class DeploymentOverlay
{
    private const int BubbleDp = 60;
    private const int MarginDp = 6;
    private const int CloseDp = 64;
    private const int CloseBottomDp = 72;

    private static readonly Handler Main = new(Looper.MainLooper!);

    private static IWindowManager? _windows;
    private static OverlayBubbleView? _bubble;
    private static WindowManagerLayoutParams? _bubbleParams;
    private static DeploymentStatusPanel? _panel;
    private static TextView? _closeTarget;
    private static bool _dismissed;
    private static bool _overClose;
    private static int _x = int.MinValue;
    private static int _y;
    private static long _panelClosedOutsideAt;
    private static float _downRawX, _downRawY;
    private static int _downX, _downY;
    private static bool _dragging;
    private static bool _alerted;
    private static bool _longPressed;
    private static int _pressId;

    private static Context AppContext => global::Android.App.Application.Context;

    public static bool CanDraw(Context context) => Settings.CanDrawOverlays(context);

    /// <summary>Muestra la burbuja aunque el usuario la hubiera descartado (botón «Minimizar»).</summary>
    public static void Show() => Main.Post(() => { _dismissed = false; ShowCore(); });

    /// <summary>La app dejó de estar en primer plano: muestra la burbuja si hay un pase corriendo y no se descartó.</summary>
    public static void ShowIfRunning() => Main.Post(ShowCore);

    /// <summary>La app volvió a primer plano: ya no hace falta la burbuja.</summary>
    public static void Hide() => Main.Post(HideCore);

    /// <summary>Empezó un pase nuevo: una burbuja descartada antes vuelve a estar permitida.</summary>
    public static void OnServiceStarted() => Main.Post(() =>
    {
        _dismissed = false;
        _alerted = false;
    });

    /// <summary>El pase terminó: la burbuja se queda con el resultado si lo hubo; si no, desaparece.</summary>
    public static void OnServiceStopped() => Main.Post(() =>
    {
        if (!DeploymentBubble.Finished) HideCore();
    });

    private static void ShowCore()
    {
        if (_bubble is not null || _dismissed) return;
        var context = AppContext;
        if (!CanDraw(context) || !DeploymentForegroundService.IsRunning) return;

        try
        {
            _windows ??= context.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
            var metrics = context.Resources!.DisplayMetrics!;
            var size = Dp(BubbleDp);
            if (_x == int.MinValue)
            {
                _x = metrics.WidthPixels - size - Dp(MarginDp);
                _y = metrics.HeightPixels / 3;
            }
            _x = Math.Clamp(_x, 0, Math.Max(0, metrics.WidthPixels - size));
            _y = Math.Clamp(_y, 0, Math.Max(0, metrics.HeightPixels - size));

            var bubble = new OverlayBubbleView(context);
            var layout = NewParams(size, size, WindowManagerFlags.NotFocusable);
            layout.X = _x;
            layout.Y = _y;
            bubble.Touch += OnBubbleTouch;
            bubble.SetState(Classify(DeploymentBubble.Message, DeploymentBubble.Finished));
            bubble.SetProgress(DeploymentBubble.Percent);
            _windows!.AddView(bubble, layout);
            _bubble = bubble;
            _bubbleParams = layout;
            DeploymentBubble.Changed += OnChanged;
            DeploymentBubble.ProgressChanged += OnProgress;
        }
        catch (System.Exception ex)
        {
            // La burbuja es un extra: nunca debe afectar al pase. El motivo queda en el registro de actividad.
            ActivityLog.Record(ActivityLevel.Warning, "burbuja", $"burbuja flotante no mostrada: {ex.GetType().Name}: {ex.Message}");
            HideCore();
        }
    }

    private static void HideCore()
    {
        DeploymentBubble.Changed -= OnChanged;
        DeploymentBubble.ProgressChanged -= OnProgress;
        ClosePanel();
        HideCloseTarget();
        if (_bubble is null) return;
        RemoveView(_bubble);
        _bubble = null;
        _bubbleParams = null;
    }

    private static void OnChanged(string message, bool finished) => Main.Post(() =>
    {
        // Con la app en segundo plano (la burbuja visible), avisa una vez cuando el pase termina.
        if (finished && _bubble is not null && !_alerted)
        {
            _alerted = true;
            Alert(Classify(message, finished));
        }
        _bubble?.SetState(Classify(message, finished));
        _panel?.Apply(message, finished);
    });

    /// <summary>Vibración (y sonido si el teléfono no está en silencio) al terminar el pase: una pulsación si salió bien, tres si no.</summary>
    private static void Alert(BubbleState state)
    {
        try
        {
            var context = AppContext;
            var audio = context.GetSystemService(Context.AudioService) as global::Android.Media.AudioManager;
            var mode = audio?.RingerMode ?? global::Android.Media.RingerMode.Normal;
            if (mode == global::Android.Media.RingerMode.Silent) return;

            var pattern = state == BubbleState.Success ? new long[] { 0, 180 } : new long[] { 0, 160, 120, 160, 120, 320 };
            (context.GetSystemService(Context.VibratorService) as Vibrator)?.Vibrate(VibrationEffect.CreateWaveform(pattern, -1));

            if (mode == global::Android.Media.RingerMode.Normal)
            {
                global::Android.Media.RingtoneManager.GetRingtone(context,
                    global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification))?.Play();
            }
        }
        catch (System.Exception) { /* un aviso nunca debe afectar al pase */ }
    }

    private static void OnProgress() => Main.Post(() =>
    {
        _bubble?.SetProgress(DeploymentBubble.Percent);
        _panel?.Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
    });

    private static BubbleState Classify(string message, bool finished)
    {
        if (!finished) return BubbleState.Running;
        if (message.Contains("verificado", StringComparison.OrdinalIgnoreCase)) return BubbleState.Success;
        if (message.Contains("avisos", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Cancelaci", StringComparison.OrdinalIgnoreCase)) return BubbleState.Warning;
        return BubbleState.Failure;
    }

    private static void OnBubbleTouch(object? sender, View.TouchEventArgs e)
    {
        e.Handled = true;
        var ev = e.Event;
        if (ev is null || _bubble is null || _bubbleParams is null) return;

        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _downRawX = ev.RawX;
                _downRawY = ev.RawY;
                _downX = _bubbleParams.X;
                _downY = _bubbleParams.Y;
                _dragging = false;
                _longPressed = false;
                var press = ++_pressId;
                Main.PostDelayed(() => OnLongPress(press), 500);
                break;

            case MotionEventActions.Move:
            {
                var dx = ev.RawX - _downRawX;
                var dy = ev.RawY - _downRawY;
                if (!_dragging && Math.Abs(dx) + Math.Abs(dy) > Dp(8))
                {
                    _dragging = true;
                    _pressId++;
                    _bubble.PerformHapticFeedback(FeedbackConstants.LongPress);
                    ClosePanel();
                    ShowCloseTarget();
                }
                if (_dragging)
                {
                    MoveBubble(_downX + (int)dx, _downY + (int)dy);
                    UpdateCloseTarget();
                }
                break;
            }

            case MotionEventActions.Up:
                _pressId++;
                if (!_dragging)
                {
                    if (!_longPressed) TogglePanel();
                    break;
                }
                var dismiss = IsOverCloseTarget();
                HideCloseTarget();
                if (dismiss)
                {
                    _dismissed = true;
                    HideCore();
                }
                else SnapToEdge();
                _dragging = false;
                break;

            case MotionEventActions.Cancel:
                _pressId++;
                HideCloseTarget();
                if (_dragging) SnapToEdge();
                _dragging = false;
                break;
        }
    }

    /// <summary>Pulsación larga: abre el panel con «Abortar» ya a un toque de confirmarse.</summary>
    private static void OnLongPress(int press)
    {
        if (press != _pressId || _dragging || _bubble is null) return;
        _longPressed = true;
        _bubble.PerformHapticFeedback(FeedbackConstants.LongPress);
        if (_panel is null) OpenPanel();
        if (DeploymentBubble.Finished) return;
        _panel?.ArmAbort();
    }

    private static void MoveBubble(int x, int y)
    {
        if (_bubble is null || _bubbleParams is null || _windows is null) return;
        var metrics = AppContext.Resources!.DisplayMetrics!;
        var size = Dp(BubbleDp);
        _x = Math.Clamp(x, 0, Math.Max(0, metrics.WidthPixels - size));
        _y = Math.Clamp(y, 0, Math.Max(0, metrics.HeightPixels - size));
        _bubbleParams.X = _x;
        _bubbleParams.Y = _y;
        try { _windows.UpdateViewLayout(_bubble, _bubbleParams); }
        catch (System.Exception) { /* la ventana ya no existe */ }
    }

    /// <summary>Al soltarla, la burbuja se pega al borde izquierdo o derecho más cercano.</summary>
    private static void SnapToEdge()
    {
        var metrics = AppContext.Resources!.DisplayMetrics!;
        var size = Dp(BubbleDp);
        var from = _x;
        var to = _x + size / 2 < metrics.WidthPixels / 2 ? Dp(MarginDp) : metrics.WidthPixels - size - Dp(MarginDp);
        var animator = ValueAnimator.OfFloat(0f, 1f)!;
        animator.SetDuration(180);
        animator.Update += (_, args) => MoveBubble((int)(from + (to - from) * args.Animation!.AnimatedFraction), _y);
        animator.Start();
    }

    private static void TogglePanel()
    {
        if (_panel is not null)
        {
            ClosePanel();
            return;
        }
        // Tocar la burbuja con el panel abierto manda primero un «toque fuera» que ya lo cerró: no reabrirlo.
        if (SystemClock.UptimeMillis() - _panelClosedOutsideAt < 350) return;
        OpenPanel();
    }

    private static void OpenPanel()
    {
        if (_windows is null) return;
        try
        {
            var context = AppContext;
            var metrics = context.Resources!.DisplayMetrics!;
            var width = Math.Min(metrics.WidthPixels - Dp(24), Dp(360));

            var panel = new DeploymentStatusPanel(context, OpenApp);
            var background = new GradientDrawable();
            background.SetColor(Color.ParseColor("#0F1722"));
            background.SetCornerRadius(Dp(20));
            panel.Background = background;
            panel.ClipToOutline = true;
            panel.Elevation = Dp(8);
            panel.Apply(DeploymentBubble.Message, DeploymentBubble.Finished);
            panel.Measure(View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
                View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));

            var layout = NewParams(width, ViewGroup.LayoutParams.WrapContent,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.WatchOutsideTouch);
            layout.Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal;
            var below = _y + Dp(BubbleDp) + Dp(8);
            layout.Y = below + panel.MeasuredHeight <= metrics.HeightPixels - Dp(16)
                ? below
                : Math.Max(Dp(24), _y - panel.MeasuredHeight - Dp(8));
            panel.Touch += (_, args) =>
            {
                if (args.Event?.Action == MotionEventActions.Outside)
                {
                    _panelClosedOutsideAt = SystemClock.UptimeMillis();
                    ClosePanel();
                }
                args.Handled = false;
            };

            _windows.AddView(panel, layout);
            _panel = panel;
        }
        catch (System.Exception ex)
        {
            ActivityLog.Record(ActivityLevel.Warning, "burbuja", $"panel de la burbuja no mostrado: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ClosePanel()
    {
        if (_panel is null) return;
        RemoveView(_panel);
        _panel = null;
    }

    private static void OpenApp()
    {
        var context = AppContext;
        ClosePanel();
        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (launch is null) return;
        launch.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(launch);
        // MainActivity.OnStart oculta la burbuja al volver al primer plano.
    }

    /// <summary>Círculo «×» abajo, visible solo mientras se arrastra: soltar la burbuja encima la descarta.</summary>
    private static void ShowCloseTarget()
    {
        if (_closeTarget is not null || _windows is null) return;
        try
        {
            var context = AppContext;
            var metrics = context.Resources!.DisplayMetrics!;
            var size = Dp(CloseDp);
            var circle = new GradientDrawable();
            circle.SetShape(ShapeType.Oval);
            circle.SetColor(Color.ParseColor("#B91C1C"));
            var target = new TextView(context) { Text = "✕", Gravity = GravityFlags.Center, TextSize = 24, Alpha = 0.9f };
            target.SetTextColor(Color.White);
            target.Background = circle;

            var layout = NewParams(size, size, WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable);
            layout.X = (metrics.WidthPixels - size) / 2;
            layout.Y = metrics.HeightPixels - size - Dp(CloseBottomDp);
            _windows.AddView(target, layout);
            _closeTarget = target;
            _overClose = false;
        }
        catch (System.Exception) { /* sin el círculo de descarte se puede seguir moviendo la burbuja */ }
    }

    private static void HideCloseTarget()
    {
        if (_closeTarget is null) return;
        RemoveView(_closeTarget);
        _closeTarget = null;
        _overClose = false;
    }

    private static bool IsOverCloseTarget()
    {
        if (_closeTarget is null) return false;
        var metrics = AppContext.Resources!.DisplayMetrics!;
        var bubbleCx = _x + Dp(BubbleDp) / 2f;
        var bubbleCy = _y + Dp(BubbleDp) / 2f;
        var closeCx = metrics.WidthPixels / 2f;
        var closeCy = metrics.HeightPixels - Dp(CloseBottomDp) - Dp(CloseDp) / 2f;
        var dx = bubbleCx - closeCx;
        var dy = bubbleCy - closeCy;
        return Math.Sqrt(dx * dx + dy * dy) < Dp(CloseDp) + Dp(16);
    }

    private static void UpdateCloseTarget()
    {
        if (_closeTarget is null) return;
        var over = IsOverCloseTarget();
        if (over == _overClose) return;
        _overClose = over;
        _bubble?.PerformHapticFeedback(FeedbackConstants.ClockTick);
        _closeTarget.ScaleX = _closeTarget.ScaleY = over ? 1.25f : 1f;
    }

    private static WindowManagerLayoutParams NewParams(int width, int height, WindowManagerFlags flags)
        => new(width, height, WindowManagerTypes.ApplicationOverlay, flags | WindowManagerFlags.LayoutInScreen, Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left
        };

    private static void RemoveView(View view)
    {
        try { _windows?.RemoveView(view); }
        catch (System.Exception) { /* ya estaba quitada */ }
    }

    private static int Dp(int value) => (int)(value * AppContext.Resources!.DisplayMetrics!.Density + 0.5f);
}
