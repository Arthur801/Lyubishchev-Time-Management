using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class ReportServiceTests
{
    private static ReportService CreateService(AppDbContext db, DateTime utcNow)
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
    public async Task GetCategoryAsync_defaults_to_the_current_calendar_month_when_no_range_is_given()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        // September in Asia/Taipei for this clock.
        var now = new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc));
        // Outside September; must not be counted.
        await CreateEntryAsync(db, userId, new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc));

        var service = CreateService(db, now);
        var result = await service.GetCategoryAsync(userId, null, null, null, default);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Response!.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), result.Response.EndDateInclusive);
        Assert.Equal(3600, result.Response.TotalSeconds);
    }

    [Theory]
    [InlineData("today")]
    [InlineData("week")]
    [InlineData("month")]
    public async Task GetCategoryAsync_accepts_every_supported_preset(string preset)
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetCategoryAsync(userId, preset, null, null, default);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetTagAsync_uses_a_custom_date_range_when_no_preset_is_given()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 10, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 10, 2, 0, 0, DateTimeKind.Utc), tags: ["focus"]);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.GetTagAsync(userId, null, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 12), default);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 1, 10), result.Response!.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 12), result.Response.EndDateInclusive);
        Assert.Equal(3600, result.Response.TotalSeconds);
    }

    [Fact]
    public async Task GetCategoryAsync_rejects_a_custom_range_whose_end_precedes_its_start()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetCategoryAsync(userId, null, new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 10), default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_DATE_RANGE", result.ErrorCode);
    }

    [Fact]
    public async Task GetCategoryAsync_rejects_an_unrecognized_preset()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetCategoryAsync(userId, "nonsense", null, null, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_RANGE_PRESET", result.ErrorCode);
    }

    [Fact]
    public async Task GetCategoryAsync_rejects_a_preset_combined_with_custom_dates()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetCategoryAsync(userId, "today", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_RANGE_PRESET", result.ErrorCode);
    }

    [Fact]
    public async Task GetTagAsync_rejects_a_partially_specified_custom_range()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.GetTagAsync(userId, null, new DateOnly(2026, 1, 1), null, default);

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_RANGE_PRESET", result.ErrorCode);
    }

    [Fact]
    public async Task GetCategoryAsync_includes_uncategorized_time_with_the_shared_uncategorized_color()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var now = new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc));

        var service = CreateService(db, now);
        var result = await service.GetCategoryAsync(userId, "month", null, null, default);

        var uncategorized = Assert.Single(result.Response!.CategoryTotals);
        Assert.Null(uncategorized.CategoryId);
        Assert.Equal("#a6adb7", uncategorized.Color);
        Assert.Equal(3600, uncategorized.DurationSeconds);
    }

    [Fact]
    public async Task GetTagAsync_keeps_additive_tag_durations_and_sorts_by_duration_then_name()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var now = new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc), tags: ["focus", "planning"]);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 1, 30, 0, DateTimeKind.Utc), tags: ["focus"]);

        var service = CreateService(db, now);
        var result = await service.GetTagAsync(userId, "month", null, null, default);

        Assert.Equal(["focus", "planning"], result.Response!.TagTotals.Select(t => t.Name));
        Assert.True(result.Response.TagTotals.Sum(t => t.DurationSeconds) > result.Response.TotalSeconds);
    }

    [Fact]
    public async Task GetCategoryAsync_and_GetTagAsync_return_empty_totals_when_there_is_no_data()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var category = await service.GetCategoryAsync(userId, "month", null, null, default);
        var tag = await service.GetTagAsync(userId, "month", null, null, default);

        Assert.Equal(0, category.Response!.TotalSeconds);
        Assert.Empty(category.Response.CategoryTotals);
        Assert.Empty(tag.Response!.TagTotals);
    }

    [Fact]
    public async Task GetCategoryAsync_only_reads_the_calling_users_entries()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        await CreateEntryAsync(db, otherUserId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 2, 0, 0, DateTimeKind.Utc));

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.GetCategoryAsync(userId, "month", null, null, default);

        Assert.Equal(0, result.Response!.TotalSeconds);
        Assert.Empty(result.Response.CategoryTotals);
    }

    [Fact]
    public async Task GetCategoryAsync_sorts_categories_by_duration_descending_then_name()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var now = new DateTime(2026, 1, 15, 4, 0, 0, DateTimeKind.Utc);
        var work = await TestDatabase.CreateCategoryAsync(db, userId, "Work", "#111111");
        var rest = await TestDatabase.CreateCategoryAsync(db, userId, "Rest", "#222222");
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 1, 30, 0, DateTimeKind.Utc), rest.Id);
        await CreateEntryAsync(db, userId, new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 2, 0, 0, DateTimeKind.Utc), work.Id);

        var service = CreateService(db, now);
        var result = await service.GetCategoryAsync(userId, "month", null, null, default);

        Assert.Equal(["Work", "Rest"], result.Response!.CategoryTotals.Select(c => c.Name));
    }
}
