using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class UserSettingsServiceTests
{
    [Fact]
    public async Task UpdateAsync_updates_only_the_current_users_timezone_and_timestamp()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var updatedAt = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(options);
        var service = new UserSettingsService(db, new TestClock(updatedAt), new TimeZoneCatalog());

        var result = await service.UpdateAsync(userId, "America/New_York", default);

        Assert.True(result.Succeeded);
        Assert.Equal("America/New_York", result.Settings!.TimeZoneId);

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("America/New_York", user.TimeZoneId);
        Assert.Equal(updatedAt, user.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_does_not_touch_any_TimeEntry_UTC_values()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var originalStart = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var originalEnd = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        ulong entryId;
        await using (var db = new AppDbContext(options))
        {
            var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
            var created = await entryService.CreateAsync(userId, new()
            {
                StartTimeUtc = originalStart,
                EndTimeUtc = originalEnd,
            }, default);
            entryId = created.Entry!.Id;
        }

        await using var updateDb = new AppDbContext(options);
        var service = new UserSettingsService(updateDb, new TestClock(DateTime.UtcNow), new TimeZoneCatalog());
        await service.UpdateAsync(userId, "Asia/Tokyo", default);

        await using var assertDb = new AppDbContext(options);
        var entry = await assertDb.TimeEntries.SingleAsync(e => e.Id == entryId);
        Assert.Equal(originalStart, entry.StartTimeUtc);
        Assert.Equal(originalEnd, entry.EndTimeUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public async Task UpdateAsync_rejects_missing_blank_or_unsupported_ids(string? timeZoneId)
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new UserSettingsService(db, new TestClock(DateTime.UtcNow), new TimeZoneCatalog());

        var result = await service.UpdateAsync(userId, timeZoneId, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_TIME_ZONE", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_returns_USER_NOT_FOUND_for_a_nonexistent_user()
    {
        var (keepAlive, options, _) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new UserSettingsService(db, new TestClock(DateTime.UtcNow), new TimeZoneCatalog());

        var result = await service.UpdateAsync(999_999, "UTC", default);

        Assert.False(result.Succeeded);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_selecting_the_current_timezone_still_succeeds_and_bumps_UpdatedAtUtc()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        var firstUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondUpdate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = new AppDbContext(options))
        {
            var service = new UserSettingsService(db, new TestClock(firstUpdate), new TimeZoneCatalog());
            await service.UpdateAsync(userId, "Asia/Taipei", default);
        }

        await using (var db = new AppDbContext(options))
        {
            var service = new UserSettingsService(db, new TestClock(secondUpdate), new TimeZoneCatalog());
            var result = await service.UpdateAsync(userId, "Asia/Taipei", default);
            Assert.True(result.Succeeded);
        }

        await using var assertDb = new AppDbContext(options);
        var user = await assertDb.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("Asia/Taipei", user.TimeZoneId);
        Assert.Equal(secondUpdate, user.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_for_one_user_does_not_change_another_users_timezone()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        var service = new UserSettingsService(db, new TestClock(DateTime.UtcNow), new TimeZoneCatalog());

        await service.UpdateAsync(userId, "Europe/London", default);

        var otherUser = await db.Users.SingleAsync(u => u.Id == otherUserId);
        Assert.Equal("Asia/Taipei", otherUser.TimeZoneId);
    }

    [Fact]
    public async Task GetAsync_returns_the_users_current_timezone_and_the_full_option_list()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = new UserSettingsService(db, new TestClock(DateTime.UtcNow), new TimeZoneCatalog());

        var settings = await service.GetAsync(userId, default);

        Assert.Equal("Asia/Taipei", settings.TimeZoneId);
        Assert.Equal(TimeZoneCatalog.Options.Count, settings.Options.Count);
    }
}
