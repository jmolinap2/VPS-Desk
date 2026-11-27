using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Logging;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>Registro de actividad de la aplicación: ver los últimos eventos, copiarlos, guardarlos o borrarlos.</summary>
public partial class ActivityLogViewModel : ObservableObject
{
    // Más de esto no cabe en una pantalla de teléfono con sentido; el informe exportado lleva todo.
    private const int VisibleLines = 300;

    [ObservableProperty] private bool _onlyProblems;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _hasEntries;

    public ActivityLogViewModel() => Refresh();

    partial void OnOnlyProblemsChanged(bool value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        var all = ActivityLog.Snapshot();
        var shown = OnlyProblems ? all.Where(x => x.Level != ActivityLevel.Info).ToList() : all.ToList();
        HasEntries = all.Count > 0;
        Summary = string.Format(LocalizationService.T("Activity_Summary"),
            all.Count, all.Count(x => x.Level == ActivityLevel.Error), all.Count(x => x.Level == ActivityLevel.Warning));
        Text = shown.Count == 0
            ? LocalizationService.T("Activity_Empty")
            : string.Join(Environment.NewLine, shown.TakeLast(VisibleLines).Reverse().Select(ActivityLog.Format));
    }

    [RelayCommand]
    private void Clear()
    {
        ActivityLog.Clear();
        Refresh();
    }

    /// <summary>Informe completo (no solo lo visible) con el filtro elegido.</summary>
    public string BuildReport() => ActivityReport.Build(ActivityLog.Snapshot(OnlyProblems), OnlyProblems);
}
