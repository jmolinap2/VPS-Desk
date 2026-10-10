using Avalonia.Controls;
using Avalonia.Input;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MobileTerminalView : UserControl
{
    private TerminalViewModel? _subscribedViewModel;

    public MobileTerminalView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
        AttachedToVisualTree += (_, _) => WireViewModel();
    }

    private void WireViewModel()
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _subscribedViewModel = DataContext as TerminalViewModel;
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }
    }

    // Mantiene visible la última línea cuando llega salida nueva.
    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TerminalViewModel.TerminalOutput)) return;
        OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0;
    }

    // Misma lógica que la terminal de escritorio: Enter envía, flechas navegan el historial
    // y Ctrl+C interrumpe. En el teclado virtual, Enter llega como Key.Enter.
    private async void CommandInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TerminalViewModel vm) return;

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.SendCommand.CanExecute(null))
            {
                await vm.SendCommand.ExecuteAsync(null);
            }
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            vm.NavigateHistory(-1);
            CommandInput.CaretIndex = CommandInput.Text?.Length ?? 0;
            return;
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            vm.NavigateHistory(1);
            CommandInput.CaretIndex = CommandInput.Text?.Length ?? 0;
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await vm.SendInterruptCommand.ExecuteAsync(null);
        }
    }
}
