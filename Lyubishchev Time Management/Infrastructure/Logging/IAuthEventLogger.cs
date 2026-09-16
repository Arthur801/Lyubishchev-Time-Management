namespace Lyubishchev_Time_Management.Infrastructure.Logging;

public interface IAuthEventLogger
{
    void LoginSucceeded(string email, string? ipAddress);

    void LoginFailed(string email, string errorCode, string? ipAddress);

    void RegisterSucceeded(string email, string? ipAddress);

    void RegisterFailed(string email, string errorCode, string? ipAddress);

    void RateLimitExceeded(string endpoint, string? ipAddress);
}
