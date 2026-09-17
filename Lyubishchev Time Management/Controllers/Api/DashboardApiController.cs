using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/dashboard")]
public sealed class DashboardApiController(DashboardService dashboardService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DashboardRequest request, CancellationToken cancellationToken)
    {
        var result = await dashboardService.GetAsync(
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
