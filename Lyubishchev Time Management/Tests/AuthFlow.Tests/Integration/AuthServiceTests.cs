using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthFlow.Tests.Integration;

public sealed class AuthServiceTests
{
    private sealed class NoOpAuthEventLogger : IAuthEventLogger
    {
        public void LoginSucceeded(string email, string? ipAddress)
        {
        }

        public void LoginFailed(string email, string errorCode, string? ipAddress)
        {
        }

        public void RegisterSucceeded(string email, string? ipAddress)
        {
        }

        public void RegisterFailed(string email, string errorCode, string? ipAddress)
        {
        }

        public void RateLimitExceeded(string endpoint, string? ipAddress)
        {
        }
    }

    private static AuthService CreateAuthService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "unit-test-signing-key-unit-test-signing-key",
            ExpirationHours = 8,
        });

        return new AuthService(dbContext, new JwtTokenService(jwtOptions), new NoOpAuthEventLogger());
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RegisterAsync_creates_user_and_returns_token()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        var result = await authService.RegisterAsync("New.User@Example.com", "correct-password", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("new.user@example.com", (await dbContext.Users.SingleAsync()).Email);
    }

    [Fact]
    public async Task RegisterAsync_rejects_duplicate_email()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        await authService.RegisterAsync("duplicate@example.com", "correct-password", null, CancellationToken.None);
        var result = await authService.RegisterAsync("duplicate@example.com", "another-password", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("EMAIL_ALREADY_REGISTERED", result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_succeeds_with_correct_password()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        await authService.RegisterAsync("login@example.com", "correct-password", null, CancellationToken.None);
        var result = await authService.LoginAsync("login@example.com", "correct-password", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
    }

    [Fact]
    public async Task LoginAsync_rejects_wrong_password()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        await authService.RegisterAsync("wrongpass@example.com", "correct-password", null, CancellationToken.None);
        var result = await authService.LoginAsync("wrongpass@example.com", "incorrect-password", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_rejects_unknown_email()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        var result = await authService.LoginAsync("nobody@example.com", "any-password", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.ErrorCode);
    }
}
