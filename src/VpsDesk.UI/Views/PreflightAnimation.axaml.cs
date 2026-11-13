using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VpsDesk.Desktop.Views;

public partial class PreflightAnimation : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PreflightAnimation, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _elapsed = new();
    private readonly TranslateTransform _flight = new();
    private readonly RotateTransform _radar = new();
    private bool _attached;

    public PreflightAnimation()
    {
        InitializeComponent();
        JetCarrier.RenderTransform = _flight;
        RadarSweep.RenderTransform = _radar;
        _timer.Tick += (_, _) => Animate();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdatePlayback();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdatePlayback();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) UpdatePlayback();
    }

    private void UpdatePlayback()
    {
        if (_attached && IsActive)
        {
            _elapsed.Restart();
            Animate();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _elapsed.Reset();
        }
    }

    private void Animate()
    {
        var seconds = _elapsed.Elapsed.TotalSeconds;
        var lap = seconds % 5 / 5;
        // Propiedades numéricas directas: compatibles con Android Release/AOT,
        // sin animadores de RenderTransform que deban descubrirse por reflexión.
        _flight.X = -100 + lap * 440;
        _flight.Y = -12 * Math.Sin(lap * Math.PI) + 3 * Math.Sin(seconds * 3);
        JetCarrier.Opacity = Math.Min(1, Math.Min(lap * 10, (1 - lap) * 10));
        Exhaust.Opacity = 0.65 + 0.35 * Math.Sin(seconds * 16);
        _radar.Angle = seconds * 100 % 360;
        RadarBlip.Opacity = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(seconds * 4));
    }
}
