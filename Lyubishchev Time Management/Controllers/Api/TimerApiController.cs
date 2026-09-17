using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/timer")]
public sealed class TimerApiController(TimerService timerService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var status = await timerService.GetStatusAsync(currentUserService.GetRequiredUserId(), cancellationToken);
        return Ok(status);
    }

    [HttpPost("start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var result = await timerService.StartAsync(currentUserService.GetRequiredUserId(), cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status409Conflict, title: result.ErrorCode);
        }

        return Ok(result.Timer);
    }

    [HttpPost("stop")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop([FromBody] StopTimerRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await timerService.StopAsync(
            currentUserService.GetRequiredUserId(), request.Name, request.CategoryId, request.Tags, cancellationToken);
        return result.Succeeded ? Ok(result.TimeEntry) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "TIMER_NOT_RUNNING" => Problem(detail: errorMessage, statusCode: StatusCodes.Status404NotFound, title: errorCode),
        "CATEGORY_NOT_FOUND" => Problem(detail: errorMessage, statusCode: StatusCodes.Status404NotFound, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
