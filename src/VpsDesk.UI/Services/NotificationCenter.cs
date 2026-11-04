using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VpsDesk.Desktop.Services;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Un aviso visible (toast). Las propiedades Is* alimentan los estilos por clase de la vista.</summary>
public sealed partial class AppNotification : ObservableObject
{
    private IDisposable? _timer;

    public AppNotification(NotificationLevel level, string title, string message)
    {
        Level = level;
        Title = title;
        Message = message;
    }

    public NotificationLevel Level { get; }
    public string Title { get; }
    public string Message { get; }

    /// <summary>Veces que se repitió el mismo aviso mientras seguía visible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepeats), nameof(RepeatLabel))]
    private int _repeatCount = 1;

    public bool HasRepeats => RepeatCount > 1;
    public string RepeatLabel => $"×{RepeatCount}";

    public bool IsInfo => Level == NotificationLevel.Info;
    public bool IsSuccess => Level == NotificationLevel.Success;
    public bool IsWarning => Level == NotificationLevel.Warning;
    public bool IsError => Level == NotificationLevel.Error;

    public string Glyph => Level switch
    {
        NotificationLevel.Success => "✓",
        NotificationLevel.Warning => "!",
        NotificationLevel.Error => "✕",
        _ => "i"
    };

    internal void ArmTimer(TimeSpan duration, Action expire)
    {
        _timer?.Dispose();
        _timer = DispatcherTimer.RunOnce(expire, duration);
    }

    internal void DisarmTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

/// <summary>
/// Canal único de avisos para escritorio y móvil. Los textos de estado de cada pantalla
/// siguen existiendo (son el registro persistente), pero un resultado importante (error,
/// éxito de una operación remota) además se anuncia aquí para que no pase desapercibido.
/// </summary>
public sealed partial class NotificationCenter : ObservableObject
{
    private const int MaxVisible = 4;

    public static NotificationCenter Shared { get; } = new();

    public ObservableCollection<AppNotification> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    private NotificationCenter()
    {
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));
    }

    public void Publish(NotificationLevel level, string message, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Publish(level, message, title));
            return;
        }

        var text = Truncate(message.Trim(), 600);
        var heading = title ?? DefaultTitle(level);

        // Mismo aviso ya visible: se cuenta y se reinicia su temporizador, en lugar de apilar copias.
        var existing = Items.FirstOrDefault(x => x.Level == level && x.Title == heading && x.Message == text);
        if (existing is not null)
        {
            existing.RepeatCount++;
            Items.Remove(existing);
            Items.Insert(0, existing);
            existing.ArmTimer(DurationFor(level), () => Dismiss(existing));
            return;
        }

        var notification = new AppNotification(level, heading, text);
        Items.Insert(0, notification);
        notification.ArmTimer(DurationFor(level), () => Dismiss(notification));

        while (Items.Count > MaxVisible)
        {
            var oldest = Items[^1];
            oldest.DisarmTimer();
            Items.RemoveAt(Items.Count - 1);
        }
    }

    public void Dismiss(AppNotification notification)
    {
        notification.DisarmTimer();
        Items.Remove(notification);
    }

    [RelayCommand]
    private void DismissNotification(AppNotification? notification)
    {
        if (notification is not null) Dismiss(notification);
    }

    [RelayCommand]
    private void DismissAll()
    {
        foreach (var item in Items.ToArray()) Dismiss(item);
    }

    // Los errores duran más: hay que poder leerlos y copiar el detalle antes de que desaparezcan.
    private static TimeSpan DurationFor(NotificationLevel level) => level switch
    {
        NotificationLevel.Error => TimeSpan.FromSeconds(14),
        NotificationLevel.Warning => TimeSpan.FromSeconds(9),
        NotificationLevel.Info => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(4)
    };

    private static string DefaultTitle(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "Listo",
        NotificationLevel.Warning => "Atención",
        NotificationLevel.Error => "Algo salió mal",
        _ => "Información"
    };

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}

/// <summary>Atajo estático para publicar avisos desde cualquier ViewModel.</summary>
public static class Toast
{
    public static void Info(string message, string? title = null)
        => NotificationCenter.Shared.Publish(NotificationLevel.Info, message, title);

    public static void Success(string message, string? title = null)
        => NotificationCenter.Shared.Publish(NotificationLevel.Success, message, title);

    public static void Warning(string message, string? title = null)
        => NotificationCenter.Shared.Publish(NotificationLevel.Warning, message, title);

    public static void Error(string message, string? title = null)
        => NotificationCenter.Shared.Publish(NotificationLevel.Error, message, title);

    /// <summary>Error a partir de una excepción, con un texto entendible en lugar del mensaje crudo.</summary>
    public static void Error(Exception exception, string context)
        => NotificationCenter.Shared.Publish(NotificationLevel.Error, UserFacingErrors.Describe(exception), context);
}
