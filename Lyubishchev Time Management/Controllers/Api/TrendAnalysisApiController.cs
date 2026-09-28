using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[EnableRateLimiting(RateLimiterPolicies.AiTrendAnalysis)]
[Route("api/reports/trend-analysis")]
public sealed class TrendAnalysisApiController(TrendAnalysisService trendAnalysisService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze([FromBody] TrendAnalysisRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await trendAnalysisService.AnalyzeAsync(
                currentUserService.GetRequiredUserId(), request.Preset, request.StartDate, request.EndDate, cancellationToken);
            return result.Succeeded ? Ok(result.Response) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
        }
        catch (TrendAnalysisUnavailableException exception)
        {
            return Problem(detail: exception.Message, statusCode: (int)exception.StatusCode, title: exception.ErrorCode);
        }
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "INVALID_DATE_RANGE" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "INVALID_RANGE_PRESET" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "AI_ANALYSIS_RANGE_TOO_LARGE" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
