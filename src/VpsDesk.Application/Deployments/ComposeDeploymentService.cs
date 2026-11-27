using System.Text.RegularExpressions;
using VpsDesk.Application.Abstractions;
using VpsDesk.Application.Logging;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Application.Deployments;

public sealed record DeploymentProjectOperationRequest(
    string Code,
    string Label,
    string CommandTemplate,
    string? Input = null,
    int TimeoutSeconds = 600,
    bool IsBlocking = true);

public sealed record ComposeDeploymentRequest(
    ServerProfile Server,
    string RemoteRepositoryPath,
    string Branch,
    string ComposeFile,
    bool PullImages = true,
    bool BuildImages = true,
    bool CleanBuildCache = false,
    bool DeployApplication = true,
    IReadOnlyList<string>? Services = null,
    DeploymentProjectOperationRequest? Operation = null,
    bool DataSaver = false,
    string? VerifiedCommit = null,
    string? VerifiedFilesDigest = null,
    /// <summary>
    /// Los pasos que no se pueden repetir (pull, up, migraciones) corren desacoplados de la sesión SSH: si la conexión se corta,
    /// siguen en el servidor y la aplicación espera y vuelve a consultar, en vez de repetirlos o darlos por fallidos.
    /// </summary>
    bool DetachLongSteps = false);

public sealed record DeploymentStepResult(
    string Code,
    string Label,
    bool Succeeded,
    int ExitCode,
    string Output,
    TimeSpan Duration,
    bool IsBlocking = true);

public enum DeploymentProgressState
{
    Started,
    /// <summary>Una línea de salida del paso, emitida mientras el paso sigue en ejecución.</summary>
    Output,
    OutputSnapshot,
    Completed
}

public sealed record DeploymentProgressUpdate(
    string Code,
    string Label,
    DeploymentProgressState State,
    bool? Succeeded = null,
    int? ExitCode = null,
    string? Output = null,
    TimeSpan? Duration = null,
    int StepIndex = 0,
    int TotalSteps = 0,
    bool IsBlocking = true);

public sealed record ComposeDeploymentResult(
    IReadOnlyList<DeploymentStepResult> Steps,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? PreviousCommit,
    string? DeployedCommit)
{
    public bool Succeeded => Steps.Count > 0 && Steps.Where(x => x.IsBlocking).All(x => x.Succeeded);
    public bool HasWarnings => Steps.Any(x => !x.Succeeded && !x.IsBlocking);
    public DeploymentStepResult? FailedStep => Steps.FirstOrDefault(x => x.IsBlocking && !x.Succeeded);
}

public interface IComposeDeploymentService
{
    event Action<DeploymentProgressUpdate>? ProgressChanged;

    Task<ComposeDeploymentResult> ExecuteAsync(
        ComposeDeploymentRequest request,
        string? secret,
        CancellationToken cancellationToken = default);
}

