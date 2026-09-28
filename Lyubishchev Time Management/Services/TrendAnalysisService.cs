using Lyubishchev_Time_Management.Models.Responses;

namespace Lyubishchev_Time_Management.Services;

public sealed class TrendAnalysisService(ReportService reportService, TrendAnalysisClient client)
{
    public async Task<TrendAnalysisApplicationResult> AnalyzeAsync(
        ulong userId,
        string? preset,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var input = await reportService.GetTrendAnalysisInputAsync(userId, preset, startDate, endDate, cancellationToken);
        if (!input.Succeeded)
        {
            return TrendAnalysisApplicationResult.Fail(input.ErrorCode!, input.ErrorMessage!);
        }

        var analysis = await client.AnalyzeAsync(input.Request!, cancellationToken);
        return TrendAnalysisApplicationResult.Ok(new TrendAnalysisResponse(input.StartDate, input.EndDateInclusive, input.TimeZoneId!, analysis));
    }
}

public sealed record TrendAnalysisApplicationResult(bool Succeeded, TrendAnalysisResponse? Response, string? ErrorCode, string? ErrorMessage)
{
    public static TrendAnalysisApplicationResult Ok(TrendAnalysisResponse response) => new(true, response, null, null);

    public static TrendAnalysisApplicationResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}
