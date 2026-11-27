using System.Text.Json;
using VpsDesk.Application.Runtime;

namespace VpsDesk.Application.Logging;

public enum ActivityLevel
{
    Info,
    Warning,
    Error
}

/// <summary>Un hecho ocurrido en la aplicación (conexión, paso de un pase, acción del usuario, error).</summary>
public sealed record ActivityLogEntry(
    DateTimeOffset At,
    ActivityLevel Level,
    string Source,
    string Message,
    string? Server = null,
    TimeSpan? Duration = null);

/// <summary>
/// Registro de actividad de toda la aplicación: qué pasó y cuándo, para poder explicar un fallo después.
/// Guarda los últimos días en un archivo local (JSON por línea, con rotación) y un tope de eventos en
/// memoria. Los textos se sanean antes de guardarse y nunca se registra el texto de un comando ni su
/// salida completa. Registrar nunca lanza una excepción: el diagnóstico no debe romper la operación.
/// </summary>
public static class ActivityLog
{
    public const int MemoryLimit = 2000;
    public const int MaxMessageLength = 400;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private const long MaxFileBytes = 1_000_000;
    private const string FileName = "activity.jsonl";
    private const string PreviousFileName = "activity.1.jsonl";

    private static readonly object Gate = new();
    private static readonly List<ActivityLogEntry> Entries = [];
    private static readonly List<ActivityLogEntry> NotPersisted = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static string? _directory;

    /// <summary>Se dispara tras registrar o borrar; puede llegar desde cualquier hilo.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Activa el guardado en disco y recupera lo guardado antes. Los eventos registrados antes de llamarlo
    /// se conservan y se escriben ahora.
    /// </summary>
    public static void Initialize(string? directory = null)
    {
        try
        {
            lock (Gate)
            {
                _directory = directory ?? VpsDeskDataPaths.LocalRoot;
                Directory.CreateDirectory(_directory);
                var cutoff = DateTimeOffset.UtcNow - Retention;
                var loaded = new List<ActivityLogEntry>();
                foreach (var name in new[] { PreviousFileName, FileName })
                    loaded.AddRange(ReadFile(Path.Combine(_directory, name)).Where(entry => entry.At >= cutoff));

                var pending = NotPersisted.ToList();
                NotPersisted.Clear();
                Entries.Clear();
                Entries.AddRange(loaded);
                foreach (var entry in pending)
                {
                    Entries.Add(entry);
                    Append(entry);
                }

                Trim();
            }
        }
        catch
        {
            // Sin disco sigue funcionando en memoria.
        }

        RaiseChanged();
    }

    public static void Record(ActivityLevel level, string source, string message, string? server = null, TimeSpan? duration = null)
    {
        try
        {
            var entry = new ActivityLogEntry(
                DateTimeOffset.UtcNow, level, Clean(source, 40), Clean(message, MaxMessageLength),
                string.IsNullOrWhiteSpace(server) ? null : Clean(server, 80), duration);
            lock (Gate)
            {
                Entries.Add(entry);
                Trim();
                if (_directory is null) NotPersisted.Add(entry);
                else Append(entry);
            }
        }
        catch
        {
            // El diagnóstico nunca debe romper la operación.
            return;
        }

        RaiseChanged();
    }

    public static void Info(string source, string message, string? server = null, TimeSpan? duration = null)
        => Record(ActivityLevel.Info, source, message, server, duration);

    public static void Warning(string source, string message, string? server = null, TimeSpan? duration = null)
        => Record(ActivityLevel.Warning, source, message, server, duration);

    public static void Error(string source, string message, string? server = null, TimeSpan? duration = null)
        => Record(ActivityLevel.Error, source, message, server, duration);

    /// <summary>Eventos del más antiguo al más reciente. Con <paramref name="onlyProblems"/> solo avisos y errores.</summary>
    public static IReadOnlyList<ActivityLogEntry> Snapshot(bool onlyProblems = false)
    {
        lock (Gate)
            return Entries.Where(entry => !onlyProblems || entry.Level != ActivityLevel.Info).ToList();
    }

    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                Entries.Clear();
                NotPersisted.Clear();
                if (_directory is not null)
                    foreach (var name in new[] { FileName, PreviousFileName })
                    {
                        var path = Path.Combine(_directory, name);
                        if (File.Exists(path)) File.Delete(path);
                    }
            }
        }
        catch
        {
            // Si no se puede borrar el archivo, al menos queda vacío en pantalla.
        }

        RaiseChanged();
    }

    /// <summary>Línea legible de un evento, la que se muestra y se exporta.</summary>
    public static string Format(ActivityLogEntry entry)
    {
        var level = entry.Level switch { ActivityLevel.Error => "ERR ", ActivityLevel.Warning => "WARN", _ => "INFO" };
        var server = entry.Server is null ? string.Empty : $" · {entry.Server}";
        var duration = entry.Duration is { } d ? $" ({d.TotalSeconds:F1} s)" : string.Empty;
        return $"{entry.At.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{level}] {entry.Source}{server} · {entry.Message}{duration}";
    }

    internal static string Clean(string text, int maxLength)
    {
        var clean = LogSanitizer.Sanitize((text ?? string.Empty).Replace("\r", " ").Replace("\n", " ")).Trim();
        return clean.Length > maxLength ? clean[..maxLength] + "…" : clean;
    }

    private static void Trim()
    {
        if (Entries.Count > MemoryLimit) Entries.RemoveRange(0, Entries.Count - MemoryLimit);
    }

    private static void Append(ActivityLogEntry entry)
    {
        var path = Path.Combine(_directory!, FileName);
        if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
        {
            var previous = Path.Combine(_directory!, PreviousFileName);
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(path, previous);
        }

        File.AppendAllText(path, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
    }

    private static IEnumerable<ActivityLogEntry> ReadFile(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path))
        {
            ActivityLogEntry? entry = null;
            try { entry = JsonSerializer.Deserialize<ActivityLogEntry>(line, Json); }
            catch (JsonException) { /* una línea dañada no invalida el resto */ }
            if (entry is not null) yield return entry;
        }
    }

    private static void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* un suscriptor con error no debe afectar a quien registra */ }
    }
}
