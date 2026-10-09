using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VpsDesk.Application.Snapshots;

namespace VpsDesk.Infrastructure.Snapshots;

/// <summary>
/// Snapshots del VPS mediante la API de Hostinger
/// (https://developers.hostinger.com, rutas /api/vps/v1/...). Hostinger guarda
/// UN solo snapshot por VPS: crear uno nuevo sobrescribe el anterior.
/// El token viaja solo en la cabecera Authorization y nunca se registra.
/// </summary>
public sealed class HostingerVpsSnapshotService : IVpsSnapshotService, IDisposable
{
    private static readonly Uri BaseAddress = new("https://developers.hostinger.com/");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(90);

    private readonly HttpClient _http;
    private readonly string? _configuredVirtualMachineId;
    private string? _resolvedVirtualMachineId;

    public HostingerVpsSnapshotService(string? apiToken, string? virtualMachineId = null, HttpMessageHandler? handler = null)
    {
        IsConfigured = !string.IsNullOrWhiteSpace(apiToken);
        _configuredVirtualMachineId = string.IsNullOrWhiteSpace(virtualMachineId) ? null : virtualMachineId.Trim();

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = BaseAddress;
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (IsConfigured)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken!.Trim());
        }
    }

    public bool IsConfigured { get; }

    public async Task<VpsSnapshotInfo?> GetAsync(string serverHost, CancellationToken cancellationToken = default)
    {
        var vm = await ResolveVirtualMachineIdAsync(serverHost, cancellationToken);
        using var response = await _http.GetAsync($"api/vps/v1/virtual-machines/{vm}/snapshot", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        var root = await ReadJsonAsync(response, cancellationToken);
        // Un VPS sin snapshot puede responder 200 con un cuerpo sin "id".
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return new VpsSnapshotInfo(
            id.GetInt64(),
            ReadDate(root, "created_at") ?? DateTimeOffset.MinValue,
            ReadDate(root, "expires_at"),
            root.TryGetProperty("restore_time", out var rt) && rt.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds(rt.GetInt64())
                : null);
    }

    public async Task CreateAsync(string serverHost, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var vm = await ResolveVirtualMachineIdAsync(serverHost, cancellationToken);
        progress?.Report("Solicitando el snapshot a Hostinger (sobrescribe el anterior)...");
        var actionId = await PostActionAsync($"api/vps/v1/virtual-machines/{vm}/snapshot", cancellationToken);
        await WaitForActionAsync(vm, actionId, CreateTimeout, "La creación del snapshot", progress, cancellationToken);
    }

    public async Task RestoreAsync(string serverHost, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var vm = await ResolveVirtualMachineIdAsync(serverHost, cancellationToken);
        progress?.Report("Solicitando la restauración a Hostinger...");
        var actionId = await PostActionAsync($"api/vps/v1/virtual-machines/{vm}/snapshot/restore", cancellationToken);
        await WaitForActionAsync(vm, actionId, RestoreTimeout, "La restauración del snapshot", progress, cancellationToken);
    }

    private async Task<string> ResolveVirtualMachineIdAsync(string serverHost, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (_configuredVirtualMachineId is not null) return _configuredVirtualMachineId;
        if (_resolvedVirtualMachineId is not null) return _resolvedVirtualMachineId;

        using var response = await _http.GetAsync("api/vps/v1/virtual-machines", cancellationToken);
        var root = await ReadJsonAsync(response, cancellationToken);
        var machines = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var data) ? data : default;

        if (machines.ValueKind == JsonValueKind.Array)
        {
            foreach (var machine in machines.EnumerateArray())
            {
                if (!machine.TryGetProperty("id", out var id)) continue;

                var matchesHostname = machine.TryGetProperty("hostname", out var hostname)
                    && string.Equals(hostname.GetString(), serverHost, StringComparison.OrdinalIgnoreCase);
                var matchesIp = machine.TryGetProperty("ipv4", out var ips)
                    && ips.ValueKind == JsonValueKind.Array
                    && ips.EnumerateArray().Any(ip => ip.TryGetProperty("address", out var a)
                        && string.Equals(a.GetString()?.Trim(), serverHost.Trim(), StringComparison.OrdinalIgnoreCase));

                if (matchesHostname || matchesIp)
                {
                    return _resolvedVirtualMachineId = id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString()!;
                }
            }
        }

        throw new InvalidOperationException(
            $"Ningún VPS de la cuenta de Hostinger coincide con '{serverHost}'. Define HOSTINGER_VM_ID en el .env.");
    }

    private async Task<long> PostActionAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(path, content: null, cancellationToken);
        var root = await ReadJsonAsync(response, cancellationToken);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            return id.GetInt64();
        }

        throw new InvalidOperationException("Hostinger aceptó la solicitud pero no devolvió el identificador de la acción.");
    }

    private async Task WaitForActionAsync(
        string vm,
        long actionId,
        TimeSpan timeout,
        string label,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        string? lastState = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var response = await _http.GetAsync($"api/vps/v1/virtual-machines/{vm}/actions/{actionId}", cancellationToken);
            var root = await ReadJsonAsync(response, cancellationToken);
            var state = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("state", out var s) ? s.GetString() : null;

            if (state != lastState)
            {
                lastState = state;
                progress?.Report($"Estado en Hostinger: {state ?? "desconocido"} ({elapsed.Elapsed:mm\\:ss})");
            }

            if (string.Equals(state, "success", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(state, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{label} falló en Hostinger (acción {actionId}).");
            }

            if (elapsed.Elapsed > timeout)
            {
                throw new TimeoutException(
                    $"{label} no terminó en {timeout.TotalMinutes:F0} min (acción {actionId}, último estado: {state ?? "desconocido"}). Revísalo en hPanel.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "token inválido o vencido",
                HttpStatusCode.Forbidden => "el token no tiene permisos sobre este VPS",
                HttpStatusCode.TooManyRequests => "límite de solicitudes de la API excedido, reintenta en un minuto",
                _ => Truncate(body, 200)
            };
            throw new InvalidOperationException($"Hostinger respondió {(int)response.StatusCode}: {detail}");
        }

        if (string.IsNullOrWhiteSpace(body)) return default;

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Falta HOSTINGER_API_TOKEN en el .env: los snapshots no están configurados.");
        }
    }

    public void Dispose() => _http.Dispose();
}