public sealed class ComposeDeploymentService(ISshCommandExecutor ssh) : IComposeDeploymentService
{
    private static readonly Regex SafeServiceName = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);
    private static readonly Regex UnresolvedToken = new("\\{\\{[^}]+\\}\\}", RegexOptions.Compiled);

    public event Action<DeploymentProgressUpdate>? ProgressChanged;

    public async Task<ComposeDeploymentResult> ExecuteAsync(
        ComposeDeploymentRequest request,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RemoteRepositoryPath))
            throw new ArgumentException("Remote repository path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Branch))
            throw new ArgumentException("Git branch is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ComposeFile))
            throw new ArgumentException("Compose file is required.", nameof(request));
        if (!request.DeployApplication && request.Operation is null)
            throw new ArgumentException("The request must deploy the application or execute a project operation.", nameof(request));
        if (request.VerifiedCommit is not { } verifiedCommit || !Regex.IsMatch(verifiedCommit, "\\A(?:[0-9a-f]{40}|[0-9a-f]{64})\\z"))
            throw new ArgumentException("A verified Git commit is required. Run preflight again.", nameof(request));
        if (request.VerifiedFilesDigest is not { } verifiedDigest || !Regex.IsMatch(verifiedDigest, "\\A[0-9a-f]{64}\\z"))
            throw new ArgumentException("Verified deployment files are required. Run preflight again.", nameof(request));

        var startedAt = DateTimeOffset.UtcNow;
        var steps = new List<DeploymentStepResult>();
        var repo = DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'));
        var branch = DeploymentPreflightService.ShellQuote(request.Branch.Trim());
        var compose = DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim());
        var serviceArguments = BuildServiceArguments(request.Services);
        var expectedCommit = DeploymentPreflightService.ShellQuote(verifiedCommit);
        var revisionGuard = $"REPO={repo}; COMPOSE={compose}; " + DeploymentPreflightService.BuildFilesDigestCommand(emit: false)
            + $"if [ \"$FILES_DIGEST\" != {DeploymentPreflightService.ShellQuote(verifiedDigest)} ] || "
            + $"[ \"$(git -C {repo} rev-parse --verify HEAD 2>/dev/null)\" != {expectedCommit} ] || "
            + $"[ \"$(git -C {repo} symbolic-ref --quiet --short HEAD 2>/dev/null)\" != {branch} ] || "
            + $"! git -C {repo} diff --quiet --no-ext-diff -- || ! git -C {repo} diff --cached --quiet --no-ext-diff --; then "
            + "echo 'La revisión o los archivos versionados cambiaron después del prevuelo. Repite el prevuelo.' >&2; exit 1; fi\n";

        var previousCommit = await TryReadCommitAsync(request, repo, secret, cancellationToken);

        var commands = new List<(string Code, string Label, string Command, TimeSpan Timeout, bool IsBlocking)>
        {
            ("verify_revision", "Verificar revisión validada", revisionGuard + "echo 'Revisión del prevuelo confirmada.'", TimeSpan.FromSeconds(30), true)
        };

        if (request.DeployApplication && request.PullImages)
        {
            commands.Add((
                "compose_pull",
                "Pull container images",
                $"cd -- {repo} && docker compose -f {compose} pull{serviceArguments}",
                TimeSpan.FromMinutes(5),
                true));
        }

        if (request.DeployApplication)
        {
            // Con servicios concretos (p. ej. solo Front) el pase debe tocar únicamente esos
            // servicios. Sin --no-deps, Compose también recrearía las dependencias cuya
            // configuración haya cambiado (p. ej. la API si recibe metadatos distintos en
            // cada pase). Antes se levantan, sin recrear, las dependencias que falten o
            // estén detenidas, porque --no-deps ya no lo haría.
            var isolateServices = serviceArguments.Length > 0;
            if (isolateServices)
            {
                commands.Add((
                    "compose_deps",
                    "Ensure dependencies are running",
                    $"cd -- {repo} && docker compose -f {compose} up -d --no-recreate{serviceArguments}",
                    TimeSpan.FromMinutes(10),
                    true));
            }

            var buildFlag = request.BuildImages ? " --build" : string.Empty;
            var noDepsFlag = isolateServices ? " --no-deps" : string.Empty;
            commands.Add((
                "compose_up",
                "Apply Docker Compose",
                $"cd -- {repo} && docker compose -f {compose} up -d{buildFlag}{noDepsFlag}{serviceArguments}",
                TimeSpan.FromMinutes(10),
                true));
        }

        if (request.Operation is not null)
        {
            var operation = request.Operation;
            commands.Add((
                operation.Code,
                operation.Label,
                RenderOperationCommand(operation, request),
                TimeSpan.FromSeconds(Math.Clamp(operation.TimeoutSeconds, 10, 3600)),
                operation.IsBlocking));
        }

        commands.Add((
            "compose_ps",
            "Read deployment status",
            $"cd -- {repo} && docker compose -f {compose} ps{serviceArguments}",
            TimeSpan.FromSeconds(45),
            false));

        if (request.DeployApplication && request.CleanBuildCache)
        {
            commands.Add((
                "build_cache_prune",
                "Clean Docker build cache",
                "docker builder prune -f",
                TimeSpan.FromMinutes(3),
                false));
        }

        for (var index = 0; index < commands.Count; index++)
        {
            var step = commands[index];
            var stepIndex = index + 1;
            cancellationToken.ThrowIfCancellationRequested();
            ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                step.Code,
                step.Label,
                DeploymentProgressState.Started,
                StepIndex: stepIndex,
                TotalSteps: commands.Count,
                IsBlocking: step.IsBlocking));

            // Las líneas se sanean una a una antes de salir de este servicio, igual que
            // la salida final del paso, para que ningún secreto llegue a la UI en vivo.
            var terminal = new TerminalTextBuffer();

            // Modo ahorro de datos: la salida se acumula en el VPS y solo vuelve el final
            // (tope por paso), sin transmitir el log en vivo. El código de salida se conserva.
            var guardedCommand = step.Code == "verify_revision" ? step.Command : revisionGuard + step.Command;
            var commandText = request.DataSaver
                ? LimitRemoteOutput(guardedCommand, DataSaverMaxStepOutputBytes)
                : guardedCommand;
            Action<string, bool>? onChunk = request.DataSaver
                ? null
                : (chunk, completed) =>
                {
                    terminal.Append(chunk);
                    ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                        step.Code,
                        step.Label,
                        DeploymentProgressState.OutputSnapshot,
                        Output: LogSanitizer.Sanitize(terminal.GetText(holdTrailingToken: !completed)),
                        StepIndex: stepIndex,
                        TotalSteps: commands.Count,
                        IsBlocking: step.IsBlocking));
                };

            var stepRequest = new SshCommandRequest(
                request.Server,
                "export NO_COLOR=1 COMPOSE_ANSI=never BUILDKIT_PROGRESS=plain;\n" + commandText,
                step.Timeout,
                OutputChunkReceived: onChunk);

            SshCommandResult result;
            if (request.DetachLongSteps && !IdempotentStepCodes.Contains(step.Code))
            {
                // Sin el envoltorio de modo ahorro: el resultado ya viene por consultas y aquí se recorta la salida.
                var detachedCommand = "export NO_COLOR=1 COMPOSE_ANSI=never BUILDKIT_PROGRESS=plain;\n" + guardedCommand;
                void Status(string message)
                {
                    terminal.Append($"\n[{message}]\n");
                    ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                        step.Code, step.Label, DeploymentProgressState.OutputSnapshot,
                        Output: LogSanitizer.Sanitize(terminal.GetText()), StepIndex: stepIndex,
                        TotalSteps: commands.Count, IsBlocking: step.IsBlocking));
                }

                var detached = await new DetachedRemoteJob(ssh).RunAsync(
                    request.Server, secret, DetachedRemoteJob.NewJobId(), detachedCommand, step.Timeout, onChunk, Status, cancellationToken);
                var text = request.DataSaver ? TailForDataSaver(detached.Output) : detached.Output;
                result = new SshCommandResult(detached.ExitCode, text, string.Empty, detached.Duration, detached.TimedOut, detached.Cancelled, detached.Unconfirmed);
            }
            else
            {
                result = await ssh.ExecuteAsync(stepRequest, secret, cancellationToken);
            }

            // Si el TRANSPORTE se cortó (exit -1, sin plazo agotado ni cancelación) en un paso que se
            // puede repetir sin efectos, se reintenta: p. ej. "Software caused connection abort" al
            // abrir la conexión justo después de un `up` largo. `up` y las migraciones NO se repiten
            // aquí: si el corte fue a mitad de comando podría seguir ejecutándose en el VPS.
            var retry = 0;
            while (IdempotentStepCodes.Contains(step.Code)
                   && IsTransportFailure(result)
                   && retry < MaxIdempotentStepRetries)
            {
                retry++;
                terminal.Append($"\n[Conexión interrumpida ({result.StandardError.Trim()}). Reintentando {retry} de {MaxIdempotentStepRetries}...]\n");
                ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                    step.Code, step.Label, DeploymentProgressState.OutputSnapshot,
                    Output: LogSanitizer.Sanitize(terminal.GetText()), StepIndex: stepIndex,
                    TotalSteps: commands.Count, IsBlocking: step.IsBlocking));
                await Task.Delay(TimeSpan.FromSeconds(2 * retry), cancellationToken);
                result = await ssh.ExecuteAsync(stepRequest, secret, cancellationToken);
            }

            var output = CombineOutput(result.StandardOutput, result.StandardError);
            if (terminal.GetText().Length == 0) terminal.Append(output);
            else if (!string.IsNullOrWhiteSpace(result.StandardError))
                terminal.Append("\n" + result.StandardError + "\n");
            var sanitized = LogSanitizer.Sanitize(terminal.GetText());
            ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                step.Code, step.Label, DeploymentProgressState.OutputSnapshot,
                Output: sanitized, StepIndex: stepIndex, TotalSteps: commands.Count,
                IsBlocking: step.IsBlocking));
            var stepResult = new DeploymentStepResult(
                step.Code,
                step.Label,
                result.Succeeded,
                result.ExitCode,
                sanitized,
                result.Duration,
                step.IsBlocking);
            steps.Add(stepResult);
            LogStepForDiagnostics(stepResult, request.Server.Name);

            ProgressChanged?.Invoke(new DeploymentProgressUpdate(
                step.Code,
                step.Label,
                DeploymentProgressState.Completed,
                stepResult.Succeeded,
                stepResult.ExitCode,
                stepResult.Output,
                stepResult.Duration,
                stepIndex,
                commands.Count,
                step.IsBlocking));

            if (!result.Succeeded && step.IsBlocking) break;
        }

        var deployedCommit = steps.Any(x => x.Code == "verify_revision" && x.Succeeded)
            ? await TryReadCommitAsync(request, repo, secret, cancellationToken)
            : previousCommit;

        return new ComposeDeploymentResult(
            steps,
            startedAt,
            DateTimeOffset.UtcNow,
            previousCommit,
            deployedCommit);
    }

    /// <summary>
    /// Una línea por paso en la salida de error del proceso (en Android va a logcat). La salida de
    /// cada paso solo vive en la pantalla y en el historial privado de la app; sin esto, un fallo
    /// en el teléfono no deja rastro que se pueda leer por ADB. La salida ya está sanitizada y, en
    /// los pasos correctos, no se incluye: solo resultado, código de salida y duración.
    /// </summary>
    private static void LogStepForDiagnostics(DeploymentStepResult step, string serverName)
    {
        try
        {
            RecordStepActivity(step, serverName);
            var line = $"[VPSDESK] deploy step={step.Code} ok={step.Succeeded} exit={step.ExitCode} t={step.Duration.TotalSeconds:F1}s";
            if (!step.Succeeded)
            {
                var output = step.Output ?? string.Empty;
                var tail = output.Length > 2000 ? output[^2000..] : output;
                line += " output-tail=" + tail.Replace("\r", " ").Replace("\n", " ⏎ ");
            }

            Console.Error.WriteLine(line);
        }
        catch
        {
            // El diagnóstico nunca debe romper un pase.
        }
    }

    private static void RecordStepActivity(DeploymentStepResult step, string serverName)
    {
        if (step.Succeeded)
        {
            ActivityLog.Info("pase", $"{step.Label}: correcto", serverName, step.Duration);
            return;
        }

        // Última línea no vacía de la salida: suele ser la causa. ActivityLog la sanea y la recorta.
        var cause = (step.Output ?? string.Empty).Split('\n')
            .Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? string.Empty;
        var level = step.IsBlocking ? ActivityLevel.Error : ActivityLevel.Warning;
        ActivityLog.Record(level, "pase",
            $"{step.Label}: falló (código {step.ExitCode}){(cause.Length == 0 ? string.Empty : " · " + cause)}", serverName, step.Duration);
    }

    private static readonly HashSet<string> IdempotentStepCodes = new(StringComparer.Ordinal)
    {
        "verify_revision", "compose_ps"
    };

    private const int MaxIdempotentStepRetries = 2;

    private static string TailForDataSaver(string output)
    {
        var max = DataSaverMaxStepOutputBytes;
        if (output.Length <= max) return output;
        return $"[Modo ahorro de datos: se omitieron {output.Length - max} caracteres iniciales de la salida]\n" + output[^max..];
    }

    private static bool IsTransportFailure(SshCommandResult result)
        => result.ConnectionLost && !result.TimedOut && !result.Cancelled;

    /// <summary>Tope de salida que vuelve al dispositivo por paso en modo ahorro de datos.</summary>
    private const int DataSaverMaxStepOutputBytes = 16 * 1024;

    /// <summary>
    /// Ejecuta el comando en un subshell con su salida en un archivo temporal del VPS y devuelve
    /// únicamente los últimos <paramref name="maxBytes"/>. Conserva el código de salida; un
    /// <c>exit</c> dentro del comando solo termina el subshell.
    /// </summary>
    private static string LimitRemoteOutput(string command, int maxBytes)
        => "VPSDESK_LOG=\"$(mktemp)\"; trap 'rm -f \"$VPSDESK_LOG\"' EXIT\n" +
           "(\n" + command + "\n) >\"$VPSDESK_LOG\" 2>&1\n" +
           "VPSDESK_RC=$?\n" +
           "VPSDESK_SIZE=$(wc -c <\"$VPSDESK_LOG\")\n" +
           $"if [ \"$VPSDESK_SIZE\" -gt {maxBytes} ]; then echo \"[Modo ahorro de datos: se omitieron $((VPSDESK_SIZE - {maxBytes})) bytes iniciales de la salida]\"; fi\n" +
           $"tail -c {maxBytes} \"$VPSDESK_LOG\"\n" +
           "exit $VPSDESK_RC";

    /// <summary>
    /// Pull de solo avance rápido. Si falla (p. ej. ramas divergidas) añade a la salida un diagnóstico de
    /// solo lectura: commits que solo existen en el servidor, cuántos le faltan y cambios sin confirmar.
    /// Conserva el código de salida del pull. Los argumentos llegan ya entrecomillados para el shell.
    /// </summary>
    internal static string BuildFastForwardCommand(string repo, string branch, string remoteBranch)
        => $"git -C {repo} pull --ff-only origin {branch} || {{ rc=$?; echo; echo '--- Diagnostico (solo lectura) ---'; "
           + $"echo 'Commits solo en el servidor (no estan en origin):'; git -C {repo} log --oneline --no-decorate -n 20 {remoteBranch}..{branch} 2>&1; "
           + $"echo 'Commits de origin que faltan en el servidor:'; git -C {repo} rev-list --count {branch}..{remoteBranch} 2>&1; "
           + $"echo 'Cambios locales sin confirmar:'; git -C {repo} status --short 2>&1 | head -n 20; exit $rc; }}";

    private static string BuildServiceArguments(IReadOnlyList<string>? services)
    {
        if (services is null || services.Count == 0) return string.Empty;

        var normalized = services
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Select(service => service.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var service in normalized)
        {
            if (!SafeServiceName.IsMatch(service))
            {
                throw new InvalidOperationException($"Invalid Docker Compose service name: '{service}'.");
            }
        }

        return normalized.Length == 0
            ? string.Empty
            : " " + string.Join(" ", normalized.Select(DeploymentPreflightService.ShellQuote));
    }

    private static string RenderOperationCommand(
        DeploymentProjectOperationRequest operation,
        ComposeDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(operation.Code) || string.IsNullOrWhiteSpace(operation.Label))
            throw new InvalidOperationException("Project operation code and label are required.");
        if (string.IsNullOrWhiteSpace(operation.CommandTemplate))
            throw new InvalidOperationException($"Project operation '{operation.Label}' has no command template.");

        var command = operation.CommandTemplate
            .Replace("{{compose}}", DeploymentPreflightService.ShellQuote(request.ComposeFile.Trim()), StringComparison.Ordinal)
            .Replace("{{repository}}", DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/')), StringComparison.Ordinal)
            .Replace("{{branch}}", DeploymentPreflightService.ShellQuote(request.Branch.Trim()), StringComparison.Ordinal)
            .Replace("{{input}}", DeploymentPreflightService.ShellQuote(operation.Input ?? string.Empty), StringComparison.Ordinal);

        var unresolved = UnresolvedToken.Match(command);
        if (unresolved.Success)
        {
            throw new InvalidOperationException(
                $"Project operation '{operation.Label}' contains unsupported template token {unresolved.Value}.");
        }

        return $"cd -- {DeploymentPreflightService.ShellQuote(request.RemoteRepositoryPath.Trim().TrimEnd('/'))} && {command}";
    }

    private async Task<string?> TryReadCommitAsync(
        ComposeDeploymentRequest request,
        string quotedRepository,
        string? secret,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ssh.ExecuteAsync(
                new SshCommandRequest(
                    request.Server,
                    $"git -C {quotedRepository} rev-parse HEAD",
                    TimeSpan.FromSeconds(15)),
                secret,
                cancellationToken);

            if (!result.Succeeded) return null;
            var value = LogSanitizer.Sanitize(result.StandardOutput).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return stdout.TrimEnd();
        if (string.IsNullOrWhiteSpace(stdout)) return stderr.TrimEnd();
        return stdout.TrimEnd() + Environment.NewLine + stderr.TrimEnd();
    }
}
