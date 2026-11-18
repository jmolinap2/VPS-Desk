using System.ComponentModel;
using Avalonia.Controls;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class DeploymentJet : UserControl
{
    private DeploymentsViewModel? _model;

    public DeploymentJet()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachModel();
        AttachedToVisualTree += (_, _) => AttachModel();
        DetachedFromVisualTree += (_, _) => DetachModel();
        SizeChanged += (_, _) => UpdateFlight();
    }

    private void AttachModel()
    {
        DetachModel();
        _model = DataContext as DeploymentsViewModel;
        if (_model is not null) _model.PropertyChanged += OnProgressChanged;
        UpdateFlight();
    }

    private void DetachModel()
    {
        if (_model is not null) _model.PropertyChanged -= OnProgressChanged;
        _model = null;
    }

    private void OnProgressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeploymentsViewModel.DeploymentProgressPercent)) UpdateFlight();
    }

    private void UpdateFlight()
    {
        var progress = Math.Clamp(_model?.DeploymentProgressPercent ?? 0, 0, 100);
        Canvas.SetLeft(Jet, Math.Max(0, Bounds.Width - 76) * progress / 100);
        Jet.Classes.Set("phase-start", progress < 30);
        Jet.Classes.Set("phase-progress", progress >= 30 && progress < 90);
        Jet.Classes.Set("phase-final", progress >= 90);
    }
}
