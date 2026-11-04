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
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (GetIsEnabled(box)) ScrollToEnd(box, clearSelection: true);
        });
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (!GetIsEnabled(box)) return;
            ScrollToEnd(box);
        });
    }

    private static void ScrollToEnd(TextBox box, bool clearSelection = false)
        => Dispatcher.UIThread.Post(() =>
        {
            // No roba el foco ni deshace una selección mientras el usuario copia texto.
            if (!GetIsEnabled(box)) return;
            var end = box.Text?.Length ?? 0;
            if (clearSelection)
            {
                box.SelectionStart = end;
                box.SelectionEnd = end;
            }
            if (box.SelectionStart == box.SelectionEnd) box.CaretIndex = end;
        }, DispatcherPriority.Background);

    public static bool GetIsEnabled(TextBox box) => box.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBox box, bool value) => box.SetValue(IsEnabledProperty, value);
}
