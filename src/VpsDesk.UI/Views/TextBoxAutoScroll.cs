using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Mantiene visible el final de un <see cref="TextBox"/> de solo lectura cuando su texto
/// crece (salida de un despliegue en curso). Uso: <c>views:TextBoxAutoScroll.IsEnabled="True"</c>.
/// </summary>
public static class TextBoxAutoScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(TextBoxAutoScroll));

    static TextBoxAutoScroll()
    {
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (!GetIsEnabled(box)) return;

            // Tras el layout, para que el caret ya tenga posición al desplazarse.
            Dispatcher.UIThread.Post(
                () => box.CaretIndex = box.Text?.Length ?? 0,
                DispatcherPriority.Background);
        });
    }

    public static bool GetIsEnabled(TextBox box) => box.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBox box, bool value) => box.SetValue(IsEnabledProperty, value);
}
