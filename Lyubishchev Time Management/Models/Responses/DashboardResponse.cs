namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record DashboardResponse(
    DateOnly StartDate,
    DateOnly EndDateInclusive,
    string TimeZoneId,
    long TotalSeconds,
    long ComparisonSeconds,
    long ChangeSeconds,
    IReadOnlyList<DailyTotal> DailyTotals,
    IReadOnlyList<CategoryTotal> CategoryTotals,
    IReadOnlyList<TagTotal> TagTotals,
    IReadOnlyList<TimeEntryResponse> RecentEntries);
