using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed class TimeAggregationService(AppDbContext dbContext, IClock clock, TimeZoneCatalog catalog)
{
    private const string UncategorizedName = "未分類";
    private const string UncategorizedColor = "#a6adb7";

    public LocalDateRange GetRangeForPreset(DateRangePreset preset, string timeZoneId)
    {
        var timeZone = catalog.ResolveOrUtc(timeZoneId);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, timeZone));

        return preset switch
        {
            DateRangePreset.Today => new LocalDateRange(localToday, localToday),
            DateRangePreset.ThisWeek => WeekRange(localToday),
            DateRangePreset.ThisMonth => MonthRange(localToday),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    public async Task<TimeAggregationResult> AggregateAsync(ulong userId, LocalDateRange range, string timeZoneId, CancellationToken cancellationToken)
    {
        if (range.EndDateInclusive < range.StartDate)
        {
            throw new ArgumentException("EndDateInclusive must not precede StartDate.", nameof(range));
        }

        var timeZone = catalog.ResolveOrUtc(timeZoneId);
        var rangeStartUtc = ConvertLocalMidnightToUtc(range.StartDate, timeZone);
        var rangeEndUtc = ConvertLocalMidnightToUtc(range.EndDateInclusive.AddDays(1), timeZone);

        var entries = await dbContext.TimeEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.StartTimeUtc < rangeEndUtc && e.EndTimeUtc > rangeStartUtc)
            .Include(e => e.Category)
            .Include(e => e.TimeEntryTags)
            .ThenInclude(link => link.Tag)
            .ToListAsync(cancellationToken);

        var dailyTotals = new SortedDictionary<DateOnly, long>();
        for (var date = range.StartDate; date <= range.EndDateInclusive; date = date.AddDays(1))
        {
            dailyTotals[date] = 0;
        }

        // Dictionary<TKey, TValue> disallows a null key even when TKey is a nullable value type,
        // so the Uncategorized bucket (CategoryId == null) is tracked separately instead of as a
        // null key in categoryTotals.
        var categoryTotals = new Dictionary<ulong, (string Name, string Color, long Seconds)>();
        long uncategorizedSeconds = 0;
        var tagTotals = new Dictionary<ulong, (string Name, long Seconds)>();
        long totalSeconds = 0;

        foreach (var entry in entries)
        {
            var overlapStart = entry.StartTimeUtc > rangeStartUtc ? entry.StartTimeUtc : rangeStartUtc;
            var overlapEnd = entry.EndTimeUtc < rangeEndUtc ? entry.EndTimeUtc : rangeEndUtc;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var seconds = (long)(overlapEnd - overlapStart).TotalSeconds;
            totalSeconds += seconds;

            SplitIntoLocalDays(dailyTotals, overlapStart, overlapEnd, timeZone);

            if (entry.CategoryId is { } categoryId)
            {
                categoryTotals[categoryId] = categoryTotals.TryGetValue(categoryId, out var existingCategory)
                    ? (entry.Category!.Name, entry.Category.Color, existingCategory.Seconds + seconds)
                    : (entry.Category!.Name, entry.Category.Color, seconds);
            }
            else
            {
                uncategorizedSeconds += seconds;
            }

            foreach (var link in entry.TimeEntryTags)
            {
                tagTotals[link.TagId] = tagTotals.TryGetValue(link.TagId, out var existingTag)
                    ? (link.Tag.Name, existingTag.Seconds + seconds)
                    : (link.Tag.Name, seconds);
            }
        }

        var categoryTotalList = categoryTotals
            .Select(kv => new CategoryTotal(kv.Key, kv.Value.Name, kv.Value.Color, kv.Value.Seconds))
            .ToList();
        if (uncategorizedSeconds > 0)
        {
            categoryTotalList.Add(new CategoryTotal(null, UncategorizedName, UncategorizedColor, uncategorizedSeconds));
        }

        return new TimeAggregationResult(
            rangeStartUtc,
            rangeEndUtc,
            timeZoneId,
            totalSeconds,
            dailyTotals.Select(kv => new DailyTotal(kv.Key, kv.Value)).ToList(),
            categoryTotalList
                .OrderByDescending(c => c.DurationSeconds)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            tagTotals
                .Select(kv => new TagTotal(kv.Key, kv.Value.Name, kv.Value.Seconds))
                .OrderByDescending(t => t.DurationSeconds)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static LocalDateRange WeekRange(DateOnly today)
    {
        var start = today.AddDays(-(int)today.DayOfWeek);
        return new LocalDateRange(start, start.AddDays(6));
    }

    private static LocalDateRange MonthRange(DateOnly today)
    {
        var start = new DateOnly(today.Year, today.Month, 1);
        return new LocalDateRange(start, start.AddMonths(1).AddDays(-1));
    }

    // Walks the UTC interval [overlapStartUtc, overlapEndUtc) day by day in the user's local time
    // zone, crediting each local calendar day only the slice that actually falls on it.
    private static void SplitIntoLocalDays(IDictionary<DateOnly, long> dailyTotals, DateTime overlapStartUtc, DateTime overlapEndUtc, TimeZoneInfo timeZone)
    {
        var cursor = overlapStartUtc;
        while (cursor < overlapEndUtc)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(cursor, timeZone));
            var nextLocalMidnightUtc = ConvertLocalMidnightToUtc(localDate.AddDays(1), timeZone);
            var segmentEnd = nextLocalMidnightUtc < overlapEndUtc ? nextLocalMidnightUtc : overlapEndUtc;

            if (dailyTotals.ContainsKey(localDate))
            {
                dailyTotals[localDate] += (long)(segmentEnd - cursor).TotalSeconds;
            }

            cursor = segmentEnd;
        }
    }

    // Converts a local calendar midnight to its UTC instant. A spring-forward gap has no such
    // local time, so this searches forward minute-by-minute for the first valid one (the gap is
    // always a small, bounded number of minutes). A fall-back overlap makes the local time
    // ambiguous; this always resolves to the earlier UTC instant (the DST-side offset), so day
    // ranges stay contiguous and non-overlapping.
    private static DateTime ConvertLocalMidnightToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            while (timeZone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }

            return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var earlierOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - earlierOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }
}
