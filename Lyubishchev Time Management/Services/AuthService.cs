using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record AuthResult(bool Succeeded, string? Token, string? ErrorCode, string? ErrorMessage)
{
    public static AuthResult Ok(string token) => new(true, token, null, null);

    public static AuthResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed class AuthService(AppDbContext dbContext, JwtTokenService jwtTokenService)
{
    private const string DefaultTimeZoneId = "Asia/Taipei";

    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async Task<AuthResult> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var emailTaken = await dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            return AuthResult.Fail("EMAIL_ALREADY_REGISTERED", "此電子郵件已被註冊。");
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = string.Empty,
            TimeZoneId = DefaultTimeZoneId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        user.PasswordHash = PasswordHasher.HashPassword(user, password);

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The email is the only unique constraint on Users, so a failed insert
            // here means another request registered the same email concurrently.
            return AuthResult.Fail("EMAIL_ALREADY_REGISTERED", "此電子郵件已被註冊。");
        }

        return AuthResult.Ok(jwtTokenService.CreateToken(user));
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return AuthResult.Fail("INVALID_CREDENTIALS", "電子郵件或密碼錯誤。");
        }

        var verificationResult = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return AuthResult.Fail("INVALID_CREDENTIALS", "電子郵件或密碼錯誤。");
        }

        return AuthResult.Ok(jwtTokenService.CreateToken(user));
    }
}
