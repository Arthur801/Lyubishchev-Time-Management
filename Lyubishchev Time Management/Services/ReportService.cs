using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.EntityFrameworkCore;

namespace Lyubishchev_Time_Management.Services;

public sealed record CategoryReportResult(bool Succeeded, CategoryReportResponse? Response, string? ErrorCode, string? ErrorMessage)
{
    public static CategoryReportResult Ok(CategoryReportResponse response) => new(true, response, null, null);

    public static CategoryReportResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

public sealed record TagReportResult(bool Succeeded, TagReportResponse? Response, string? ErrorCode, string? ErrorMessage)
{
    public static TagReportResult Ok(TagReportResponse response) => new(true, response, null, null);

    public static TagReportResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}

// ReportService only consumes TimeAggregationService's output for the resolved range -- it must
// never recompute timezone conversion, interval intersection, or Category/Tag bucketing itself
// (that logic lives once, shared with DashboardService).
public sealed class ReportService(AppDbContext dbContext, TimeAggregationService aggregationService)
{
    private const string InvalidRangePresetMessage = "請提供有效的 preset（today、week、month）或完整的自訂日期範圍。";
    private const string InvalidDateRangeMessage = "結束日期不可早於開始日期。";

    public async Task<CategoryReportResult> GetCategoryAsync(ulong userId, string? preset, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var range = await ResolveRangeAsync(userId, preset, startDate, endDate, cancellationToken);
        if (!range.Succeeded)
        {
            return CategoryReportResult.Fail(range.ErrorCode!, range.ErrorMessage!);
        }

        var (localRange, timeZoneId) = (range.Range!, range.TimeZoneId!);
        var aggregation = await aggregationService.AggregateAsync(userId, localRange, timeZoneId, cancellationToken);
        return CategoryReportResult.Ok(new CategoryReportResponse(
            localRange.StartDate,
            localRange.EndDateInclusive,
            timeZoneId,
            aggregation.TotalSeconds,
            aggregation.CategoryTotals));
    }

    public async Task<TagReportResult> GetTagAsync(ulong userId, string? preset, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var range = await ResolveRangeAsync(userId, preset, startDate, endDate, cancellationToken);
        if (!range.Succeeded)
        {
            return TagReportResult.Fail(range.ErrorCode!, range.ErrorMessage!);
        }

        var (localRange, timeZoneId) = (range.Range!, range.TimeZoneId!);
        var aggregation = await aggregationService.AggregateAsync(userId, localRange, timeZoneId, cancellationToken);
        return TagReportResult.Ok(new TagReportResponse(
            localRange.StartDate,
            localRange.EndDateInclusive,
            timeZoneId,
            aggregation.TotalSeconds,
            aggregation.TagTotals));
    }

    private readonly record struct RangeResolution(bool Succeeded, LocalDateRange? Range, string? TimeZoneId, string? ErrorCode, string? ErrorMessage)
    {
        public static RangeResolution Ok(LocalDateRange range, string timeZoneId) => new(true, range, timeZoneId, null, null);

        public static RangeResolution Fail(string errorCode, string errorMessage) => new(false, null, null, errorCode, errorMessage);
    }

    // Unlike Dashboard, Report defaults to "month" when neither a preset nor custom dates are
    // supplied -- everything else (mutual exclusivity, invalid preset/date validation) matches
    // DashboardService's rule.
    private async Task<RangeResolution> ResolveRangeAsync(ulong userId, string? preset, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .SingleAsync(cancellationToken);

        if (startDate is not null && endDate is not null && string.IsNullOrWhiteSpace(preset))
        {
            if (endDate.Value < startDate.Value)
            {
                return RangeResolution.Fail("INVALID_DATE_RANGE", InvalidDateRangeMessage);
            }

            return RangeResolution.Ok(new LocalDateRange(startDate.Value, endDate.Value), timeZoneId);
        }

        if (startDate is null && endDate is null)
        {
            var effectivePreset = string.IsNullOrWhiteSpace(preset) ? "month" : preset;
            if (!TryParsePreset(effectivePreset, out var aggregationPreset))
            {
                return RangeResolution.Fail("INVALID_RANGE_PRESET", InvalidRangePresetMessage);
            }

            return RangeResolution.Ok(aggregationService.GetRangeForPreset(aggregationPreset, timeZoneId), timeZoneId);
        }

        return RangeResolution.Fail("INVALID_RANGE_PRESET", InvalidRangePresetMessage);
    }

    private static bool TryParsePreset(string preset, out DateRangePreset result)
    {
        switch (preset.Trim().ToLowerInvariant())
        {
            case "today":
                result = DateRangePreset.Today;
                return true;
            case "week":
                result = DateRangePreset.ThisWeek;
                return true;
            case "month":
                result = DateRangePreset.ThisMonth;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
