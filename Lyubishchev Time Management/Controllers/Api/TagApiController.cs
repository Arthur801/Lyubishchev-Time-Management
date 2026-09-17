using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/tags")]
public sealed class TagApiController(TagService tagService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await tagService.ListAsync(currentUserService.GetRequiredUserId(), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] TagRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await tagService.CreateAsync(currentUserService.GetRequiredUserId(), request, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(List), null, result.Tag)
            : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpPatch("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(ulong id, [FromBody] TagRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await tagService.UpdateAsync(currentUserService.GetRequiredUserId(), id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Tag) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpDelete("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken cancellationToken)
    {
        var result = await tagService.DeleteAsync(currentUserService.GetRequiredUserId(), id, cancellationToken);
        return result.Succeeded
            ? NoContent()
            : Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status404NotFound, title: result.ErrorCode);
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "INVALID_NAME" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "TAG_NAME_CONFLICT" => Problem(detail: errorMessage, statusCode: StatusCodes.Status409Conflict, title: errorCode),
        "TAG_NOT_FOUND" => Problem(detail: errorMessage, statusCode: StatusCodes.Status404NotFound, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
