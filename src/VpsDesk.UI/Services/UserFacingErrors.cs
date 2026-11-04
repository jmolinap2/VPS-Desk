using System.Net.Sockets;
using System.Reflection;

namespace VpsDesk.Desktop.Services;

/// <summary>
/// Traduce excepciones técnicas a un texto que el usuario pueda entender y accionar.
/// Los tipos de SSH.NET se reconocen por nombre para que la capa de UI no dependa de
/// esa librería.
/// </summary>
public static class UserFacingErrors
{
    public static string Describe(Exception exception)
    {
        var e = Unwrap(exception);

        switch (e)
        {
            case MissingMemberException or TypeLoadException or FileLoadException or BadImageFormatException:
                return "VPS Desk tiene componentes de versiones distintas (instalación incompleta o actualizada a medias). "
                       + "Cierra la aplicación y reinstálala o vuelve a publicarla. "
                       + $"Detalle técnico: {e.Message}";

            case FileNotFoundException { FileName: { Length: > 0 } file }:
                return $"No se encontró el archivo «{file}». Revisa la ruta en el perfil del servidor.";

            case DirectoryNotFoundException:
                return $"No se encontró la carpeta indicada. {e.Message}";

            case UnauthorizedAccessException:
                return $"Windows o Android negó el acceso a un archivo o carpeta. {e.Message}";

            case SocketException:
                return "No se pudo alcanzar el servidor. Revisa el host, el puerto, el firewall y tu conexión de red.";

            case TimeoutException:
                return "La operación tardó demasiado. Comprueba que el servidor responde y vuelve a intentarlo.";

            case OperationCanceledException:
                return "La operación se canceló.";
        }

        return e.GetType().Name switch
        {
            "SshAuthenticationException"
                => "El servidor rechazó la autenticación SSH. Revisa el usuario, la clave privada y la passphrase.",
            "SshOperationTimeoutException" or "SshConnectionException" when LooksLikeTimeout(e)
                => "El servidor no respondió a tiempo. Comprueba la red y que el VPS esté encendido.",
            "SshPassPhraseNullOrEmptyException"
                => "La clave privada está protegida con passphrase: escríbela en el campo correspondiente.",
            "SshException" when e.Message.Contains("Invalid private key", StringComparison.OrdinalIgnoreCase)
                => "La clave privada no tiene un formato válido o la passphrase es incorrecta.",
            _ => string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message
        };
    }

    private static bool LooksLikeTimeout(Exception e)
        => e.Message.Contains("time", StringComparison.OrdinalIgnoreCase);

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case AggregateException { InnerExceptions.Count: 1 } aggregate:
                    current = aggregate.InnerExceptions[0];
                    continue;
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
                default:
                    return current;
            }
        }
    }
}
