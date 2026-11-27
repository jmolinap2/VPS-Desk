using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Java.Lang;
using VpsDesk.Application.Logging;

namespace VpsDesk.Android;

/// <summary>
/// Burbuja flotante (Android 11+) con el pase en curso: el estado y un botón para abortarlo, sin abrir la app.
/// Es una notificación de conversación con metadatos de burbuja; Android solo la muestra flotando si el usuario
/// permite las burbujas de VPS Desk (Ajustes de Android → Apps → VPS Desk → Notificaciones → Burbujas). Si no,
/// o en versiones anteriores, no pasa nada: el pase sigue igual y la notificación normal del servicio se mantiene.
/// </summary>
internal static class DeploymentBubble
{
    private const string ChannelId = "deployment_bubble";
    private const string ShortcutId = "deployment_bubble_shortcut";
    private const int NotificationId = 1403;
    private const int BubbleRequestCode = 1404;

    private static readonly object Gate = new();

    /// <summary>Último estado mostrado; la actividad de la burbuja lo lee al abrirse.</summary>
    public static string Message { get; private set; } = string.Empty;

    /// <summary>true cuando el pase terminó (con éxito o no): la burbuja deja de ofrecer «Abortar».</summary>
    public static bool Finished { get; private set; }

    /// <summary>Cambió el estado: (mensaje, terminado). Puede llegar desde cualquier hilo.</summary>
    public static event Action<string, bool>? Changed;

    public static void Start(Context context, string message) => Post(context, message, finished: false);

    public static void Update(Context context, string message) => Post(context, message, finished: false);

    public static void Finish(Context context, string title, string message) => Post(context, $"{title}: {message}", finished: true);

    private static void Post(Context context, string message, bool finished)
    {
        lock (Gate)
        {
            Message = message;
            Finished = finished;
        }

        try { Changed?.Invoke(message, finished); } catch (System.Exception) { /* un suscriptor no debe afectar al pase */ }

        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            LogOnce("burbuja no disponible: requiere Android 11 o superior");
            return;
        }

        try
        {
            Notify(context, message, finished);
            LogOnce("burbuja publicada; si no flota, actívala en Ajustes → Apps → VPS Desk → Notificaciones → Pase en burbuja", ActivityLevel.Info);
        }
        catch (System.Exception ex)
        {
            // Una burbuja es un extra: nunca debe interrumpir un pase. Pero el motivo queda en el registro de actividad.
            LogOnce($"burbuja no publicada: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? _lastLogged;

    // Una línea por motivo distinto: el estado del pase cambia muchas veces y no debe llenar el registro.
    private static void LogOnce(string message, ActivityLevel level = ActivityLevel.Warning)
    {
        if (message == _lastLogged) return;
        _lastLogged = message;
        ActivityLog.Record(level, "burbuja", message);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private static void Notify(Context context, string message, bool finished)
    {
        var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        var channel = new NotificationChannel(ChannelId, "Pase en burbuja", NotificationImportance.Default)
        {
            Description = "Estado del pase en una burbuja flotante con botón para abortarlo"
        };
        channel.SetAllowBubbles(true);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        notifications.CreateNotificationChannel(channel);

        var person = new Person.Builder().SetName("VPS Desk")!.SetImportant(true)!.Build()!;
        var bubbleActivity = new Intent(context, typeof(BubbleActivity)).SetAction(Intent.ActionView)!;

        // Android exige un atajo de larga duración asociado a la conversación para permitir la burbuja.
        var shortcut = new ShortcutInfo.Builder(context, ShortcutId)
            .SetLongLived(true)!
            .SetShortLabel("Pase en curso")!
            .SetIntent(bubbleActivity)!
            .SetPerson(person)!
            .Build()!;
        ((ShortcutManager)context.GetSystemService(Context.ShortcutService)!).PushDynamicShortcut(shortcut);

        var bubbleIntent = PendingIntent.GetActivity(context, BubbleRequestCode, bubbleActivity,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
        var icon = Icon.CreateWithResource(context, context.ApplicationInfo!.Icon)!;
        var bubble = new Notification.BubbleMetadata.Builder(bubbleIntent, icon)
            .SetDesiredHeight(420)!
            .SetAutoExpandBubble(false)!
            .SetSuppressNotification(false)!
            .Build()!;

        var style = new Notification.MessagingStyle(person);
        style.SetConversationTitle(finished ? "Pase terminado" : "Pase en curso");
        style.AddMessage(message, JavaSystem.CurrentTimeMillis(), person);

        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)!
            .SetStyle(style)!
            .SetShortcutId(ShortcutId)!
            .SetBubbleMetadata(bubble)!
            .SetCategory(Notification.CategoryMessage)!
            .SetOnlyAlertOnce(true)!
            .SetAutoCancel(finished)!
            .Build()!;
        notifications.Notify(NotificationId, notification);
    }
}
