namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record TrendAnalysisResponse(
    DateOnly StartDate,
    DateOnly EndDateInclusive,
    string TimeZoneId,
    TrendAnalysisContent Analysis);

public sealed record TrendAnalysisContent(
    string DataSufficiency,
    string Summary,
    IReadOnlyList<TrendObservation> Observations,
    IReadOnlyList<string> Suggestions,
    string Disclaimer);

public sealed record TrendObservation(string Claim, string Evidence);

public sealed record TrendAnalysisServiceRequest(
    TrendAnalysisRange Range,
    IReadOnlyList<DailyTotal> DailyTotals,
    IReadOnlyList<TrendNamedTotal> CategoryTotals,
    IReadOnlyList<TrendNamedTotal> TagTotals,
    TrendDerivedMetrics DerivedMetrics);

public sealed record TrendAnalysisRange(DateOnly StartDate, DateOnly EndDateInclusive, string TimeZoneId);

public sealed record TrendNamedTotal(string Name, long DurationSeconds);

public sealed record TrendDerivedMetrics(
    long TotalSeconds,
    int TrackedDayCount,
    int CalendarDayCount,
    long AverageSecondsPerTrackedDay,
    long FirstHalfSeconds,
    long SecondHalfSeconds);
