using Renci.SshNet;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Infrastructure.Ssh;

internal static class SshConnectionFactory
{
    public static ConnectionInfo Create(ServerProfile server, string? secret, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(server.Host))
        {
            throw new InvalidOperationException("Server host is required.");
        }

        if (string.IsNullOrWhiteSpace(server.Username))
        {
            throw new InvalidOperationException("SSH username is required.");
        }

        AuthenticationMethod auth = server.AuthenticationType switch
        {
            SshAuthenticationType.PrivateKey when !string.IsNullOrWhiteSpace(server.PrivateKeyPath)
                => CreatePrivateKeyAuth(server, secret),
            SshAuthenticationType.Password when !string.IsNullOrEmpty(secret)
                => new PasswordAuthenticationMethod(server.Username, secret),
            SshAuthenticationType.Agent
                => throw new NotSupportedException("SSH agent authentication is planned but not implemented yet."),
            _ => throw new InvalidOperationException("The configured SSH authentication method is not ready.")
        };

        return new ConnectionInfo(server.Host, server.Port, server.Username, auth)
        {
            Timeout = timeout
        };
    }

    private static AuthenticationMethod CreatePrivateKeyAuth(ServerProfile server, string? passphrase)
    {
        if (string.IsNullOrWhiteSpace(server.PrivateKeyPath))
        {
            throw new InvalidOperationException("Private key path is required.");
        }

        if (!File.Exists(server.PrivateKeyPath))
        {
            throw new FileNotFoundException("SSH private key was not found.", server.PrivateKeyPath);
        }

        var key = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(server.PrivateKeyPath)
            : new PrivateKeyFile(server.PrivateKeyPath, passphrase);

        return new PrivateKeyAuthenticationMethod(server.Username, key);
    }
}
