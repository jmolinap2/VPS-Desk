using Avalonia;
using Avalonia.Controls;

namespace VpsDesk.Android;

internal sealed class AndroidMoreView : UserControl
{
    private readonly ContentControl _contentHost = new();
    private readonly Avalonia.Controls.Button _serversButton = new() { Content = "Servers" };
    private readonly Avalonia.Controls.Button _storageButton = new() { Content = "Storage" };
    private readonly Avalonia.Controls.Button _filesButton = new() { Content = "Files" };

    public AndroidMoreView()
    {
        _serversButton.Click += (_, _) => ShowServers();
        _storageButton.Click += (_, _) => ShowStorage();
        _filesButton.Click += (_, _) => ShowFiles();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                Place(
                    new Grid
                    {
                        Margin = new Thickness(24, 16, 24, 8),
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            PlaceColumn(_serversButton, 0),
                            PlaceColumn(_storageButton, 1),
                            PlaceColumn(_filesButton, 2)
                        }
                    },
                    0),
                Place(_contentHost, 1)
            }
        };

        ShowServers();
    }

    private void ShowServers()
    {
        _contentHost.Content = new AndroidServersView();
        _serversButton.IsEnabled = false;
        _storageButton.IsEnabled = true;
        _filesButton.IsEnabled = true;
    }

    private void ShowStorage()
    {
        _contentHost.Content = new AndroidStorageView();
        _serversButton.IsEnabled = true;
        _storageButton.IsEnabled = false;
        _filesButton.IsEnabled = true;
    }

    private void ShowFiles()
    {
        _contentHost.Content = new AndroidFilesView();
        _serversButton.IsEnabled = true;
        _storageButton.IsEnabled = true;
        _filesButton.IsEnabled = false;
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
