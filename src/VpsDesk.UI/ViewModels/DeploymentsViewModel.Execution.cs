using System.Text;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Localization;
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
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;
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
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = $"La detección falló. Revisa la conexión SSH y la ruta del repositorio: {friendly}";
            Toast.Error(friendly, "La detección falló");
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
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = $"No se pudo detectar automáticamente el proyecto: {friendly}";
            Toast.Error(friendly, "No se pudo detectar el proyecto");
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
    public async Task PullSourceAsync()
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;

        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "No hay un servidor activo. Selecciónalo y conéctalo primero en Servidores.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RemoteRepositoryPath))
        {
            StatusMessage = "Indica o detecta primero la ruta remota del repositorio.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Branch))
        {
            StatusMessage = "Selecciona la rama Git que quieres actualizar.";
            return;
        }

        CanDeploy = false;
        CancelPendingDeploy();
        Checks.Clear();
        Steps.Clear();
        PostChecks.Clear();
        DeploymentOutput = string.Empty;
        BeginOperationFeedback(
            "Trayendo cambios",
            $"Preparando conexión con la rama '{Branch.Trim()}'...");
        IsBusy = true;
        StatusMessage = $"Actualizando la rama '{Branch.Trim()}' sin desplegar contenedores...";

        var succeeded = false;
        var hasChanges = false;
        _gitUpdate.ProgressChanged += OnGitUpdateProgressChanged;
        try
        {
            var result = await _gitUpdate.PullAsync(
                new GitRepositoryUpdateRequest(
                    server,
                    RemoteRepositoryPath.Trim(),
                    Branch.Trim()),
                _secretAccessor());

            var output = new StringBuilder();
            foreach (var step in result.Steps)
            {
                Steps.Add(step);
                var marker = step.Succeeded ? "OK" : "FAILED";
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
                StatusMessage = $"No se pudieron traer los cambios: falló '{result.FailedStep?.Label ?? "Git"}'. Revisa la salida sanitizada.";
                CompleteOperationFeedback(false, $"No se completó la actualización: falló {result.FailedStep?.Label ?? "Git"}.");
                return;
            }

            succeeded = true;
            hasChanges = result.HasChanges;
            StatusMessage = hasChanges
                ? "Código actualizado correctamente. Volviendo a detectar la configuración del proyecto..."
                : "El repositorio ya estaba actualizado. Volviendo a detectar la configuración del proyecto...";
            CompleteOperationFeedback(
                true,
                hasChanges
                    ? "Cambios recibidos correctamente. No se reinició ningún contenedor."
                    : "El repositorio ya estaba actualizado. No se reinició ningún contenedor.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudieron traer los cambios: {UserFacingErrors.Describe(ex)}";
            CompleteOperationFeedback(false, "La actualización no pudo completarse. Revisa el terminal.");
        }
        finally
        {
            _gitUpdate.ProgressChanged -= OnGitUpdateProgressChanged;
            IsBusy = false;
        }

        if (!succeeded) return;

        await DiscoverRemoteOptionsAsync();
    }

    [RelayCommand]
    public async Task StartDeploymentAsync()
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;

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

        // Desde DESPLEGAR el prevuelo no se queda como un modal más: si pasa, se cierra y se
        // muestra directamente la confirmación (que resume lo que se va a hacer).
        if (!await RunPreflightCoreAsync(continueToConfirmation: true)) return;

        RequestDeploy();
        StatusMessage = "Todo está listo. Revisa el objetivo, las migraciones y confirma la ejecución.";
    }

    private async Task<bool> RunPreflightCoreAsync(bool continueToConfirmation = false)
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return false;
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
        Checks.Clear();
        PostChecks.Clear();
        var token = BeginPreflightModal();
        StatusMessage = "Ejecutando prevuelo remoto del despliegue...";

        try
        {
            // WaitAsync: aunque la capa SSH tarde en reaccionar, cancelar o agotar el plazo
            // libera la pantalla al instante.
            var result = await _preflight.CheckAsync(
                    new DeploymentPreflightRequest(
                        server,
                        RemoteRepositoryPath.Trim(),
                        Branch.Trim(),
                        ComposeFile.Trim(),
                        RequireEnvironmentFile,
                        EnvironmentFileName.Trim(),
                        GetRequiredServices()),
                    _secretAccessor(),
                    token)
                .WaitAsync(token);

            var totalChecks = Math.Max(1, result.Checks.Count);
            for (var index = 0; index < result.Checks.Count; index++)
            {
                var check = result.Checks[index];
                Checks.Add(check);
                PreflightModalMessage = LocalizationService.Current.Format(
                    "Deployments_PreflightChecking", index + 1, totalChecks, check.Label);
                await Task.Delay(60, token);
            }

            CountPreflightChecks();
            CanDeploy = result.CanProceed;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            if (result.CanProceed)
            {
                PersistProfile(server);
            }
            PreflightModalMessage = LocalizationService.T(result.CanProceed
                ? "Deployments_PreflightPassed"
                : "Deployments_PreflightBlocked");
            StatusMessage = result.CanProceed
                ? $"El prevuelo pasó. {BuildExecutionSummary()}."
                : "El prevuelo bloqueó la ejecución. Corrige los requisitos fallidos y vuelve a ejecutar.";
            return result.CanProceed;
        }
        catch (OperationCanceledException)
        {
            CanDeploy = false;
            if (_preflightCancelledByUser)
            {
                // Lo canceló la persona: no hay nada que explicar, se cierra y se avisa en el estado.
                IsPreflightModalOpen = false;
                StatusMessage = LocalizationService.T("Deployments_PreflightCancelled");
            }
            else
            {
                PreflightModalMessage = LocalizationService.T("Deployments_PreflightTimeout");
                StatusMessage = LocalizationService.T("Deployments_PreflightTimeout");
            }
            return false;
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            PreflightModalMessage = friendly;
            StatusMessage = $"La validación previa falló: {friendly}";
            Toast.Error(friendly, "La validación previa falló");
            CanDeploy = false;
            return false;
        }
        finally
        {
            // El estado del modal se fija antes de soltar IsBusy: el botón de cierre ya no depende de él.
            IsPreflightRunning = false;
            if (IsPreflightModalOpen && CanDeploy && continueToConfirmation) IsPreflightModalOpen = false;
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

        RefreshServerContext();
        PendingExecution = BuildExecutionSummary();
        PendingDeployMessage = LocalizationService.T(server.Environment == ServerEnvironment.Production
            ? "Deployments_ConfirmWarningProd"
            : "Deployments_ConfirmWarning");

        PendingNotices.Clear();
        foreach (var notice in new[] { BuildSnapshotNotice(), BuildAppBackupNotice(), BuildDataSaverNotice() })
        {
            if (!string.IsNullOrWhiteSpace(notice)) PendingNotices.Add(notice.Trim());
        }
        OnPropertyChanged(nameof(HasPendingNotices));

        HasPendingDeploy = true;
    }

    [RelayCommand]
    private async Task ConfirmDeployAsync()
    {
        if (!HasPendingDeploy || !CanDeploy || IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "El servidor activo cambió. Ejecución cancelada.";
            CancelPendingDeploy();
            return;
        }

        if (!ValidateConfiguration()) return;

        // Punto de restauración del VPS completo: sin él no se toca el servidor.
        if (!await TakeSnapshotBeforeDeployAsync(server)) return;

        // Copia descargable de la aplicación: conserva varias versiones, a diferencia del snapshot.
        if (!await TakeAppBackupBeforeDeployAsync(server)) return;

        IsBusy = true;
        CanDeploy = false;
        Steps.Clear();
        PostChecks.Clear();
        DeploymentOutput = string.Empty;
        DeploymentOutcomeTitle = "Pase en curso";
        Toast.Info($"Desplegando la rama '{Branch.Trim()}'. Te avisaremos al terminar.", "Despliegue iniciado");
        DeploymentNextAction = "Mantén la aplicación abierta. Los pasos reservan el último tramo para verificar el resultado.";
        StatusMessage = LocalizationService.T("Deployments_StatusRunning");
        CancelPendingDeploy();

        var deploymentServices = GetDeploymentServices();
        var operation = BuildProjectOperation();
        // La red se evalúa ahora: puede haber cambiado desde que se abrió la pantalla.
        var dataSaver = IsDataSaverActive;
        RefreshDataSaverStatus();

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
                    Operation: operation,
                    DataSaver: dataSaver),
                _secretAccessor());

            while (_progressQueue.TryDequeue(out var pending)) ApplyProgress(pending);
            Steps.Clear();
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

            DeploymentOutput = _liveDeploymentOutput.Length > 0
                ? _liveDeploymentOutput.ToString().TrimEnd()
                : output.ToString().TrimEnd();

            if (!result.Succeeded)
            {
                StatusMessage = $"La ejecución se detuvo en '{result.FailedStep?.Label ?? "paso desconocido"}'. Revisa la salida sanitizada.";
                DeploymentOutcomeTitle = "Pase detenido";
                DeploymentNextAction = "Revisa el paso fallido y los servicios. Puede haber cambios aplicados. Ejecuta un nuevo prevuelo antes de repetir.";
                Toast.Error(StatusMessage, "El despliegue se detuvo");
                return;
            }

            var elapsed = (result.FinishedAt - result.StartedAt).TotalSeconds;
            if (MigrationOnly)
            {
                DeploymentOutcomeTitle = "Migraciones completadas";
                DeploymentProgressPercent = 100;
                DeploymentNextAction = "Revisa la salida de las migraciones antes de continuar.";
                StatusMessage = $"Migraciones completadas en {elapsed:F1}s · {SelectedMigrationMode?.Label}.";
                Toast.Success(StatusMessage, "Migraciones completadas");
                return;
            }

            StatusMessage = LocalizationService.T("Deployments_StatusVerifying");
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
            DeploymentOutcomeTitle = !verification.Passed ? "Pase con problemas" : hasWarnings ? "Pase completado con avisos" : "Pase verificado";
            DeploymentProgressPercent = 100;
            DeploymentNextAction = verification.Passed
                ? "Revisa las comprobaciones y los avisos. El resultado queda disponible al volver a esta pantalla."
                : "Revisa las comprobaciones fallidas y los logs de los servicios antes de repetir el pase.";
            StatusMessage = LocalizationService.Current.Format(
                !verification.Passed ? "Deployments_StatusDoneProblems"
                : hasWarnings ? "Deployments_StatusDoneWarnings"
                : "Deployments_StatusDone",
                elapsed.ToString("F1"));

            if (!verification.Passed)
                Toast.Error($"Los comandos terminaron en {elapsed:F1}s, pero la verificación posterior encontró problemas. Revisa las comprobaciones antes de dar la versión por buena.", "Despliegue con problemas");
            else if (hasWarnings)
                Toast.Warning($"Desplegado en {elapsed:F1}s y saludable, con avisos de mantenimiento o posteriores que conviene revisar.", "Despliegue completado con avisos");
            else
                Toast.Success($"Desplegado en {elapsed:F1}s y la verificación posterior pasó.", "Despliegue completado");
        }
        catch (Exception ex)
        {
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = $"La ejecución falló o no pudo verificarse: {friendly}";
            DeploymentOutcomeTitle = "Pase interrumpido · estado sin confirmar";
            while (_progressQueue.TryDequeue(out var pending)) ApplyProgress(pending);
            if (_liveDeploymentOutput.Length > 0)
                DeploymentOutput = _liveDeploymentOutput.ToString().TrimEnd();
            DeploymentNextAction = "Recupera la conexión y revisa contenedores, logs y migraciones antes de repetir. El servidor puede haber aplicado cambios.";
            DeploymentOutput += $"\n[Interrupción] {friendly}";
            Toast.Error(friendly, "El despliegue falló");
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
        PendingExecution = string.Empty;
        PendingNotices.Clear();
        OnPropertyChanged(nameof(HasPendingNotices));
    }

    private void PersistProfile(ServerProfile server)
        => _profileStore.Upsert(CreateDeploymentProfile(server));

    private DeploymentProfile CreateDeploymentProfile(ServerProfile server)
        => new DeploymentProfile(
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
            MigrationOnly,
            _activeProjectId,
            BackupBeforeDeploy,
            KeepCopies,
            StoreAppBackupOnServer,
            SnapshotBeforeDeploy,
            AppBackupPostgresContainer.Trim());
}
