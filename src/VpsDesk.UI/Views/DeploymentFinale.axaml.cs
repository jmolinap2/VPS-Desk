using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

/// <summary>A short, non-interactive finale; it never changes the deployment state.</summary>
public partial class DeploymentFinale : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly RotateTransform _rotation = new();
    private DeploymentsViewModel? _model;
    private DeploymentFinaleOutcome _outcome;
    private Point _origin;

    public DeploymentFinale()
    {
        InitializeComponent();
        Aircraft.RenderTransform = _rotation;
        _timer.Tick += (_, _) => Animate();
        DataContextChanged += (_, _) => AttachModel();
        AttachedToVisualTree += (_, _) => AttachModel();
        DetachedFromVisualTree += (_, _) => { Stop(); DetachModel(); };
    }

    private void AttachModel()
    {
        DetachModel();
        _model = DataContext as DeploymentsViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
    }

    private void DetachModel()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeploymentsViewModel.DeploymentFinale)) return;
        if (_model?.DeploymentFinale == DeploymentFinaleOutcome.None) { Stop(); return; }
        if (_model is null || !IsEffectivelyVisible || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Start(_model.DeploymentFinale);
    }

    private void Start(DeploymentFinaleOutcome outcome)
    {
        Stop();
        _outcome = outcome;
        var jet = this.GetVisualParent()?.GetVisualDescendants().OfType<DeploymentJet>()
            .FirstOrDefault();
        var progress = Math.Clamp(_model?.DeploymentProgressPercent ?? 0, 0, 100) / 100;
        _origin = jet?.TranslatePoint(new Point(Math.Max(0, jet.Bounds.Width - 76) * progress + 38, 18), this)
            ?? new Point(Bounds.Width * .5, Bounds.Height * .65);
        _origin = new Point(Math.Clamp(_origin.X, 38, Math.Max(38, Bounds.Width - 38)),
            Math.Clamp(_origin.Y, 36, Math.Max(36, Bounds.Height - 36)));
        _clock.Restart();
        Animate();
        _timer.Start();
    }

    private void Animate()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        if (_outcome == DeploymentFinaleOutcome.Success)
        {
            var t = Math.Clamp(seconds / 2.5, 0, 1);
            var eased = t * t * (3 - 2 * t);
            var x = Lerp(_origin.X, Bounds.Width * .55, eased);
            var y = Lerp(_origin.Y, -45, eased);
            PlaceAircraft(x, y, Lerp(0, -72, Math.Min(1, t * 3)));
            Aircraft.Opacity = t < .82 ? 1 : (1 - t) / .18;
            Canvas.SetLeft(Clouds, Math.Max(0, (Bounds.Width - 300) / 2));
            Canvas.SetTop(Clouds, 0);
            Clouds.Opacity = Math.Min(.85, t * 2);
            if (t >= 1) Stop();
        }
        else if (_outcome == DeploymentFinaleOutcome.Failed)
        {
            var t = Math.Clamp(seconds / 2.1, 0, 1);
            var rise = Math.Min(1, t / .3);
            var fall = Math.Clamp((t - .3) / .7, 0, 1);
            var y = _origin.Y - 65 * Math.Sin(rise * Math.PI / 2);
            if (t > .3) y = Lerp(_origin.Y - 65, Bounds.Height - 28, fall * fall);
            var x = Math.Clamp(_origin.X + 80 * t, 38, Math.Max(38, Bounds.Width - 38));
            PlaceAircraft(x, y, t < .3 ? -25 * rise : Lerp(-25, 75, fall));
            Aircraft.Opacity = t < .98 ? 1 : 0;
            if (t >= 1)
            {
                var burst = Math.Clamp((seconds - 2.1) / .8, 0, 1);
                var size = 15 + 105 * burst;
                BlastRing.Width = BlastRing.Height = size;
                Canvas.SetLeft(BlastRing, x - size / 2);
                Canvas.SetTop(BlastRing, Bounds.Height - 28 - size / 2);
                BlastRing.Opacity = 1 - burst;
                Canvas.SetLeft(Blast, x - 40);
                Canvas.SetTop(Blast, Bounds.Height - 68);
                Blast.Opacity = 1 - burst;
                if (burst >= 1) Stop();
            }
        }
    }

    private void PlaceAircraft(double x, double y, double angle)
    {
        Canvas.SetLeft(Aircraft, x - 38);
        Canvas.SetTop(Aircraft, y - 18);
        _rotation.Angle = angle;
    }

    private void Stop()
    {
        _timer.Stop();
        _clock.Reset();
        _outcome = DeploymentFinaleOutcome.None;
        Aircraft.Opacity = Clouds.Opacity = Blast.Opacity = BlastRing.Opacity = 0;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
