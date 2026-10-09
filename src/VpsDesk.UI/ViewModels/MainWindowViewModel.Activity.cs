using System.Collections.ObjectModel;
using VpsDesk.Application.Activity;
using VpsDesk.Domain.Activity;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private IOperationHistoryStore? _operationHistoryStore;

    public ObservableCollection<OperationHistoryEntry> RecentActivity { get; } = new();
    public bool HasRecentActivity => RecentActivity.Count > 0;

    public void InitializeActivity(IOperationHistoryStore operationHistoryStore)
    {
        _operationHistoryStore = operationHistoryStore;
        _ = RefreshRecentActivityAsync();
    }

    public async Task RefreshRecentActivityAsync()
    {
        if (_operationHistoryStore is null) return;
        var items = await _operationHistoryStore.GetRecentAsync(8);
        RecentActivity.Clear();
        foreach (var item in items) RecentActivity.Add(item);
        OnPropertyChanged(nameof(HasRecentActivity));
    }
}
