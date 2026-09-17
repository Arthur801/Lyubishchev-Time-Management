using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/settings")]
public sealed class SettingsApiController(UserSettingsService settingsService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("timezone")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await settingsService.GetAsync(currentUserService.GetRequiredUserId(), cancellationToken));

    [HttpPatch("timezone")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromBody] UpdateTimezoneRequest request, CancellationToken cancellationToken)
    {
        var result = await settingsService.UpdateAsync(currentUserService.GetRequiredUserId(), request.TimeZoneId, cancellationToken);
        return result.Succeeded ? Ok(result.Settings) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "INVALID_TIME_ZONE" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "USER_NOT_FOUND" => Problem(detail: errorMessage, statusCode: StatusCodes.Status404NotFound, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
