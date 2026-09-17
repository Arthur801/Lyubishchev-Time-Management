using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class DashboardServiceTests
{
    private static DashboardService CreateService(AppDbContext db, DateTime utcNow)
        => new(db, new TimeAggregationService(db, new TestClock(utcNow), new TimeZoneCatalog()));

    private static async Task CreateEntryAsync(AppDbContext db, ulong userId, DateTime startUtc, DateTime endUtc, ulong? categoryId = null, List<string>? tags = null)
    {
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            CategoryId = categoryId,
            Tags = tags,
        }, default);
    }

    [Fact]
    public async Task GetAsync_uses_the_previous_adjacent_range_for_comparison()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        // Asia/Taipei "today" for this clock is 2026-01-15.
        var now = new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 15, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc));
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 14, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 14, 1, 30, 0, DateTimeKind.Utc));

        var service = CreateService(db, now);
        var result = await service.GetAsync(userId, "today", null, null, default);

        Assert.True(result.Succeeded);
        Assert.Equal(3600, result.Response!.TotalSeconds);
        Assert.Equal(1800, result.Response.ComparisonSeconds);
        Assert.Equal(1800, result.Response.ChangeSeconds);
        Assert.Equal(new DateOnly(2026, 1, 15), result.Response.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 15), result.Response.EndDateInclusive);
    }

    [Fact]
    public async Task GetAsync_ThisWeek_compares_against_the_previous_calendar_week()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        // 2026-01-18 is a Sunday in Asia/Taipei -> this week is Jan 18-24, previous week Jan 11-17.
        var now = new DateTime(2026, 1, 20, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 18, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 18, 2, 0, 0, DateTimeKind.Utc));
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 12, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 12, 1, 45, 0, DateTimeKind.Utc));

        var service = CreateService(db, now);
        var result = await service.GetAsync(userId, "week", null, null, default);

        Assert.Equal(3600, result.Response!.TotalSeconds);
        Assert.Equal(2700, result.Response.ComparisonSeconds);
        Assert.Equal(new DateOnly(2026, 1, 18), result.Response.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 24), result.Response.EndDateInclusive);
    }

    [Fact]
    public async Task GetAsync_ThisMonth_compares_against_the_previous_calendar_month_of_different_length()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        // September (30 days) vs August (31 days) in Asia/Taipei.
        var now = new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc));
        await CreateEntryAsync(db, userId, new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 31, 1, 20, 0, DateTimeKind.Utc));
        // Outside the previous calendar month; must not be counted in the comparison.
        await CreateEntryAsync(db, userId, new DateTime(2026, 7, 30, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 30, 3, 0, 0, DateTimeKind.Utc));

        var service = CreateService(db, now);
        var result = await service.GetAsync(userId, "month", null, null, default);

        Assert.Equal(new DateOnly(2026, 9, 1), result.Response!.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), result.Response.EndDateInclusive);
        Assert.Equal(3600, result.Response.TotalSeconds);
        Assert.Equal(1200, result.Response.ComparisonSeconds);
    }

    [Fact]
    public async Task GetAsync_custom_range_compares_against_an_equal_length_preceding_window()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 10, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 10, 2, 0, 0, DateTimeKind.Utc));
        // Three-day custom range Jan 10-12 -> comparison is the preceding three days, Jan 7-9.
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 8, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 8, 1, 30, 0, DateTimeKind.Utc));

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.GetAsync(userId, null, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 12), default);

        Assert.Equal(3600, result.Response!.TotalSeconds);
        Assert.Equal(1800, result.Response.ComparisonSeconds);
    }

    [Fact]
    public async Task GetAsync_rejects_a_custom_range_whose_end_precedes_its_start()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetAsync(userId, null, new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 10), default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_DATE_RANGE", result.ErrorCode);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetAsync_rejects_an_unrecognized_or_missing_preset_with_no_custom_dates(string? preset)
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetAsync(userId, preset, null, null, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_RANGE_PRESET", result.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_rejects_a_preset_combined_with_custom_dates()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetAsync(userId, "today", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_RANGE_PRESET", result.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_with_no_entries_returns_zero_totals_and_no_recent_entries()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetAsync(userId, "today", null, null, default);

        Assert.Equal(0, result.Response!.TotalSeconds);
        Assert.Equal(0, result.Response.ComparisonSeconds);
        Assert.Empty(result.Response.RecentEntries);
        Assert.Empty(result.Response.CategoryTotals);
    }

    [Fact]
    public async Task GetAsync_returns_the_five_most_recently_ended_entries_newest_first()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        for (var i = 0; i < 6; i++)
        {
            await CreateEntryAsync(db, userId,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc).AddHours(i));
        }

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.GetAsync(userId, "month", null, null, default);

        Assert.Equal(5, result.Response!.RecentEntries.Count);
        Assert.Equal(
            new DateTime(2026, 1, 1, 5, 30, 0, DateTimeKind.Utc),
            result.Response.RecentEntries[0].EndTimeUtc);
    }

    [Fact]
    public async Task GetAsync_only_reads_the_calling_users_entries()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        await CreateEntryAsync(db, otherUserId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc));

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.GetAsync(userId, "month", null, null, default);

        Assert.Equal(0, result.Response!.TotalSeconds);
        Assert.Empty(result.Response.RecentEntries);
    }

    [Fact]
    public async Task GetAsync_passes_through_uncategorized_and_additive_tag_totals_from_aggregation()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var now = new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc), tags: ["a", "b"]);

        var service = CreateService(db, now);
        var result = await service.GetAsync(userId, "month", null, null, default);

        var uncategorized = Assert.Single(result.Response!.CategoryTotals);
        Assert.Null(uncategorized.CategoryId);
        Assert.Equal(3600, uncategorized.DurationSeconds);
        Assert.Equal(2, result.Response.TagTotals.Count);
        Assert.True(result.Response.TagTotals.Sum(t => t.DurationSeconds) > result.Response.TotalSeconds);
    }
}
