namespace Lyubishchev_Time_Management.Models.Responses;

public enum DateRangePreset
{
    Today,
    ThisWeek,
    ThisMonth,
}

public sealed record LocalDateRange(DateOnly StartDate, DateOnly EndDateInclusive);

public sealed record DailyTotal(DateOnly Date, long DurationSeconds);

public sealed record CategoryTotal(ulong? CategoryId, string Name, string Color, long DurationSeconds);

public sealed record TagTotal(ulong TagId, string Name, long DurationSeconds);

public sealed record TimeAggregationResult(
    DateTime RangeStartUtc,
    DateTime RangeEndUtc,
    string TimeZoneId,
    long TotalSeconds,
    IReadOnlyList<DailyTotal> DailyTotals,
    IReadOnlyList<CategoryTotal> CategoryTotals,
    IReadOnlyList<TagTotal> TagTotals);
