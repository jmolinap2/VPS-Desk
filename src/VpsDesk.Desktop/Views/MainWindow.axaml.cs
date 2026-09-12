using Avalonia.Controls;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
        };
    }
}
