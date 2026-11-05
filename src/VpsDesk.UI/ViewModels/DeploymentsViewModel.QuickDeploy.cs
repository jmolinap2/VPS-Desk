using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;

namespace VpsDesk.Desktop.ViewModels;

/// <summary>Una opción de "qué ejecutar": un objetivo de despliegue o "solo migraciones".</summary>
public sealed record DeploymentChoice(string Id, string Label, ProjectDeploymentTarget? Target)
{
    public bool IsMigrationOnly => Target is null;
}

/// <summary>
/// Selección rápida del pase (Completo / Backend / Frontend / Solo migraciones) como fila de
/// chips. No guarda estado propio: es una vista sobre <c>SelectedDeploymentTarget</c> y
/// <c>MigrationOnly</c>, así que el prevuelo, el perfil guardado y los textos de confirmación
/// siguen funcionando igual que con los controles anteriores.
/// </summary>
public partial class DeploymentsViewModel
{
    private bool _syncingQuickChoice;

    public ObservableCollection<DeploymentChoice> QuickChoices { get; } = new();

    [ObservableProperty] private DeploymentChoice? _selectedQuickChoice;
    [ObservableProperty] private bool _isAdvancedOptionsOpen;

    /// <summary>Qué se va a ejecutar, en una línea: objetivo, migraciones, rama y ahorro de datos.</summary>
    public string ExecutionSummaryLine
    {
        get
        {
            var branch = string.IsNullOrWhiteSpace(Branch) ? "sin rama" : Branch.Trim();
            var saver = IsDataSaverActive ? " · ahorro de datos" : string.Empty;
            return $"{BuildExecutionSummary()} · {branch}{saver}";
        }
    }

    [RelayCommand]
    private void OpenAdvancedOptions() => IsAdvancedOptionsOpen = true;

    [RelayCommand]
    private void CloseAdvancedOptions() => IsAdvancedOptionsOpen = false;

    private void NotifyExecutionSummaryChanged()
        => OnPropertyChanged(nameof(ExecutionSummaryLine));

    /// <summary>Reconstruye los chips tras cargar o reiniciar el modelo del proyecto.</summary>
    private void RebuildQuickChoices()
    {
        _syncingQuickChoice = true;
        try
        {
            QuickChoices.Clear();
            foreach (var target in AvailableTargets)
            {
                QuickChoices.Add(new DeploymentChoice(target.Id, target.Label, target));
            }

            if (HasMigrationRecipe)
            {
                QuickChoices.Add(new DeploymentChoice("__migrations_only__", "Solo migraciones", null));
            }
        }
        finally
        {
            _syncingQuickChoice = false;
        }

        SyncQuickChoiceFromState();
        NotifyExecutionSummaryChanged();
    }

    /// <summary>Refleja en el chip seleccionado el objetivo / "solo migraciones" actuales.</summary>
    private void SyncQuickChoiceFromState()
    {
        if (_syncingQuickChoice) return;

        _syncingQuickChoice = true;
        try
        {
            SelectedQuickChoice = MigrationOnly
                ? QuickChoices.FirstOrDefault(choice => choice.IsMigrationOnly)
                : QuickChoices.FirstOrDefault(choice => ReferenceEquals(choice.Target, SelectedDeploymentTarget));
        }
        finally
        {
            _syncingQuickChoice = false;
        }
    }

    partial void OnSelectedQuickChoiceChanged(DeploymentChoice? value)
    {
        if (_syncingQuickChoice || value is null) return;

        _syncingQuickChoice = true;
        try
        {
            if (value.IsMigrationOnly)
            {
                MigrationOnly = true;
            }
            else
            {
                MigrationOnly = false;
                SelectedDeploymentTarget = value.Target;

                // El interruptor de migraciones vuelve al valor por defecto del objetivo, también
                // al regresar desde "solo migraciones" aunque el objetivo no haya cambiado.
                if (HasMigrationRecipe && value.Target is not null)
                {
                    RunMigrations = value.Target.RunMigrationsByDefault;
                }
            }
        }
        finally
        {
            _syncingQuickChoice = false;
        }

        NotifyExecutionSummaryChanged();
    }
}
