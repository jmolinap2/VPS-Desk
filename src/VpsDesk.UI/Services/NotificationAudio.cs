namespace VpsDesk.Desktop.Services;

/// <summary>La plataforma reproduce el sonido; un fallo de audio nunca interrumpe una operación.</summary>
public static class NotificationAudio
{
    public static Action<NotificationLevel>? PlaySound { get; set; }

    public static void Play(NotificationLevel level)
    {
        if (!NotificationCenter.Shared.SoundEnabled) return;
        try { PlaySound?.Invoke(level); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Notification audio: {ex.Message}"); }
    }
}
