using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/reports")]
public sealed class ReportApiController(ReportService reportService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("category")]
    public async Task<IActionResult> Category([FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        var result = await reportService.GetCategoryAsync(
            currentUserService.GetRequiredUserId(), request.Preset, request.StartDate, request.EndDate, cancellationToken);
        return result.Succeeded ? Ok(result.Response) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpGet("tag")]
    public async Task<IActionResult> Tag([FromQuery] ReportRequest request, CancellationToken cancellationToken)
    {
        var result = await reportService.GetTagAsync(
            currentUserService.GetRequiredUserId(), request.Preset, request.StartDate, request.EndDate, cancellationToken);
        return result.Succeeded ? Ok(result.Response) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "INVALID_DATE_RANGE" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "INVALID_RANGE_PRESET" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
