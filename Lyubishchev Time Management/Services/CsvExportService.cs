using System.Globalization;
using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Csv;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record CsvExportRequest(
    DateTime? StartUtc,
    DateTime? EndUtc,
    ulong? CategoryId,
    string? Search);

public sealed record CsvExportDocument(byte[] Content, string FileName);

// Exports full, unclipped TimeEntry detail rows — never TimeAggregationService, which intersects
// entries against a range for statistics and would silently truncate an exported row's real
// start/end/duration.
public sealed class CsvExportService(
    AppDbContext dbContext,
    UserSettingsService userSettingsService,
    TimeZoneCatalog timeZoneCatalog,
    IOperationalEventLogger operationalEventLogger)
{
    // The design doc specifies this literal English fallback for the CSV's Category column
    // (distinct from the app UI's own "未分類" label used elsewhere, e.g. TimeAggregationService).
    private const string UncategorizedName = "Uncategorized";

    private static readonly string[] Headers = ["Date", "Start Time", "End Time", "Duration", "Name", "Category", "Tags", "Time Zone"];

    public async Task<CsvExportDocument> ExportAsync(ulong userId, CsvExportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await userSettingsService.GetAsync(userId, cancellationToken);
            var timeZone = timeZoneCatalog.ResolveOrUtc(settings.TimeZoneId);

            var query = dbContext.TimeEntries.AsNoTracking().Where(e => e.UserId == userId);

            if (request.StartUtc is not null)
            {
                query = query.Where(e => e.EndTimeUtc > request.StartUtc.Value);
            }

            if (request.EndUtc is not null)
            {
                query = query.Where(e => e.StartTimeUtc < request.EndUtc.Value);
            }

            if (request.CategoryId is not null)
            {
                query = query.Where(e => e.CategoryId == request.CategoryId.Value);
            }

            var term = request.Search?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                query = query.Where(e =>
                    EF.Functions.Like(e.Name ?? "", $"%{term}%") ||
                    e.TimeEntryTags.Any(link => EF.Functions.Like(link.Tag.Name, $"%{term}%")));
            }

            var entries = await query
                .OrderByDescending(e => e.StartTimeUtc)
                // Same SQLite-provider workaround as TimeEntryService.ListAsync/DashboardService: it
                // refuses to translate ORDER BY on a raw ulong column. No Skip/Take — export is
                // intentionally unpaginated.
                .ThenByDescending(e => (long)e.Id)
                .Include(e => e.Category)
                .Include(e => e.TimeEntryTags)
                .ThenInclude(link => link.Tag)
                .ToListAsync(cancellationToken);

            var rows = entries.Select(entry => BuildRow(entry, timeZone, settings.TimeZoneId));
            var content = CsvWriter.Write(Headers, rows);
            var fileName = BuildFileName(request.StartUtc, request.EndUtc, timeZone);

            return new CsvExportDocument(content, fileName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Cancellation (client disconnect, navigated away) is not an application error and
            // must not be logged as one; everything else here is unexpected (DB failure, bad
            // stored time zone, etc.) and worth an operator-visible record before it rethrows for
            // the global exception handler to turn into a safe HTTP 500.
            operationalEventLogger.CsvExportFailed(
                userId,
                request.StartUtc is not null || request.EndUtc is not null,
                request.CategoryId is not null,
                !string.IsNullOrWhiteSpace(request.Search),
                exception);
            throw;
        }
    }

    private static string[] BuildRow(TimeEntry entry, TimeZoneInfo timeZone, string timeZoneId)
    {
        var duration = entry.EndTimeUtc - entry.StartTimeUtc;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(entry.StartTimeUtc, timeZone);
        var tags = string.Join("; ", entry.TimeEntryTags.Select(link => link.Tag.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        return
        [
            localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FormatDateTime(entry.StartTimeUtc, timeZone),
            FormatDateTime(entry.EndTimeUtc, timeZone),
            FormatDuration(duration),
            entry.Name ?? string.Empty,
            entry.Category?.Name ?? UncategorizedName,
            tags,
            timeZoneId,
        ];
    }

    private static string FormatDateTime(DateTime utc, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration)
        => $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    private static string BuildFileName(DateTime? startUtc, DateTime? endUtc, TimeZoneInfo timeZone)
    {
        if (startUtc is null || endUtc is null)
        {
            return "time-entries-all.csv";
        }

        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc.Value, timeZone));
        // endUtc is an exclusive half-open bound; back off one tick so the file name reflects the
        // selected range's last *inclusive* local day, not the following day.
        var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc.Value.AddTicks(-1), timeZone));
        return $"time-entries-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv";
    }
}
