namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record CategoryReportResponse(
    DateOnly StartDate,
    DateOnly EndDateInclusive,
    string TimeZoneId,
    long TotalSeconds,
    IReadOnlyList<CategoryTotal> CategoryTotals);

public sealed record TagReportResponse(
    DateOnly StartDate,
    DateOnly EndDateInclusive,
    string TimeZoneId,
    long TotalSeconds,
    IReadOnlyList<TagTotal> TagTotals);
