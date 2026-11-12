using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using VpsDesk.Desktop.ViewModels;

namespace VpsDesk.Desktop.Views;

public partial class MobilePageHost : UserControl
{
    private MainWindowViewModel? _subscribedViewModel;
    private readonly Dictionary<string, Grid> _pages;

    public MobilePageHost()
    {
        InitializeComponent();

        _pages = new Dictionary<string, Grid>
        {
            ["Dashboard"] = DashboardPage,
            ["Servers"] = ServersPage,
            ["Containers"] = ContainersPage,
            ["Projects"] = ProjectsPage,
            ["Deployments"] = DeploymentsPage,
            ["Backups"] = BackupsPage,
            ["Environment"] = EnvironmentPage,
            ["Logs"] = LogsPage,
            ["Storage"] = StoragePage,
            ["Files"] = FilesPage,
            ["Terminal"] = TerminalPage,
            ["Security"] = SecurityPage,
            ["Settings"] = SettingsPage
        };

        DataContextChanged += (_, _) => WireViewModel();
        AttachedToVisualTree += (_, _) => WireViewModel();
    }

    private void WireViewModel()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as MainWindowViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedPage)) return;
        if (sender is not MainWindowViewModel vm) return;
        if (!_pages.TryGetValue(vm.SelectedPage, out var page)) return;

        // La página recién mostrada arranca "enter" (transparente y desplazada) y la
        // transición de la hoja de estilos la lleva a su posición. Se quita después
        // de que el enlace de IsVisible se haya aplicado, en la siguiente pasada de UI.
        page.Classes.Add("enter");
        Dispatcher.UIThread.Post(() => page.Classes.Remove("enter"), DispatcherPriority.Background);
    }
}
