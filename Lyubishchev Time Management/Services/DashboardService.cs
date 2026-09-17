using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record DashboardResult(bool Succeeded, DashboardResponse? Response, string? ErrorCode, string? ErrorMessage)
{
    public static DashboardResult Ok(DashboardResponse response) => new(true, response, null, null);

    public static DashboardResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed class DashboardService(AppDbContext dbContext, TimeAggregationService aggregationService)
{
    private const int RecentEntryCount = 5;
    private const string InvalidRangePresetMessage = "請提供有效的 preset（today、week、month）或完整的自訂日期範圍。";
    private const string InvalidDateRangeMessage = "結束日期不可早於開始日期。";

    private enum RangeMode { Today, Week, Month, Custom }

    public async Task<DashboardResult> GetAsync(ulong userId, string? preset, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .SingleAsync(cancellationToken);

        RangeMode mode;
        LocalDateRange currentRange;

        if (startDate is not null && endDate is not null && string.IsNullOrWhiteSpace(preset))
        {
            if (endDate.Value < startDate.Value)
            {
                return DashboardResult.Fail("INVALID_DATE_RANGE", InvalidDateRangeMessage);
            }

            mode = RangeMode.Custom;
            currentRange = new LocalDateRange(startDate.Value, endDate.Value);
        }
        else if (startDate is null && endDate is null && TryParsePreset(preset, out mode))
        {
            currentRange = aggregationService.GetRangeForPreset(ToAggregationPreset(mode), timeZoneId);
        }
        else
        {
            return DashboardResult.Fail("INVALID_RANGE_PRESET", InvalidRangePresetMessage);
        }

        var comparisonRange = GetComparisonRange(currentRange, mode);

        var current = await aggregationService.AggregateAsync(userId, currentRange, timeZoneId, cancellationToken);
        var comparison = await aggregationService.AggregateAsync(userId, comparisonRange, timeZoneId, cancellationToken);

        var recentEntries = await dbContext.TimeEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.EndTimeUtc)
            // SQLite can't translate ORDER BY on a raw ulong column; long safely covers every
            // realistic TimeEntry.Id (same workaround as TimeEntryService.ListAsync).
            .ThenByDescending(e => (long)e.Id)
            .Take(RecentEntryCount)
            .Include(e => e.Category)
            .Include(e => e.TimeEntryTags)
            .ThenInclude(link => link.Tag)
            .ToListAsync(cancellationToken);

        return DashboardResult.Ok(new DashboardResponse(
            currentRange.StartDate,
            currentRange.EndDateInclusive,
            timeZoneId,
            current.TotalSeconds,
            comparison.TotalSeconds,
            current.TotalSeconds - comparison.TotalSeconds,
            current.DailyTotals,
            current.CategoryTotals,
            current.TagTotals,
            recentEntries.Select(MapToResponse).ToList()));
    }

    private static bool TryParsePreset(string? preset, out RangeMode mode)
    {
        switch (preset?.Trim().ToLowerInvariant())
        {
            case "today":
                mode = RangeMode.Today;
                return true;
            case "week":
                mode = RangeMode.Week;
                return true;
            case "month":
                mode = RangeMode.Month;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    private static DateRangePreset ToAggregationPreset(RangeMode mode) => mode switch
    {
        RangeMode.Today => DateRangePreset.Today,
        RangeMode.Week => DateRangePreset.ThisWeek,
        RangeMode.Month => DateRangePreset.ThisMonth,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    // The comparison period is always the immediately preceding, non-overlapping local range.
    // Today/Week/Custom use an equal day count, so "yesterday" / "previous week" / "same-length
    // prior window" fall out naturally. Month instead uses the previous calendar month: months
    // have varying lengths and users compare calendar months, not raw day counts.
    private static LocalDateRange GetComparisonRange(LocalDateRange current, RangeMode mode)
    {
        if (mode == RangeMode.Month)
        {
            var previousMonthEnd = current.StartDate.AddDays(-1);
            var previousMonthStart = new DateOnly(previousMonthEnd.Year, previousMonthEnd.Month, 1);
            return new LocalDateRange(previousMonthStart, previousMonthEnd);
        }

        var dayCount = current.EndDateInclusive.DayNumber - current.StartDate.DayNumber + 1;
        var comparisonEnd = current.StartDate.AddDays(-1);
        var comparisonStart = comparisonEnd.AddDays(-(dayCount - 1));
        return new LocalDateRange(comparisonStart, comparisonEnd);
    }

    private static TimeEntryResponse MapToResponse(TimeEntry entry) => new(
        entry.Id,
        entry.Name,
        entry.StartTimeUtc,
        entry.EndTimeUtc,
        entry.CategoryId,
        entry.Category?.Name,
        entry.Category?.Color,
        entry.TimeEntryTags.Select(link => link.Tag.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
        (long)(entry.EndTimeUtc - entry.StartTimeUtc).TotalSeconds);
}
