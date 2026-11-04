using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

namespace VpsDesk.Desktop.Views;

/// <summary>
/// Mantiene visible el final de un <see cref="TextBox"/> de solo lectura cuando su texto
/// crece (salida de un despliegue en curso). Uso: <c>views:TextBoxAutoScroll.IsEnabled="True"</c>.
/// </summary>
public static class TextBoxAutoScroll
{
    private sealed class TrackingState { public bool Hooked; }
    private static readonly ConditionalWeakTable<TextBox, TrackingState> States = new();
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(TextBoxAutoScroll));

    static TextBoxAutoScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            HookScrolling(box);
            if (GetIsEnabled(box)) ScrollToEnd(box, clearSelection: true);
        });
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (!GetIsEnabled(box)) return;
            ScrollToEnd(box);
        });
    }

    private static void HookScrolling(TextBox box)
    {
        var state = States.GetOrCreateValue(box);
        if (state.Hooked) return;
        state.Hooked = true;
        void Attach()
        {
            var scroll = box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is null) return;
            scroll.ScrollChanged += (_, e) =>
            {
                // Incluye rueda, barra y desplazamiento táctil. El crecimiento del
                // documento no debe confundirse con una decisión del usuario.
                if (e.OffsetDelta.Y < 0 && e.ExtentDelta.Y == 0 && e.ViewportDelta.Y == 0)
                    box.SetCurrentValue(IsEnabledProperty, false);
            };
        }
        box.Loaded += (_, _) => Attach();
        if (box.IsLoaded) Attach();
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
