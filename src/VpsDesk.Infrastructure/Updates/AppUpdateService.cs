using System.Security.Cryptography;
using System.Text.Json;
using VpsDesk.Application.Updates;

namespace VpsDesk.Infrastructure.Updates;

/// <summary>Consulta version.json y descarga el APK verificando su SHA-256.</summary>
public sealed class AppUpdateService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public AppUpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("VpsDesk-Updater");
        }
    }

    /// <summary>Devuelve el manifiesto publicado, o null si no hay nada más nuevo que <paramref name="currentVersionCode"/>.</summary>
    public async Task<AppUpdateManifest?> CheckAsync(string manifestUrl, int currentVersionCode, CancellationToken ct = default)
    {
        var manifestUri = ParseHttpsUri(manifestUrl);
        // GitHub Releases conserva assets reemplazados en su CDN; una URL única evita
        // recibir el version.json anterior durante la propagación.
        var manifestRequestUri = WithCacheBuster(manifestUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestRequestUri);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("version.json vacío.");
        if (string.IsNullOrWhiteSpace(manifest.ApkUrl))
        {
            throw new InvalidDataException("version.json no indica la ruta del APK.");
        }

        // La ruta del APK puede ser relativa al propio version.json.
        var apkUri = new Uri(manifestUri, manifest.ApkUrl);
        manifest = manifest with { ApkUrl = ParseHttpsUri(apkUri.ToString()).ToString() };

        return manifest.VersionCode > currentVersionCode ? manifest : null;
    }

    public async Task<string> DownloadAsync(
        AppUpdateManifest manifest,
        string directory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"vpsdesk-{manifest.VersionCode}.apk");
        var partial = target + ".part";

        using var response = await _http.GetAsync(manifest.ApkUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total is > 0) progress?.Report((double)done / total.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            await using var check = File.OpenRead(partial);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
            if (!hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new InvalidDataException("El APK descargado no coincide con el SHA-256 publicado. No se instalará.");
            }
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// APK ya descargado de esa versión y con el SHA-256 correcto, o null. Permite ofrecer «Instalar» sin volver
    /// a descargar 50 MB si la descarga anterior se completó pero no se confirmó la instalación.
    /// </summary>
    public static async Task<string?> FindDownloadedAsync(AppUpdateManifest manifest, string directory, CancellationToken ct = default)
    {
        try
        {
            var target = Path.Combine(directory, $"vpsdesk-{manifest.VersionCode}.apk");
            if (!File.Exists(target)) return null;
            if (string.IsNullOrWhiteSpace(manifest.Sha256)) return target;
            await using var file = File.OpenRead(target);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
            return hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) ? target : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Borra las descargas parciales y los APK de la versión instalada o anteriores (ya no sirven). Conserva los de
    /// una versión más nueva: son una actualización descargada que aún no se instaló.
    /// </summary>
    public static void CleanDirectory(string directory, int installedVersionCode = int.MaxValue)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "vpsdesk-*.apk*"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(file), @"^vpsdesk-(\d+)\.apk$");
                if (match.Success && int.Parse(match.Groups[1].Value) > installedVersionCode) continue;
                File.Delete(file);
            }
        }
        catch
        {
            // Limpieza de cortesía: si falla no afecta a la app.
        }
    }

    private static Uri ParseHttpsUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("La URL de actualización debe ser https.");
        }
        return uri;
    }

    private static Uri WithCacheBuster(Uri uri)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri(uri + separator + "vpsdesk-cache=" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() => _http.Dispose();
}
