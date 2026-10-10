namespace VpsDesk.Infrastructure.Ssh;

/// <summary>Huella que presento el servidor frente a la que tenia guardada el perfil.</summary>
internal sealed record HostKeyMismatch(string Algorithm, string Presented, string Expected)
{
    // Las huellas son informacion publica del servidor: mostrarlas no expone ningun secreto.
    public string Describe()
        => $"La clave del servidor no coincide con la huella guardada. "
           + $"El servidor presento SHA256:{Presented} ({Algorithm}); la huella guardada es SHA256:{Expected}. "
           + "Si la huella guardada es antigua, actualizala en el perfil del servidor.";
}

/// <summary>Resultado de aplicar la politica de huella a un cliente SSH.</summary>
internal sealed class HostKeyPolicy
{
    public HostKeyMismatch? Mismatch { get; set; }
}
