using System.Globalization;
using System.Text.Json;

namespace VpsDesk.Desktop.Localization;

public sealed class LocalizationService
{
    private const string FallbackCulture = "en-US";
    private static readonly string[] Supported = ["en-US", "es-ES"];
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public static LocalizationService Current { get; } = new();

    public event EventHandler? CultureChanged;

    public string CurrentCulture { get; private set; } = FallbackCulture;
    public IReadOnlyList<string> SupportedCultures => Supported;

    public void Initialize(Avalonia.Application application)
    {
        var requested = ResolveRequestedCulture();
        ApplyCulture(application, requested);
    }

    public void ApplyCulture(Avalonia.Application application, string? requestedCulture)
    {
        var culture = NormalizeCulture(requestedCulture);
        var fallback = LoadPack(FallbackCulture);
        var selected = culture.Equals(FallbackCulture, StringComparison.OrdinalIgnoreCase)
            ? fallback
            : LoadPack(culture);

        _strings.Clear();
        foreach (var pair in fallback)
        {
            _strings[pair.Key] = pair.Value;
        }
        foreach (var pair in selected)
        {
            _strings[pair.Key] = pair.Value;
        }

        foreach (var pair in _strings)
        {
            application.Resources[pair.Key] = pair.Value;
        }

        CurrentCulture = culture;
        var uiCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
        => _strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : key;

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string T(string key) => Current.Get(key);

    private static string ResolveRequestedCulture()
    {
        var overrideCulture = Environment.GetEnvironmentVariable("VPSDESK_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(overrideCulture))
        {
            return overrideCulture;
        }

        return CultureInfo.CurrentUICulture.Name;
    }

    private static string NormalizeCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return FallbackCulture;
        var normalized = culture.Trim();
        if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es-ES";
        if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return FallbackCulture;
    }

    private static Dictionary<string, string> LoadPack(string culture)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Localization",
            "Languages",
            culture,
            "Strings.json");

        if (!File.Exists(path))
        {
            if (culture.Equals(FallbackCulture, StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException($"Required localization pack was not found: {path}", path);
            }
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = File.OpenRead(path);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        return values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(values, StringComparer.Ordinal);
    }
}
