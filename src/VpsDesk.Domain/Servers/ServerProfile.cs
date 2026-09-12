namespace VpsDesk.Domain.Servers;

public enum ServerEnvironment
{
    Development,
    Staging,
    Production
}

public enum SshAuthenticationType
{
    PrivateKey,
    Password,
    Agent
}

public sealed record ServerProfile(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string Username,
    SshAuthenticationType AuthenticationType,
    string? PrivateKeyPath,
    string? SecretReference,
    string? ProviderLabel,
    ServerEnvironment Environment,
    IReadOnlyCollection<string> Tags);
