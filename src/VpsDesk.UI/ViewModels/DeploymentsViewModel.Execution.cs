using System.Text;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Deployments;
using VpsDesk.Desktop.Localization;
using VpsDesk.Desktop.Services;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class DeploymentsViewModel
{
    private bool _lastSourceUpdateSucceeded;
    private bool _lastDiscoverySucceeded;
    private string? _validatedCommit;
    private string? _validatedFilesDigest;
    private int _preflightVersion;
    public Task TryAutoDiscoverRepositoryAsync()
        => DiscoverRemoteOptionsAsync();

    [RelayCommand]
    public async Task DiscoverRemoteOptionsAsync()
    {
        _lastDiscoverySucceeded = false;
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
            _lastDiscoverySucceeded = true;

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
        _lastSourceUpdateSucceeded = false;
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

        var sourcePath = RemoteRepositoryPath.Trim();
        var sourceBranch = Branch.Trim();
        var sourceProjectId = _activeProjectId;
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
                    sourcePath,
                    sourceBranch),
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
            var before = result.PreviousCommit is { Length: >= 12 } ? result.PreviousCommit[..12] : "no disponible";
            var after = result.UpdatedCommit is { Length: >= 12 } ? result.UpdatedCommit[..12] : "no disponible";
            var comparison = result.PreviousCommit is null || result.UpdatedCommit is null
                ? "No se pudo determinar si cambió la revisión."
                : hasChanges ? "Se recibieron cambios." : "El repositorio ya estaba actualizado.";
            var files = result.PreviousCommit is null || result.UpdatedCommit is null
                ? "No se pudo comparar los archivos."
                : hasChanges
                ? string.IsNullOrWhiteSpace(result.ChangedFiles) ? "No se pudo obtener la lista de archivos." : result.ChangedFiles
                : "No hay archivos nuevos que mostrar.";
            _operationFeedbackLog.AppendLine($"Rama: {sourceBranch}\nCommit anterior: {before}\nCommit actual: {after}\n{comparison}\n\nArchivos (estado y ruta):\n{files}");
            OperationFeedbackOutput = _operationFeedbackLog.ToString().TrimEnd();
            StatusMessage = hasChanges
                ? "Código actualizado correctamente. Volviendo a detectar la configuración del proyecto..."
                : "El repositorio ya estaba actualizado. Volviendo a detectar la configuración del proyecto...";
            CompleteOperationFeedback(
                true,
                $"{comparison} Revisa los detalles. No se reinició ningún contenedor.");
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

        if (!succeeded || _serverAccessor()?.Id != server.Id || _activeProjectId != sourceProjectId
            || !string.Equals(RemoteRepositoryPath.Trim(), sourcePath, StringComparison.Ordinal)
            || !string.Equals(Branch.Trim(), sourceBranch, StringComparison.Ordinal)) return;

        await DiscoverRemoteOptionsAsync();
        _lastSourceUpdateSucceeded = _lastDiscoverySucceeded;
        if (!_lastDiscoverySucceeded)
            CompleteOperationFeedback(false, "Git terminó, pero no se pudo volver a detectar la configuración. Revisa la conexión y detecta el proyecto de nuevo antes del prevuelo.");
    }

    [RelayCommand]
    private async Task PullSourceAndRetryPreflightAsync()
    {
        if (!IsPreflightFinished || IsBusy) return;
        var serverId = _serverAccessor()?.Id;
        var projectId = _activeProjectId;
        var projectPath = RemoteRepositoryPath.Trim();
        var branch = Branch.Trim();
        IsPreflightModalOpen = false;
        await PullSourceAsync();
        if (!_lastSourceUpdateSucceeded || _serverAccessor()?.Id != serverId || _activeProjectId != projectId
            || !string.Equals(RemoteRepositoryPath.Trim(), projectPath, StringComparison.Ordinal)
            || !string.Equals(Branch.Trim(), branch, StringComparison.Ordinal)) return;
        PrepareSourceUpdateContinuation(serverId, projectId, projectPath, branch, false);
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

        if (CanDeploy)
        {
            RequestDeploy();
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

        // Un pase parte siempre del código más reciente de la rama: se traen los cambios antes del
        // prevuelo para que valide (y despliegue) lo que hay en origin, no una copia atrasada.
        var serverId = server.Id;
        var projectId = _activeProjectId;
        var projectPath = RemoteRepositoryPath.Trim();
        var branch = Branch.Trim();
        await PullSourceAsync();
        if (!_lastSourceUpdateSucceeded) return;
        PrepareSourceUpdateContinuation(serverId, projectId, projectPath, branch, true);
    }

    private void PrepareSourceUpdateContinuation(Guid? serverId, Guid? projectId, string path, string branch, bool confirm)
    {
        _sourceUpdateServerId = serverId;
        _sourceUpdateProjectId = projectId;
        _sourceUpdatePath = path;
        _sourceUpdateBranch = branch;
        _sourceUpdateContinueToConfirmation = confirm;
        CanContinueAfterSourceUpdate = true;
        IsOperationFeedbackModalOpen = true;
        StatusMessage = "Revisa los cambios y continúa al prevuelo cuando estés listo.";
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
        var validationVersion = _preflightVersion;
        var projectId = _activeProjectId;
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

            if (_recipeService is not null)
            {
                var currentRecipe = await _recipeService.LoadAsync(server, RemoteRepositoryPath.Trim(), _secretAccessor(), token).WaitAsync(token);
                var sameRecipe = currentRecipe.Status switch
                {
                    ProjectRecipeStatus.NotFound => _activeRecipe is null && !HasProjectRecipe,
                    ProjectRecipeStatus.Loaded => _activeRecipe is not null
                        && string.Equals(currentRecipe.FileName, ProjectRecipeFileName, StringComparison.Ordinal)
                        && System.Text.Json.JsonSerializer.Serialize(currentRecipe.Recipe) == System.Text.Json.JsonSerializer.Serialize(_activeRecipe),
                    _ => false
                };
                if (!sameRecipe)
                    result = result with { Checks = result.Checks.Append(new PreflightCheckResult("recipe_changed", "Receta del proyecto", PreflightCheckStatus.Failed,
                        "La receta cambió o no se pudo verificar. Detecta de nuevo el proyecto y revisa las opciones antes del pase.", true)).ToArray() };
            }
            if (_serverAccessor()?.Id != server.Id || _activeProjectId != projectId || validationVersion != _preflightVersion)
                result = result with { Checks = result.Checks.Append(new PreflightCheckResult("context_changed", "Configuración del pase", PreflightCheckStatus.Failed,
                    "La configuración cambió durante el prevuelo. Revísala y vuelve a comprobar.", true)).ToArray() };

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
            _validatedCommit = result.CanProceed ? result.VerifiedCommit : null;
            _validatedFilesDigest = result.CanProceed ? result.VerifiedFilesDigest : null;
            if (_validatedCommit is null || _validatedFilesDigest is null) CanDeploy = false;
            LastChecked = DateTimeOffset.Now.ToString("HH:mm:ss");
            if (CanDeploy)
            {
                PersistProfile(server);
            }
            PreflightModalMessage = LocalizationService.T(CanDeploy
                ? "Deployments_PreflightPassed"
                : "Deployments_PreflightBlocked");
            StatusMessage = CanDeploy
                ? $"El prevuelo pasó para el commit {_validatedCommit![..12]}. {BuildExecutionSummary()}."
                : "El prevuelo bloqueó la ejecución. Corrige los requisitos fallidos y vuelve a ejecutar.";
            return CanDeploy;
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

        if (!TryReadRecovery(server, out var recovery)) return;
        RequiresRecoveryReview = recovery?.NeedsReview == true;
        RecoveryReviewConfirmed = false;
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
        if (_validatedCommit is { Length: >= 12 } commit)
            PendingNotices.Add($"Se desplegará el commit {commit[..12]} validado; este pase no hará otro git pull.");
        if (recovery is not null)
            PendingNotices.Add($"Pase pendiente de revisión ({recovery.ProjectPath}). El snapshot existente se conservará y las copias no se rotarán.");
        OnPropertyChanged(nameof(HasPendingNotices));

        HasPendingDeploy = true;
    }

    [RelayCommand]
    private async Task ConfirmDeployAsync()
    {
        if (!HasPendingDeploy || !CanDeploy || _validatedCommit is null || _validatedFilesDigest is null || IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "El servidor activo cambió. Ejecución cancelada.";
            CancelPendingDeploy();
            return;
        }

        if (!ValidateConfiguration()) return;

        if (!TryReadRecovery(server, out var prior)) return;
        var projectPath = RemoteRepositoryPath.Trim().TrimEnd('/');
        var composeFile = ComposeFile.Trim();
        if (prior is not null && (prior.ProjectPath != projectPath || prior.ComposeFile != composeFile))
        {
            Toast.Warning($"Primero resuelve el pase pendiente de {prior.ProjectPath} ({prior.ComposeFile}).", "Recuperación pendiente");
            return;
        }
        if (prior?.NeedsReview == true && !RecoveryReviewConfirmed)
        {
            Toast.Warning("Confirma que revisaste el pase anterior y sus posibles cambios parciales.", "Pase pendiente de revisión");
            return;
        }

        var branch = Branch.Trim();
        var secret = _secretAccessor();
        var deploymentServices = GetDeploymentServices();
        var operation = BuildProjectOperation();
        var migrationOnly = MigrationOnly;
        var dataSaver = IsDataSaverActive;
        var request = new ComposeDeploymentRequest(server, projectPath, branch, composeFile,
            PullImages, BuildImages, CleanBuildCache, DeployApplication: !migrationOnly,
            Services: deploymentServices, Operation: operation, DataSaver: dataSaver,
            VerifiedCommit: _validatedCommit, VerifiedFilesDigest: _validatedFilesDigest,
            DetachLongSteps: true);
        var healthUrls = ParseHealthUrls().Concat(prior?.HealthUrls ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var verificationServices = prior is null ? deploymentServices
            : prior.Services is not { Count: > 0 } || deploymentServices.Count == 0 ? []
            : deploymentServices.Concat(prior.Services).Distinct(StringComparer.Ordinal).ToArray();
        var verificationRequest = new PostDeployVerificationRequest(server, projectPath, composeFile,
            healthUrls, verificationServices);

        IDisposable? backgroundExecution = null;
        try
        {
            backgroundExecution = DeploymentBackgroundExecution.Start?.Invoke();
        }
        catch (Exception ex)
        {
            Toast.Warning($"No se pudo mantener activo el pase en segundo plano: {UserFacingErrors.Describe(ex)}. Mantén la app abierta.", "Protección en segundo plano no disponible");
        }
        using var backgroundScope = backgroundExecution;
        DeploymentBackgroundExecution.Report?.Invoke("Preparando respaldos y recuperación");
        if (!await TakeAppBackupBeforeDeployAsync(server)) return;
        if (_serverAccessor()?.Id != server.Id || !HasSameDeploymentContext(projectPath, composeFile, branch)) return;
        if (prior is null)
        {
            ClearDeploymentRecovery();
            if (!await TakeSnapshotBeforeDeployAsync(server, verificationRequest, secret)) return;
        }
        if (_serverAccessor()?.Id != server.Id || !HasSameDeploymentContext(projectPath, composeFile, branch)) return;

        var recovery = new DeploymentRecoveryRecord(projectPath, composeFile,
            prior is null ? _deploymentSnapshot : prior.Snapshot,
            operation is not null, "En curso o interrumpido", prior?.PreviousCommit, prior?.DeployedCommit, verificationServices, healthUrls, ExecutionNeedsReview: true);
        try { _profileStore.SaveRecovery(server.Id, recovery); }
        catch (Exception exception)
        {
            Toast.Error(exception, "No se pudo guardar el estado de recuperación. Pase cancelado");
            return;
        }
        _deploymentSnapshot = recovery.Snapshot;
        _deploymentSnapshotServerId = server.Id;

        _previousDeploymentCommit = recovery.PreviousCommit;
        _deployedCommit = recovery.DeployedCommit;
        IsBusy = true;
        CanDeploy = false;
        ActiveDeploymentSummary = $"{BuildExecutionSummary()} · Rama: {branch} · Compose: {composeFile}";
        Steps.Clear();
        PostChecks.Clear();
        DeploymentOutput = string.Empty;
        DeploymentOutcomeTitle = "Pase en curso";
        Toast.Info($"Desplegando la rama '{Branch.Trim()}'. Te avisaremos al terminar.", "Despliegue iniciado");
        DeploymentNextAction = backgroundExecution is null
            ? "Mantén la aplicación abierta mientras se comprueban los pasos y el resultado."
            : "El pase continúa mientras se comprueban los pasos y el resultado.";
        StatusMessage = LocalizationService.T("Deployments_StatusRunning");
        CancelPendingDeploy();

        var deploymentCancellation = StartDeploymentCancellation();
        try
        {
            var result = await _deployment.ExecuteAsync(request, secret, deploymentCancellation);
            if (result.FailedStep is { Code: "verify_revision" } revisionFailure)
            {
                // The first step is read-only. Nothing in the deployment was applied, so
                // do not leave a new recovery record that suggests a partial deployment.
                if (prior is null)
                {
                    _profileStore.ClearRecovery(server.Id);
                    ClearDeploymentRecovery();
                }
                else
                {
                    _profileStore.SaveRecovery(server.Id, prior);
                    if (_serverAccessor()?.Id == server.Id) ShowSavedRecovery(server, prior);
                }
                if (_serverAccessor()?.Id != server.Id) return;
                while (_progressQueue.TryDequeue(out var revisionUpdate)) ApplyProgress(revisionUpdate);
                DeploymentOutput = revisionFailure.Output;
                DeploymentOutcomeTitle = "Pase no iniciado";
                DeploymentNextAction = "Comprueba el repositorio y ejecuta el prevuelo otra vez. No se aplicaron los pasos del pase.";
                StatusMessage = "La revisión no pudo validarse; el pase no se inició.";
                Toast.Warning(DeploymentNextAction, "Revisión del pase");
                return;
            }
            recovery = recovery with
            {
                PreviousCommit = recovery.PreviousCommit ?? result.PreviousCommit,
                DeployedCommit = result.DeployedCommit ?? recovery.DeployedCommit,
                ExecutionNeedsReview = result.FailedStep is { ExitCode: -1 },
                Status = result.Succeeded ? "Pendiente de verificación" : "Pase fallido",
                MigrationNeedsReview = operation is not null &&
                    result.Steps.Any(step => step.Code == operation.Code && !step.Succeeded)
            };
            _profileStore.SaveRecovery(server.Id, recovery);
            if (_serverAccessor()?.Id != server.Id) return;
            _previousDeploymentCommit = recovery.PreviousCommit;
            _deployedCommit = recovery.DeployedCommit;

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
                if (result.FailedStep is { ExitCode: not -1 })
                    DeploymentFinale = DeploymentFinaleOutcome.Failed;
                DeploymentNextAction = "Revisa el paso fallido y los servicios. Puede haber cambios aplicados. Ejecuta un nuevo prevuelo antes de repetir.";
                Toast.Error(StatusMessage, "El despliegue se detuvo");
                DeploymentBackgroundExecution.Complete?.Invoke("Pase detenido", "Revisa el paso fallido y el estado del servidor.");
                await CheckFailedDeploymentAsync(server, verificationRequest, secret);
                return;
            }

            var elapsed = (result.FinishedAt - result.StartedAt).TotalSeconds;
            StatusMessage = LocalizationService.T("Deployments_StatusVerifying");
            DeploymentBackgroundExecution.Report?.Invoke("Comprobando el resultado del pase");
            var verification = await _postDeployVerification.VerifyAsync(
                verificationRequest, secret);

            if (_serverAccessor()?.Id != server.Id) return;
            foreach (var check in verification.Checks) PostChecks.Add(check);

            if (verification.IsHealthy && !recovery.NeedsReview)
            {
                _profileStore.ClearRecovery(server.Id);
                ClearDeploymentRecovery();
            }
            else
            {
                recovery = recovery with { Status = verification.Passed ? "Salud pendiente de confirmar" : "Pase con problemas" };
                _profileStore.SaveRecovery(server.Id, recovery);
                ShowSavedRecovery(server, recovery);
            }

            var hasWarnings = result.HasWarnings || verification.HasWarnings;
            DeploymentOutcomeTitle = !verification.Passed ? "Pase con problemas" : hasWarnings ? "Pase completado con avisos" : "Pase verificado";
            DeploymentFinale = verification.IsHealthy && !result.HasWarnings
                ? DeploymentFinaleOutcome.Success
                : verification.HasConfirmedFailure ? DeploymentFinaleOutcome.Failed : DeploymentFinaleOutcome.None;
            DeploymentBackgroundExecution.Complete?.Invoke(DeploymentOutcomeTitle,
                verification.Passed ? "Abre VPS Desk para consultar el resultado." : "Revisa las comprobaciones del servidor.");
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
            {
                Toast.Error($"Los comandos terminaron en {elapsed:F1}s, pero la verificación posterior encontró problemas. Revisa las comprobaciones antes de dar la versión por buena.", "Despliegue con problemas");
                ApplyDeploymentRecovery(verification);
            }
            else if (hasWarnings)
                Toast.Warning($"Pase ejecutado en {elapsed:F1}s. Revisa los avisos de las comprobaciones.", "Despliegue completado con avisos");
            else
                Toast.Success($"Desplegado en {elapsed:F1}s y la verificación posterior pasó.", "Despliegue completado");
        }
        catch (OperationCanceledException) when (IsDeploymentCancellationRequested(deploymentCancellation))
        {
            if (_serverAccessor()?.Id != server.Id) return;
            StatusMessage = "Pase cancelado. Se detuvo el trabajo remoto y se guardó el estado para revisión.";
            DeploymentOutcomeTitle = "Pase cancelado";
            DeploymentNextAction = "Revisa los servicios y la salida antes de reiniciar. Puedes repetir el pase aunque Git no tenga cambios.";
            DeploymentOutput += "\n[Cancelación] Se solicitó detener el pase desde VPS Desk.";
            DeploymentBackgroundExecution.Complete?.Invoke("Pase cancelado", "Revisa el estado del servidor antes de reiniciar.");
            await CheckFailedDeploymentAsync(server, verificationRequest, secret);
        }
        catch (Exception ex)
        {
            if (_serverAccessor()?.Id != server.Id) return;
            var friendly = UserFacingErrors.Describe(ex);
            StatusMessage = $"La ejecución falló o no pudo verificarse: {friendly}";
            DeploymentOutcomeTitle = "Pase interrumpido · estado sin confirmar";
            while (_progressQueue.TryDequeue(out var pending)) ApplyProgress(pending);
            if (_liveDeploymentOutput.Length > 0)
                DeploymentOutput = _liveDeploymentOutput.ToString().TrimEnd();
            DeploymentNextAction = "Recupera la conexión y revisa contenedores, logs y migraciones antes de repetir. El servidor puede haber aplicado cambios.";
            DeploymentOutput += $"\n[Interrupción] {friendly}";
            Toast.Error(friendly, "El despliegue falló");
            DeploymentBackgroundExecution.Complete?.Invoke("Pase interrumpido", "El estado no está confirmado. Revisa el servidor antes de repetir.");
            await CheckFailedDeploymentAsync(server, verificationRequest, secret);
        }
        finally
        {
            FinishDeploymentCancellation();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartDeploymentAsync()
    {
        if (IsBusy || IsSnapshotBusy || IsAppBackupBusy) return;

        DeploymentFinale = DeploymentFinaleOutcome.None;
        DeploymentNextAction = "Reiniciando el flujo completo: actualización Git, prevuelo y confirmación.";
        StatusMessage = "Reiniciando el pase con la configuración seleccionada…";
        await StartDeploymentAsync();
    }

    [RelayCommand]
    private void CancelDeploy() => CancelPendingDeploy();

    private bool HasSameDeploymentContext(string path, string compose, string branch)
        => RemoteRepositoryPath.Trim().TrimEnd('/') == path && ComposeFile.Trim() == compose && Branch.Trim() == branch;

    private IReadOnlyList<string> ParseHealthUrls()
        => HttpHealthUrls
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void InvalidatePreflight()
    {
        _preflightVersion++;
        _validatedCommit = null;
        _validatedFilesDigest = null;
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
            AppBackupPostgresContainer.Trim(),
            HttpHealthUrls.Trim());
}
