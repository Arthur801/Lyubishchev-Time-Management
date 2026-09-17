using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Models.Responses;
using Lyubishchev_Time_Management.Services;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class TimeAggregationServiceTests
{
    private static TimeAggregationService CreateService(AppDbContext db, DateTime utcNow)
        => new(db, new TestClock(utcNow), new TimeZoneCatalog());

    [Fact]
    public void GetRangeForPreset_Today_returns_the_local_calendar_date()
    {
        // 2026-09-16 04:00 UTC is 2026-09-16 12:00 in Asia/Taipei (UTC+8).
        var service = CreateService(null!, new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc));

        var range = service.GetRangeForPreset(DateRangePreset.Today, "Asia/Taipei");

        Assert.Equal(new DateOnly(2026, 9, 16), range.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 16), range.EndDateInclusive);
    }

    [Fact]
    public void GetRangeForPreset_ThisWeek_in_AsiaTaipei_starts_on_sunday_and_ends_exclusively_next_sunday()
    {
        var service = CreateService(null!, new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc));

        var range = service.GetRangeForPreset(DateRangePreset.ThisWeek, "Asia/Taipei");

        Assert.Equal(new DateOnly(2026, 9, 13), range.StartDate);
        Assert.Equal(DayOfWeek.Sunday, range.StartDate.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 9, 19), range.EndDateInclusive);
    }

    [Fact]
    public void GetRangeForPreset_ThisMonth_in_AsiaTaipei_spans_the_whole_calendar_month()
    {
        var service = CreateService(null!, new DateTime(2026, 9, 16, 4, 0, 0, DateTimeKind.Utc));

        var range = service.GetRangeForPreset(DateRangePreset.ThisMonth, "Asia/Taipei");

        Assert.Equal(new DateOnly(2026, 9, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), range.EndDateInclusive);
    }

    [Fact]
    public async Task AggregateAsync_rejects_a_range_whose_end_precedes_its_start()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 2), new(2026, 1, 1)), "UTC", default));
    }

    [Fact]
    public async Task AggregateAsync_splits_a_cross_midnight_entry_into_local_daily_totals()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        // Asia/Taipei 2026-01-01 23:00 -> 2026-01-02 01:00 local (UTC+8, no DST).
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 15, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 2)), "Asia/Taipei", default);

        Assert.Equal(7200, result.TotalSeconds);
        Assert.Equal([3600L, 3600L], result.DailyTotals.Select(d => d.DurationSeconds));
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)], result.DailyTotals.Select(d => d.Date));
    }

    [Fact]
    public async Task AggregateAsync_prepopulates_days_with_no_entries_as_zero()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var service = CreateService(db, DateTime.UtcNow);

        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 3)), "UTC", default);

        Assert.Equal([0L, 0L, 0L], result.DailyTotals.Select(d => d.DurationSeconds));
    }

    [Fact]
    public async Task AggregateAsync_excludes_entries_entirely_outside_the_range()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2025, 12, 31, 1, 0, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        Assert.Equal(0, result.TotalSeconds);
        Assert.Empty(result.CategoryTotals);
    }

    [Fact]
    public async Task AggregateAsync_buckets_entries_without_a_category_as_Uncategorized()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        var bucket = Assert.Single(result.CategoryTotals);
        Assert.Null(bucket.CategoryId);
        Assert.Equal("未分類", bucket.Name);
        Assert.Equal("#a6adb7", bucket.Color);
        Assert.Equal(3600, bucket.DurationSeconds);
    }

    [Fact]
    public async Task AggregateAsync_category_totals_sum_to_the_overall_total()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var work = await TestDatabase.CreateCategoryAsync(db, userId, "Work", "#111111");
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            CategoryId = work.Id,
        }, default);
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 11, 30, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        Assert.Equal(result.TotalSeconds, result.CategoryTotals.Sum(c => c.DurationSeconds));
        Assert.Equal(5400, result.TotalSeconds);
    }

    [Fact]
    public async Task AggregateAsync_tag_totals_are_additive_and_can_exceed_the_overall_total()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Tags = ["deep-work", "urgent"],
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        Assert.Equal(3600, result.TotalSeconds);
        Assert.Equal(2, result.TagTotals.Count);
        Assert.All(result.TagTotals, tag => Assert.Equal(3600, tag.DurationSeconds));
        Assert.True(result.TagTotals.Sum(t => t.DurationSeconds) > result.TotalSeconds);
    }

    [Fact]
    public async Task AggregateAsync_keeps_overlapping_entries_additive()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        }, default);
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        Assert.Equal(7200, result.TotalSeconds);
    }

    [Fact]
    public async Task AggregateAsync_only_reads_the_calling_users_entries()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(otherUserId, new CreateTimeEntryRequest
        {
            StartTimeUtc = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 1, 1), new(2026, 1, 1)), "UTC", default);

        Assert.Equal(0, result.TotalSeconds);
    }

    [Fact]
    public async Task AggregateAsync_NewYork_spring_forward_day_is_only_twenty_three_hours()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var catalog = new TimeZoneCatalog();
        var newYork = catalog.ResolveOrUtc("America/New_York");
        // 2026-03-08 is the US spring-forward date (2am -> 3am).
        var localMidnightStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Unspecified), newYork);
        var localMidnightEnd = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Unspecified), newYork);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = localMidnightStart, EndTimeUtc = localMidnightEnd }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 3, 8), new(2026, 3, 8)), "America/New_York", default);

        Assert.Equal(23 * 3600, result.TotalSeconds);
    }

    [Fact]
    public async Task AggregateAsync_NewYork_fall_back_day_is_twenty_five_hours()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var catalog = new TimeZoneCatalog();
        var newYork = catalog.ResolveOrUtc("America/New_York");
        // 2026-11-01 is the US fall-back date (2am -> 1am).
        var localMidnightStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified), newYork);
        var localMidnightEnd = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Unspecified), newYork);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = localMidnightStart, EndTimeUtc = localMidnightEnd }, default);

        var service = CreateService(db, DateTime.UtcNow);
        var result = await service.AggregateAsync(userId, new LocalDateRange(new(2026, 11, 1), new(2026, 11, 1)), "America/New_York", default);

        Assert.Equal(25 * 3600, result.TotalSeconds);
    }
}
