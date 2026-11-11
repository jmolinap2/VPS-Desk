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
        // Evita que un proxy o la caché del navegador sirva un version.json viejo.
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
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

    /// <summary>Borra APK y descargas parciales anteriores (p. ej. tras instalar la actualización).</summary>
    public static void CleanDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "vpsdesk-*.apk*"))
            {
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

    public void Dispose() => _http.Dispose();
}
