using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TimerFlow.Tests.Integration;

public sealed class TimerServiceTests
{
    private static async Task<(SqliteConnection KeepAlive, DbContextOptions<AppDbContext> Options, ulong UserId)> CreateSharedDatabaseAsync()
    {
        // Shared-cache SQLite in-memory database: every DbContext built from these options sees
        // the same data, as long as the KeepAlive connection stays open. Needed (instead of the
        // EF Core InMemory provider) because Stop uses Database.BeginTransactionAsync, which the
        // InMemory provider does not support.
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

        ulong userId;
        await using (var initContext = new AppDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();

            // RunningTimers/TimeEntries have a required FK to Users, and unlike the EF Core
            // InMemory provider, SQLite enforces it -- so every test needs a real user row.
            var user = new User
            {
                Email = $"{Guid.NewGuid():N}@example.com",
                PasswordHash = "not-a-real-hash",
                TimeZoneId = "Asia/Taipei",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            initContext.Users.Add(user);
            await initContext.SaveChangesAsync();
            userId = user.Id;
        }

        return (keepAlive, options, userId);
    }

    private static TimerService CreateService(AppDbContext dbContext, TestClock clock) => new(dbContext, clock, new TimeEntryService(dbContext, clock));

    [Fact]
    public async Task StartAsync_starts_timer_when_none_is_running()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var clock = new TestClock(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var service = CreateService(dbContext, clock);

        var result = await service.StartAsync(userId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Timer!.IsRunning);
        Assert.Equal(clock.UtcNow, result.Timer.StartedAtUtc);
        Assert.Equal(1, await dbContext.RunningTimers.CountAsync());
    }

    [Fact]
    public async Task StartAsync_rejects_when_a_timer_is_already_running()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = CreateService(dbContext, new TestClock(DateTime.UtcNow));

        await service.StartAsync(userId, CancellationToken.None);
        var result = await service.StartAsync(userId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TIMER_ALREADY_RUNNING", result.ErrorCode);
        Assert.Equal(1, await dbContext.RunningTimers.CountAsync());
    }

    [Fact]
    public async Task GetStatusAsync_reflects_whether_a_timer_is_running()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(dbContext, clock);

        var beforeStart = await service.GetStatusAsync(userId, CancellationToken.None);
        await service.StartAsync(userId, CancellationToken.None);
        var afterStart = await service.GetStatusAsync(userId, CancellationToken.None);

        Assert.False(beforeStart.IsRunning);
        Assert.Null(beforeStart.StartedAtUtc);
        Assert.True(afterStart.IsRunning);
        Assert.Equal(clock.UtcNow, afterStart.StartedAtUtc);
    }

    [Fact]
    public async Task StopAsync_fails_when_no_timer_is_running()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var service = CreateService(dbContext, new TestClock(DateTime.UtcNow));

        var result = await service.StopAsync(userId, "test", null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TIMER_NOT_RUNNING", result.ErrorCode);
    }

    [Fact]
    public async Task StopAsync_creates_a_time_entry_and_clears_the_running_timer()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var clock = new TestClock(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var service = CreateService(dbContext, clock);

        await service.StartAsync(userId, CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddMinutes(25);
        var result = await service.StopAsync(userId, "  深度工作  ", null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("深度工作", result.TimeEntry!.Name);
        Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), result.TimeEntry.StartTimeUtc);
        Assert.Equal(clock.UtcNow, result.TimeEntry.EndTimeUtc);
        Assert.Equal(1500, result.TimeEntry.DurationSeconds);
        Assert.Equal(0, await dbContext.RunningTimers.CountAsync());
        Assert.Equal(1, await dbContext.TimeEntries.CountAsync());
    }

    [Fact]
    public async Task StopAsync_keeps_end_time_after_start_time_even_when_the_clock_has_not_advanced()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var clock = new TestClock(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var service = CreateService(dbContext, clock);

        await service.StartAsync(userId, CancellationToken.None);
        var result = await service.StopAsync(userId, null, null, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.TimeEntry!.EndTimeUtc > result.TimeEntry.StartTimeUtc);
    }

    [Fact]
    public async Task StopAsync_attaches_the_callers_own_category()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var category = new Category { UserId = userId, Name = "Focus", NormalizedName = "FOCUS", Color = "#e5533d", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, User = null! };
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new TestClock(DateTime.UtcNow));

        await service.StartAsync(userId, CancellationToken.None);
        var result = await service.StopAsync(userId, null, category.Id, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(category.Id, result.TimeEntry!.CategoryId);
        Assert.Equal("Focus", result.TimeEntry.CategoryName);
        Assert.Equal("#e5533d", result.TimeEntry.CategoryColor);
    }

    [Fact]
    public async Task StopAsync_rejects_a_category_owned_by_another_user_and_leaves_the_timer_running()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var otherUser = new User { Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "x", TimeZoneId = "Asia/Taipei", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        dbContext.Users.Add(otherUser);
        await dbContext.SaveChangesAsync();
        var othersCategory = new Category { UserId = otherUser.Id, Name = "Private", NormalizedName = "PRIVATE", Color = "#000000", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, User = null! };
        dbContext.Categories.Add(othersCategory);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new TestClock(DateTime.UtcNow));

        await service.StartAsync(userId, CancellationToken.None);
        var result = await service.StopAsync(userId, null, othersCategory.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CATEGORY_NOT_FOUND", result.ErrorCode);
        Assert.Equal(1, await dbContext.RunningTimers.CountAsync());
        Assert.Equal(0, await dbContext.TimeEntries.CountAsync());
    }

    [Fact]
    public async Task StopAsync_finds_or_creates_tags_by_normalized_name()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);
        var existing = new Tag { UserId = userId, Name = "Deep Work", NormalizedName = "DEEP WORK", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, User = null! };
        dbContext.Tags.Add(existing);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new TestClock(DateTime.UtcNow));

        await service.StartAsync(userId, CancellationToken.None);
        var result = await service.StopAsync(userId, null, null, [" deep work ", "urgent"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["Deep Work", "urgent"], result.TimeEntry!.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(2, await dbContext.Tags.CountAsync());
    }

    [Fact]
    public async Task StopAsync_lets_only_one_concurrent_call_create_a_time_entry()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.RunningTimers.Add(new RunningTimer { UserId = userId, StartedAtUtc = DateTime.UtcNow.AddMinutes(-5), User = null! });
            await seedContext.SaveChangesAsync();
        }

        var clock = new TestClock(DateTime.UtcNow);

        async Task<StopTimerResult> StopFromNewContextAsync(string name)
        {
            await using var dbContext = new AppDbContext(options);
            var service = CreateService(dbContext, clock);
            return await service.StopAsync(userId, name, null, null, CancellationToken.None);
        }

        // Real parallel execution against the same shared-cache database: SQLite serializes the
        // two writers, so whichever DELETE runs second affects zero rows and should surface as
        // "nothing to stop" rather than a second TimeEntry.
        var results = await Task.WhenAll(StopFromNewContextAsync("device A"), StopFromNewContextAsync("device B"));

        Assert.Single(results, r => r.Succeeded);
        Assert.Single(results, r => !r.Succeeded && r.ErrorCode == "TIMER_NOT_RUNNING");

        await using var assertContext = new AppDbContext(options);
        Assert.Equal(1, await assertContext.TimeEntries.CountAsync());
        Assert.Equal(0, await assertContext.RunningTimers.CountAsync());
    }
}
