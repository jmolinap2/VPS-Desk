using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;

namespace VpsDesk.Android;

/// <summary>Keeps the process and CPU available while the user-initiated SSH deployment runs.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DeploymentForegroundService : Service
{
    private const string ChannelId = "deployment_progress";
    private const int NotificationId = 1401;
    private static DeploymentForegroundService? _instance;
    private PowerManager.WakeLock? _wakeLock;
    private string _message = "Preparando el pase";
    private double _percent;

    /// <summary>true mientras el servicio, y por tanto el pase, está corriendo.</summary>
    internal static bool IsRunning => _instance is not null;

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Pases en curso", NotificationImportance.Low)
        {
            Description = "Progreso del pase mientras VPS Desk está en segundo plano"
        });
        StartForeground(NotificationId, BuildNotification());
        DeploymentBubble.Start(this, _message);
        DeploymentOverlay.OnServiceStarted();
        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "VpsDesk:Deployment");
        _wakeLock?.Acquire(6 * 60 * 60 * 1000L);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        => StartCommandResult.NotSticky;

    public override IBinder? OnBind(Intent? intent) => null;

    public static void Report(string message)
    {
        var service = _instance;
        if (service is null || string.IsNullOrWhiteSpace(message)) return;
        try
        {
            service._message = message;
            ((NotificationManager)service.GetSystemService(NotificationService)!).Notify(NotificationId, service.BuildNotification());
            DeploymentBubble.Update(service, message);
        }
        catch (Exception) { /* A notification must never interrupt a deployment. */ }
    }

    public static void ReportProgress(double percent, string step)
    {
        var service = _instance;
        if (service is null) return;
        try
        {
            service._percent = percent;
            DeploymentBubble.SetProgress(percent, step);
            ((NotificationManager)service.GetSystemService(NotificationService)!).Notify(NotificationId, service.BuildNotification());
        }
        catch (Exception) { /* A notification must never interrupt a deployment. */ }
    }

    public static void ReportOutputTail(string tail)
    {
        if (_instance is null) return;
        try { DeploymentBubble.SetTail(tail); }
        catch (Exception) { /* A notification must never interrupt a deployment. */ }
    }

    public static void Complete(string title, string message)
    {
        var service = _instance;
        if (service is null) return;
        try
        {
            var launch = service.PackageManager?.GetLaunchIntentForPackage(service.PackageName!);
            var pending = launch is null ? null : PendingIntent.GetActivity(service, 0, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            var builder = new Notification.Builder(service, ChannelId)
                .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
                .SetContentTitle(title).SetContentText(message).SetAutoCancel(true);
            if (pending is not null) builder.SetContentIntent(pending);
            ((NotificationManager)service.GetSystemService(NotificationService)!).Notify(NotificationId + 1, builder.Build());
            DeploymentBubble.Finish(service, title, message);
        }
        catch (Exception) { /* A notification must never interrupt a deployment. */ }
    }

    private Notification BuildNotification()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var pending = launch is null ? null : PendingIntent.GetActivity(this, 0, launch,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var builder = new Notification.Builder(this, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
            .SetContentTitle("VPS Desk · Pase en curso")
            .SetContentText(_message)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetProgress(100, (int)_percent, _percent <= 0);
        if (pending is not null) builder.SetContentIntent(pending);
        return builder.Build();
    }

    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        StopSelf();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
        DeploymentOverlay.OnServiceStopped();
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        _wakeLock = null;
        base.OnDestroy();
    }
}
