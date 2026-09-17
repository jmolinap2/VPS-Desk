using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VpsDesk.Android;

internal sealed class AndroidMainShellView : UserControl
{
    private readonly ContentControl _contentHost = new();
    private readonly Avalonia.Controls.Button _dashboardButton = new() { Content = "Dashboard" };
    private readonly Avalonia.Controls.Button _moreButton = new() { Content = "More" };
    private readonly Avalonia.Controls.Button _containersButton = new() { Content = "Containers" };
    private readonly Avalonia.Controls.Button _logsButton = new() { Content = "Logs" };

    public AndroidMainShellView()
    {
        _dashboardButton.Click += (_, _) => ShowDashboard();
        _moreButton.Click += (_, _) => ShowMore();
        _containersButton.Click += (_, _) => ShowContainers();
        _logsButton.Click += (_, _) => ShowLogs();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                Place(_contentHost, 0),
                Place(
                    new Border
                    {
                        Padding = new Thickness(12, 8),
                        Child = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
                            ColumnSpacing = 8,
                            Children =
                            {
                                PlaceColumn(_dashboardButton, 0),
                                PlaceColumn(_containersButton, 1),
                                PlaceColumn(_logsButton, 2),
                                PlaceColumn(_moreButton, 3)
                            }
                        }
                    },
                    1)
            }
        };

        ShowDashboard();
    }

    private void ShowDashboard()
    {
        _contentHost.Content = new AndroidDashboardView();
        _dashboardButton.IsEnabled = false;
        _containersButton.IsEnabled = true;
        _logsButton.IsEnabled = true;
        _moreButton.IsEnabled = true;
    }

    private void ShowContainers()
    {
        _contentHost.Content = new AndroidContainersView();
        _dashboardButton.IsEnabled = true;
        _containersButton.IsEnabled = false;
        _logsButton.IsEnabled = true;
        _moreButton.IsEnabled = true;
    }

    private void ShowLogs()
    {
        _contentHost.Content = new AndroidLogsView();
        _dashboardButton.IsEnabled = true;
        _containersButton.IsEnabled = true;
        _logsButton.IsEnabled = false;
        _moreButton.IsEnabled = true;
    }

    private void ShowMore()
    {
        _contentHost.Content = new AndroidMoreView();
        _dashboardButton.IsEnabled = true;
        _containersButton.IsEnabled = true;
        _logsButton.IsEnabled = true;
        _moreButton.IsEnabled = false;
    }

    private static Control Place(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static Control PlaceColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
