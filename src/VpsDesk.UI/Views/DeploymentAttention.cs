using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace VpsDesk.Desktop.Views;

/// <summary>Hace visible el panel al iniciar un pase, sin activar el teclado ni mover el foco.</summary>
public static class DeploymentAttention
{
    public static readonly AttachedProperty<bool> IsRunningProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsRunning", typeof(DeploymentAttention));

    static DeploymentAttention()
    {
        IsRunningProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (!GetIsRunning(control)) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (GetIsRunning(control)) control.BringIntoView();
            }, DispatcherPriority.Loaded);
        });
    }

    public static bool GetIsRunning(Control control) => control.GetValue(IsRunningProperty);
    public static void SetIsRunning(Control control, bool value) => control.SetValue(IsRunningProperty, value);
}
