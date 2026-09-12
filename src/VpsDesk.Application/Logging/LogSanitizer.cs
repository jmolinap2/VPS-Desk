using System.Text.RegularExpressions;

namespace VpsDesk.Application.Logging;

public static partial class LogSanitizer
{
    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var sanitized = SecretKeyValueRegex().Replace(value, m => $"{m.Groups[1].Value}=***");
        sanitized = GitHubTokenRegex().Replace(sanitized, "ghp_***");
        sanitized = BearerRegex().Replace(sanitized, "Bearer ***");
        sanitized = ConnectionPasswordRegex().Replace(sanitized, "Password=***;");
        sanitized = ConnectionUserRegex().Replace(sanitized, "User Id=***;");
        sanitized = SshPasswordArgumentRegex().Replace(sanitized, "-SshPassword ***");
        sanitized = GitTokenArgumentRegex().Replace(sanitized, "-GitToken ***");
        return sanitized;
    }

    [GeneratedRegex(@"(?i)\b(password|pwd|token|apikey|api_key|secret)\s*[:=]\s*([^;\s]+)")]
    private static partial Regex SecretKeyValueRegex();

    [GeneratedRegex(@"\bghp_[A-Za-z0-9]{10,}\b")]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9\-_\.]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)Password\s*=\s*[^;]*;")]
    private static partial Regex ConnectionPasswordRegex();

    [GeneratedRegex(@"(?i)User\s*Id\s*=\s*[^;]*;")]
    private static partial Regex ConnectionUserRegex();

    [GeneratedRegex(@"(?i)-SshPassword\s+([^\s]+)")]
    private static partial Regex SshPasswordArgumentRegex();

    [GeneratedRegex(@"(?i)-GitToken\s+([^\s]+)")]
    private static partial Regex GitTokenArgumentRegex();
}
