using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record UserSettingsResult(bool Succeeded, TimezoneSettingsResponse? Settings, string? ErrorCode, string? ErrorMessage)
{
    public static UserSettingsResult Ok(TimezoneSettingsResponse settings) => new(true, settings, null, null);

    public static UserSettingsResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed class UserSettingsService(AppDbContext dbContext, IClock clock, TimeZoneCatalog catalog)
{
    private const string InvalidTimeZoneMessage = "不支援的時區。";
    private const string UserNotFoundMessage = "找不到使用者。";

    public async Task<TimezoneSettingsResponse> GetAsync(ulong userId, CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .SingleAsync(cancellationToken);

        return new TimezoneSettingsResponse(timeZoneId, TimeZoneCatalog.Options);
    }

    public async Task<UserSettingsResult> UpdateAsync(ulong userId, string? timeZoneId, CancellationToken cancellationToken)
    {
        if (!catalog.IsSupported(timeZoneId))
        {
            return UserSettingsResult.Fail("INVALID_TIME_ZONE", InvalidTimeZoneMessage);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return UserSettingsResult.Fail("USER_NOT_FOUND", UserNotFoundMessage);
        }

        user.TimeZoneId = timeZoneId!;
        user.UpdatedAtUtc = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return UserSettingsResult.Ok(new TimezoneSettingsResponse(user.TimeZoneId, TimeZoneCatalog.Options));
    }
}
