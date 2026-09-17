using System.Text;
using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TimeEntryFlow.Tests.Integration;

public sealed class CsvExportServiceTests
{
    private static readonly IOperationalEventLogger NoopOperationalEventLogger = new OperationalEventLogger(NullLogger<OperationalEventLogger>.Instance);

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static CsvExportService CreateService(AppDbContext dbContext)
    {
        var clock = new TestClock(Utc(2026, 9, 17, 0, 0));
        var catalog = new TimeZoneCatalog();
        return new CsvExportService(dbContext, new UserSettingsService(dbContext, clock, catalog), catalog, NoopOperationalEventLogger);
    }

    // Decodes the exported bytes, verifies (and strips) the UTF-8 BOM, and splits into records —
    // every test asserts through this so the BOM check never gets silently skipped.
    private static List<string> DecodeRecords(byte[] content)
    {
        Assert.Equal([0xEF, 0xBB, 0xBF], content.Take(3));
        var text = Encoding.UTF8.GetString(content)[1..];
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    [Fact]
    public async Task ExportAsync_writes_complete_localized_rows_in_stable_descending_order()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync(); // default user zone: Asia/Taipei
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));

        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 1, 0),
            EndTimeUtc = Utc(2026, 1, 1, 2, 0),
        }, default); // older, uncategorized, no tags

        var categoryId = (await new CategoryService(db, new TestClock(DateTime.UtcNow)).CreateAsync(userId, new CategoryRequest { Name = "Work", Color = "#e5533d" }, default)).Category!.Id;
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            Name = "Deep work",
            StartTimeUtc = Utc(2026, 1, 2, 1, 0),
            EndTimeUtc = Utc(2026, 1, 2, 2, 30),
            CategoryId = categoryId,
            Tags = ["zeta", "Alpha"],
        }, default); // newer

        var document = await CreateService(db).ExportAsync(userId, new CsvExportRequest(null, null, null, null), default);
        var records = DecodeRecords(document.Content);

        Assert.Equal("Date,Start Time,End Time,Duration,Name,Category,Tags,Time Zone", records[0]);
        Assert.Equal("2026-01-02,2026-01-02 09:00:00,2026-01-02 10:30:00,01:30:00,Deep work,Work,Alpha; zeta,Asia/Taipei", records[1]);
        Assert.Equal("2026-01-01,2026-01-01 09:00:00,2026-01-01 10:00:00,01:00:00,,Uncategorized,,Asia/Taipei", records[2]);
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task ExportAsync_includes_a_cross_midnight_entry_once_when_it_overlaps_the_bounded_range()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));

        // 2026-01-01 23:00 -> 2026-01-02 01:00 Asia/Taipei local (UTC+8).
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 1, 1, 15, 0),
            EndTimeUtc = Utc(2026, 1, 1, 17, 0),
        }, default);

        var request = new CsvExportRequest(Utc(2026, 1, 1, 16, 0), Utc(2026, 1, 2, 16, 0), null, null);
        var document = await CreateService(db).ExportAsync(userId, request, default);
        var records = DecodeRecords(document.Content);

        Assert.Equal(2, records.Count);
        Assert.Equal("2026-01-01,2026-01-01 23:00:00,2026-01-02 01:00:00,02:00:00,,Uncategorized,,Asia/Taipei", records[1]);
    }

    [Fact]
    public async Task ExportAsync_applies_category_and_name_or_tag_filters_without_pagination()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        var categoryId = (await new CategoryService(db, new TestClock(DateTime.UtcNow)).CreateAsync(userId, new CategoryRequest { Name = "Work", Color = "#e5533d" }, default)).Category!.Id;

        for (var i = 0; i < 201; i++)
        {
            await entryService.CreateAsync(userId, new CreateTimeEntryRequest
            {
                StartTimeUtc = Utc(2026, 1, 1, 0, 0).AddHours(i),
                EndTimeUtc = Utc(2026, 1, 1, 0, 30).AddHours(i),
                CategoryId = categoryId,
            }, default);
        }

        // A non-matching entry: different category (none) so it must not appear in the export.
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            StartTimeUtc = Utc(2026, 6, 1, 0, 0),
            EndTimeUtc = Utc(2026, 6, 1, 1, 0),
        }, default);

        var request = new CsvExportRequest(null, null, categoryId, null);
        var document = await CreateService(db).ExportAsync(userId, request, default);
        var records = DecodeRecords(document.Content);

        Assert.Equal(202, records.Count); // header + 201 matching rows, proving no page-size limit applies
    }

    [Fact]
    public async Task ExportAsync_excludes_another_users_entries_and_returns_header_only_when_empty()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var otherUserId = await TestDatabase.CreateUserAsync(db);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));

        await entryService.CreateAsync(userId, new CreateTimeEntryRequest
        {
            Name = "Mine",
            StartTimeUtc = Utc(2026, 1, 1, 1, 0),
            EndTimeUtc = Utc(2026, 1, 1, 2, 0),
        }, default);
        await entryService.CreateAsync(otherUserId, new CreateTimeEntryRequest
        {
            Name = "Theirs",
            StartTimeUtc = Utc(2026, 1, 1, 1, 0),
            EndTimeUtc = Utc(2026, 1, 1, 2, 0),
        }, default);

        var document = await CreateService(db).ExportAsync(userId, new CsvExportRequest(null, null, null, null), default);
        var text = Encoding.UTF8.GetString(document.Content);
        Assert.Contains("Mine", text);
        Assert.DoesNotContain("Theirs", text);

        var emptyRangeRequest = new CsvExportRequest(Utc(2027, 1, 1, 0, 0), Utc(2027, 1, 2, 0, 0), null, null);
        var emptyDocument = await CreateService(db).ExportAsync(userId, emptyRangeRequest, default);
        var records = DecodeRecords(emptyDocument.Content);
        Assert.Single(records);
        Assert.Equal("Date,Start Time,End Time,Duration,Name,Category,Tags,Time Zone", records[0]);
    }

    [Fact]
    public async Task ExportAsync_uses_the_saved_dst_zone_for_display_but_utc_duration_for_elapsed_time()
    {
        var (keepAlive, options, userId) = await TestDatabase.CreateAsync();
        await using var _ = keepAlive;
        await using var db = new AppDbContext(options);
        var catalog = new TimeZoneCatalog();
        var settingsService = new UserSettingsService(db, new TestClock(DateTime.UtcNow), catalog);
        await settingsService.UpdateAsync(userId, "America/New_York", default);

        var newYork = catalog.ResolveOrUtc("America/New_York");
        // 2026-03-08 is the US spring-forward date (2am -> 3am): 01:30 EST -> 03:30 EDT is exactly
        // one elapsed hour in UTC despite a two-hour apparent wall-clock gap.
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 8, 1, 30, 0, DateTimeKind.Unspecified), newYork);
        var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 8, 3, 30, 0, DateTimeKind.Unspecified), newYork);
        var entryService = new TimeEntryService(db, new TestClock(DateTime.UtcNow));
        await entryService.CreateAsync(userId, new CreateTimeEntryRequest { StartTimeUtc = start, EndTimeUtc = end }, default);

        var document = await CreateService(db).ExportAsync(userId, new CsvExportRequest(null, null, null, null), default);
        var records = DecodeRecords(document.Content);

        Assert.Equal("2026-03-08,2026-03-08 01:30:00,2026-03-08 03:30:00,01:00:00,,Uncategorized,,America/New_York", records[1]);
    }
}
