using CommunityToolkit.Mvvm.ComponentModel;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedServerName = "No server selected";

    [ObservableProperty]
    private string _connectionStatus = "Offline";

    public double CpuUsage => 0;
    public double MemoryUsage => 0;
    public double DiskUsage => 0;
    public string Uptime => "--";

    public double[] CpuSeries { get; } = [0, 0, 0, 0, 0, 0, 0, 0];
    public double[] MemorySeries { get; } = [0, 0, 0, 0, 0, 0, 0, 0];
}
