using Microsoft.Extensions.Logging;

namespace Lyubishchev_Time_Management.Infrastructure.Logging;

/// <summary>
/// Emits authentication failure/success summaries only: never logs passwords, JWTs, or cookie values.
/// </summary>
public sealed class AuthEventLogger(ILogger<AuthEventLogger> logger) : IAuthEventLogger
{
    public void LoginSucceeded(string email, string? ipAddress) =>
        logger.LogInformation("Login succeeded. Email={Email} IpAddress={IpAddress}", email, ipAddress ?? "unknown");

    public void LoginFailed(string email, string errorCode, string? ipAddress) =>
        logger.LogWarning("Login failed. Email={Email} ErrorCode={ErrorCode} IpAddress={IpAddress}", email, errorCode, ipAddress ?? "unknown");

    public void RegisterSucceeded(string email, string? ipAddress) =>
        logger.LogInformation("Registration succeeded. Email={Email} IpAddress={IpAddress}", email, ipAddress ?? "unknown");

    public void RegisterFailed(string email, string errorCode, string? ipAddress) =>
        logger.LogWarning("Registration failed. Email={Email} ErrorCode={ErrorCode} IpAddress={IpAddress}", email, errorCode, ipAddress ?? "unknown");

    public void RateLimitExceeded(string endpoint, string? ipAddress) =>
        logger.LogWarning("Rate limit exceeded. Endpoint={Endpoint} IpAddress={IpAddress}", endpoint, ipAddress ?? "unknown");
}
