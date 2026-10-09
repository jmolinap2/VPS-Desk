using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>
/// Reusable feedback surface for remote operations that do not deploy containers.
/// </summary>
public partial class DeploymentsViewModel
{
    private readonly StringBuilder _operationFeedbackLog = new();

    [ObservableProperty] private bool _isOperationFeedbackModalOpen;
    [ObservableProperty] private bool _isOperationFeedbackRunning;
    [ObservableProperty] private bool _operationFeedbackSucceeded;
    [ObservableProperty] private bool _isOperationFeedbackOutputExpanded;
    [ObservableProperty] private string _operationFeedbackTitle = string.Empty;
    [ObservableProperty] private string _operationFeedbackMessage = string.Empty;
    [ObservableProperty] private string _operationFeedbackOutput = string.Empty;

    public bool IsOperationFeedbackFailed => !IsOperationFeedbackRunning && !OperationFeedbackSucceeded;

    partial void OnIsOperationFeedbackRunningChanged(bool value)
        => OnPropertyChanged(nameof(IsOperationFeedbackFailed));

    partial void OnOperationFeedbackSucceededChanged(bool value)
        => OnPropertyChanged(nameof(IsOperationFeedbackFailed));

    public void BeginOperationFeedback(string title, string message)
    {
        OperationFeedbackTitle = title;
        OperationFeedbackMessage = message;
        OperationFeedbackSucceeded = false;
        IsOperationFeedbackRunning = true;
        IsOperationFeedbackOutputExpanded = false;
        _operationFeedbackLog.Clear();
        OperationFeedbackOutput = string.Empty;
        IsOperationFeedbackModalOpen = true;
    }

    public void CompleteOperationFeedback(bool succeeded, string message)
    {
        OperationFeedbackSucceeded = succeeded;
        OperationFeedbackMessage = message;
        IsOperationFeedbackRunning = false;
        IsOperationFeedbackOutputExpanded = true;
    }

    public void OnGitUpdateProgressChanged(GitRepositoryUpdateProgressUpdate update)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsOperationFeedbackModalOpen) return;

            if (update.State == GitRepositoryUpdateProgressState.Started)
            {
                OperationFeedbackMessage = $"{update.Label} en curso...";
                _operationFeedbackLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] ▶ {update.Label}");
                _operationFeedbackLog.AppendLine("  ejecutando...");
            }
            else
            {
                var marker = update.Succeeded == true ? "✓" : "✕";
                var seconds = update.Duration?.TotalSeconds ?? 0;
                _operationFeedbackLog.AppendLine($"  {marker} exit {update.ExitCode ?? -1} · {seconds:F1}s");
                if (!string.IsNullOrWhiteSpace(update.Output))
                {
                    _operationFeedbackLog.AppendLine(update.Output.TrimEnd());
                }
                _operationFeedbackLog.AppendLine();
            }

            OperationFeedbackOutput = _operationFeedbackLog.ToString().TrimEnd();
        });
    }

    [RelayCommand]
    private void ToggleOperationFeedbackOutput()
        => IsOperationFeedbackOutputExpanded = !IsOperationFeedbackOutputExpanded;

    [RelayCommand]
    private void CloseOperationFeedbackModal()
    {
        if (!IsOperationFeedbackRunning)
        {
            IsOperationFeedbackModalOpen = false;
        }
    }
}
