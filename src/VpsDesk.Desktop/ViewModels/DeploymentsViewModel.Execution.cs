using System.Text;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    public Task TryAutoDiscoverRepositoryAsync()
        => DiscoverRemoteOptionsAsync();

    [RelayCommand]
    public async Task DiscoverRemoteOptionsAsync()
    {
        if (IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "El paso 1 está bloqueado: no hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            await DiscoverProjectsAsync(server);
            return;
        }

        IsBusy = true;
        StatusMessage = "Detectando ramas, archivos Compose y modelo del proyecto...";

        try
        {
            var result = await _discovery.DiscoverAsync(
                server,
                RemoteRepositoryPath.Trim(),
                _secretAccessor());

            AvailableBranches.Clear();
            foreach (var branch in result.Branches) AvailableBranches.Add(branch);

            var configuredBranchExists = result.Branches.Any(branch =>
                branch.Equals(Branch, StringComparison.OrdinalIgnoreCase));
            if (!configuredBranchExists && !string.IsNullOrWhiteSpace(result.CurrentBranch))
            {
                Branch = result.CurrentBranch;
            }

            AvailableComposeFiles.Clear();
            foreach (var file in result.ComposeFiles) AvailableComposeFiles.Add(file);

            SelectedBranchSuggestion = result.Branches.FirstOrDefault(x =>
                x.Equals(Branch, StringComparison.OrdinalIgnoreCase));
            SelectedComposeSuggestion = result.ComposeFiles.FirstOrDefault(x =>
                x.Equals(ComposeFile, StringComparison.OrdinalIgnoreCase));

            if (SelectedComposeSuggestion is null && result.ComposeFiles.Count > 0)
            {
                ComposeFile = result.ComposeFiles[0];
                SelectedComposeSuggestion = result.ComposeFiles[0];
            }

            await RefreshProjectModelAsync(server);
            PersistProfile(server);

            StatusMessage = IsProjectRecipeInvalid
                ? "La detección encontró una receta .vpsdesk inválida. Corrígela antes de desplegar."
                : HasProjectRecipe
                    ? $"Proyecto detectado con receta: {ProjectModelStatus}. Ejecuta el prevuelo para validar el objetivo seleccionado."
                    : $"Modo genérico listo: {result.Branches.Count} rama(s), {result.ComposeFiles.Count} archivo(s) Compose y {AvailableTargets.Count} objetivo(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"La detección falló. Revisa la conexión SSH y la ruta del repositorio: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscoverProjectsAsync(ServerProfile server)
    {
        IsBusy = true;
        StatusMessage = "Buscando automáticamente proyectos Git con Docker Compose en el VPS...";

        try
        {
            var projects = await _discovery.DiscoverProjectsAsync(server, _secretAccessor());

            AvailableProjects.Clear();
            foreach (var project in projects) AvailableProjects.Add(project);
            HasProjectSuggestions = AvailableProjects.Count > 0;

            if (projects.Count == 0)
            {
                StatusMessage = "No encontré un proyecto Compose en /root, /home, /opt ni /srv. Indica la ruta una sola vez y se guardará para este VPS.";
                return;
            }

            if (projects.Count > 1)
            {
                StatusMessage = $"Encontré {projects.Count} proyectos Compose. Selecciona el proyecto correcto; la ruta se guardará para este VPS.";
                return;
            }

            SelectedProjectSuggestion = projects[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo detectar automáticamente el proyecto: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (!string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            await DiscoverRemoteOptionsAsync();
        }
    }

    [RelayCommand]
    public async Task RunPreflightAsync()
        => await RunPreflightCoreAsync();

    [RelayCommand]
    public async Task StartDeploymentAsync()
    {
        if (IsBusy) return;

        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "No hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return;
        }

        if (AvailableBranches.Count == 0 || AvailableComposeFiles.Count == 0)
        {
            await DiscoverRemoteOptionsAsync();
        }

        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            StatusMessage = "No se pudo identificar un proyecto Compose. Selecciona uno de los proyectos detectados o indica su ruta.";
            return;
        }

        if (!await RunPreflightCoreAsync()) return;

        RequestDeploy();
        StatusMessage = "Todo está listo. Revisa el objetivo, las migraciones y confirma la ejecución.";
    }

    private async Task<bool> RunPreflightCoreAsync()
    {
        if (IsBusy) return false;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "La validación está bloqueada: no hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return false;
        }

        if (AvailableTargets.Count == 0 && !HasMigrationRecipe && !IsProjectRecipeInvalid)
        {
            await RefreshProjectModelAsync(server);
        }

        if (!ValidateConfiguration()) return false;

        IsBusy = true;
        CanDeploy = false;
        CancelPendingDeploy();
        PostChecks.Clear();
        StatusMessage = "Ejecutando prevuelo remoto del despliegue...";

        try
        {
            var result = await _preflight.CheckAsync(
                new DeploymentPreflightRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    Branch.Trim(),
                    ComposeFile.Trim(),
                    RequireEnvironmentFile,
                    EnvironmentFileName.Trim(),
                    GetRequiredServices()),
                _secretAccessor());

            Checks.Clear();
            foreach (var check in result.Checks) Checks.Add(check);

            CanDeploy = result.CanProceed;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            if (result.CanProceed)
            {
                PersistProfile(server);
            }
            StatusMessage = result.CanProceed
                ? $"El prevuelo pasó. {BuildExecutionSummary()}."
                : "El prevuelo bloqueó la ejecución. Corrige los requisitos fallidos y vuelve a ejecutar.";
            return result.CanProceed;
        }
        catch (Exception ex)
        {
            StatusMessage = $"La validación previa falló: {ex.Message}";
            CanDeploy = false;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "Guarda primero y activa un servidor para asociar este perfil de despliegue.";
            return;
        }

        if (!ValidateConfiguration()) return;

        PersistProfile(server);
        StatusMessage = $"Perfil de despliegue guardado para {server.Name}. No se guardaron secretos.";
    }

    [RelayCommand]
    private void RequestDeploy()
    {
        var server = _serverAccessor();
        if (server == null || !CanDeploy)
        {
            StatusMessage = "El prevuelo debe pasar antes de ejecutar.";
            return;
        }

        HasPendingDeploy = true;
        var productionWarning = server.Environment == ServerEnvironment.Production
            ? " Es un servidor de producción: los servicios seleccionados pueden recrearse o reiniciarse."
            : " Los servicios seleccionados pueden recrearse o reiniciarse.";
        PendingDeployMessage =
            $"Rama '{Branch.Trim()}' · {BuildExecutionSummary()} · Compose '{ComposeFile.Trim()}'.{productionWarning}";
    }

    [RelayCommand]
    private async Task ConfirmDeployAsync()
    {
        if (!HasPendingDeploy || !CanDeploy || IsBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "El servidor activo cambió. Ejecución cancelada.";
            CancelPendingDeploy();
            return;
        }

        if (!ValidateConfiguration()) return;

        IsBusy = true;
        CanDeploy = false;
        Steps.Clear();
        PostChecks.Clear();
        DeploymentOutput = string.Empty;
        StatusMessage = "Deployment is running. Do not close VPS Desk until it finishes.";
        CancelPendingDeploy();

        var deploymentServices = GetDeploymentServices();
        var operation = BuildProjectOperation();

        try
        {
            var result = await _deployment.ExecuteAsync(
                new ComposeDeploymentRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    Branch.Trim(),
                    ComposeFile.Trim(),
                    PullImages,
                    BuildImages,
                    CleanBuildCache,
                    DeployApplication: !MigrationOnly,
                    Services: deploymentServices,
                    Operation: operation),
                _secretAccessor());

            var output = new StringBuilder();
            foreach (var step in result.Steps)
            {
                Steps.Add(step);
                var marker = step.Succeeded ? "OK" : step.IsBlocking ? "FAILED" : "WARN";
                output.AppendLine($"[{marker}] {step.Label} · exit {step.ExitCode} · {step.Duration.TotalSeconds:F1}s");
                if (!string.IsNullOrWhiteSpace(step.Output))
                {
                    output.AppendLine(step.Output.TrimEnd());
                }
                output.AppendLine();
            }

            DeploymentOutput = output.ToString().TrimEnd();

            if (!result.Succeeded)
            {
                StatusMessage = $"La ejecución se detuvo en '{result.FailedStep?.Label ?? "paso desconocido"}'. Revisa la salida sanitizada.";
                return;
            }

            var elapsed = (result.FinishedAt - result.StartedAt).TotalSeconds;
            if (MigrationOnly)
            {
                StatusMessage = $"Migraciones completadas en {elapsed:F1}s · {SelectedMigrationMode?.Label}.";
                return;
            }

            StatusMessage = "Deployment commands completed. Verifying the real container and HTTP state...";
            var verification = await _postDeployVerification.VerifyAsync(
                new PostDeployVerificationRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    ComposeFile.Trim(),
                    ParseHealthUrls(),
                    deploymentServices),
                _secretAccessor());

            foreach (var check in verification.Checks) PostChecks.Add(check);

            var hasWarnings = result.HasWarnings || verification.HasWarnings;
            StatusMessage = verification.Passed
                ? hasWarnings
                    ? $"Deployment completed in {elapsed:F1}s. The release is healthy, with maintenance/post-deploy warnings to review."
                    : $"Deployment completed in {elapsed:F1}s and post-deploy verification passed."
                : $"Deployment commands completed in {elapsed:F1}s, but post-deploy verification found problems. Review the checks before considering the release healthy.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"La ejecución falló o no pudo verificarse: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDeploy() => CancelPendingDeploy();

    private IReadOnlyList<string> ParseHealthUrls()
        => HttpHealthUrls
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void InvalidatePreflight()
    {
        if (CanDeploy || Checks.Count > 0)
        {
            CanDeploy = false;
            Checks.Clear();
            StatusMessage = "La configuración cambió. Ejecuta el prevuelo otra vez.";
        }
        CancelPendingDeploy();
    }

    private bool ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            StatusMessage = "La ruta remota del repositorio es obligatoria.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Branch))
        {
            StatusMessage = "La rama Git es obligatoria.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ComposeFile))
        {
            StatusMessage = "El archivo Compose es obligatorio.";
            return false;
        }
        if (RequireEnvironmentFile && string.IsNullOrWhiteSpace(EnvironmentFileName))
        {
            StatusMessage = "El nombre del archivo de entorno es obligatorio cuando esa validación está habilitada.";
            return false;
        }
        if (IsProjectRecipeInvalid)
        {
            StatusMessage = "La receta .vpsdesk del proyecto es inválida. Corrígela antes de ejecutar operaciones.";
            return false;
        }
        if (!MigrationOnly && SelectedDeploymentTarget is null)
        {
            StatusMessage = "Selecciona un objetivo de despliegue.";
            return false;
        }
        if (MigrationOnly && !HasMigrationRecipe)
        {
            StatusMessage = "Este proyecto no declara una operación de migraciones.";
            return false;
        }
        if (ShouldExecuteMigrations && SelectedMigrationMode is null)
        {
            StatusMessage = "Selecciona el modo de migración.";
            return false;
        }
        if (RequiresMigrationInput
            && SelectedMigrationMode?.Input?.Required == true
            && string.IsNullOrWhiteSpace(MigrationInput))
        {
            StatusMessage = $"{MigrationInputLabel} es obligatorio para el modo de migración seleccionado.";
            return false;
        }
        return true;
    }

    private void CancelPendingDeploy()
    {
        HasPendingDeploy = false;
        PendingDeployMessage = string.Empty;
    }

    private void PersistProfile(ServerProfile server)
    {
        _profileStore.Upsert(new DeploymentProfile(
            server.Id,
            RemoteRepositoryPath.Trim(),
            Branch.Trim(),
            ComposeFile.Trim(),
            RequireEnvironmentFile,
            EnvironmentFileName.Trim(),
            PullImages,
            BuildImages,
            CleanBuildCache,
            SelectedDeploymentTarget?.Id,
            SelectedMigrationMode?.Id,
            RunMigrations,
            MigrationOnly));
    }
}
