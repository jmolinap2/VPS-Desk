using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace VpsDesk.Android;

internal enum BubbleState { Running, Success, Warning, Failure }

/// <summary>
/// Círculo de la burbuja flotante del pase: un anillo que gira mientras el pase corre y que queda fijo en verde,
/// ámbar o rojo cuando termina. Se dibuja a mano para no depender de recursos ni de la interfaz de Avalonia.
/// </summary>
internal sealed class OverlayBubbleView : View
{
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _ring = new(PaintFlags.AntiAlias);
    private readonly Paint _glyph = new(PaintFlags.AntiAlias);
    private readonly RectF _arc = new();
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _tick;
    private BubbleState _state = BubbleState.Running;
    private float _angle;
    private bool _spinning;

    public OverlayBubbleView(Context context) : base(context)
    {
        _fill.Color = Color.ParseColor("#E60F1722");
        _ring.SetStyle(Paint.Style.Stroke);
        _ring.StrokeCap = Paint.Cap.Round;
        _glyph.Color = Color.White;
        _glyph.TextAlign = Paint.Align.Center;
        _glyph.SetTypeface(Typeface.DefaultBold);
        _tick = Tick;
        ContentDescription = "Pase en curso de VPS Desk";
    }

    public void SetState(BubbleState state)
    {
        _state = state;
        if (state == BubbleState.Running) StartSpinning();
        else _spinning = false;
        Invalidate();
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        if (_state == BubbleState.Running) StartSpinning();
    }

    protected override void OnDetachedFromWindow()
    {
        _spinning = false;
        base.OnDetachedFromWindow();
    }

    private void StartSpinning()
    {
        if (_spinning) return;
        _spinning = true;
        _handler.PostDelayed(_tick, 30);
    }

    private void Tick()
    {
        if (!_spinning) return;
        _angle = (_angle + 9f) % 360f;
        Invalidate();
        _handler.PostDelayed(_tick, 30);
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas is null) return;

        var size = Math.Min(Width, Height);
        var stroke = size * 0.09f;
        var cx = Width / 2f;
        var cy = Height / 2f;
        canvas.DrawCircle(cx, cy, size / 2f - 1f, _fill);

        var inset = stroke / 2f + size * 0.05f;
        _arc.Set(cx - size / 2f + inset, cy - size / 2f + inset, cx + size / 2f - inset, cy + size / 2f - inset);
        _ring.StrokeWidth = stroke;
        _ring.Color = Color.ParseColor(_state switch
        {
            BubbleState.Success => "#22C55E",
            BubbleState.Warning => "#F59E0B",
            BubbleState.Failure => "#EF4444",
            _ => "#38BDF8"
        });
        if (_state == BubbleState.Running) canvas.DrawArc(_arc, _angle, 110f, false, _ring);
        else canvas.DrawOval(_arc, _ring);

        _glyph.TextSize = size * 0.34f;
        var text = _state switch
        {
            BubbleState.Success => "✓",
            BubbleState.Running => "VD",
            _ => "!"
        };
        canvas.DrawText(text, cx, cy - (_glyph.Descent() + _glyph.Ascent()) / 2f, _glyph);
    }
}
