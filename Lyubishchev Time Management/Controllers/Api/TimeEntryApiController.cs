using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers.Api;

[Authorize]
[Route("api/time-entries")]
public sealed class TimeEntryApiController(TimeEntryService timeEntryService, CsvExportService csvExportService, CurrentUserService currentUserService) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] ulong? categoryId,
        [FromQuery] string? search,
        CancellationToken cancellationToken = default)
    {
        if (startUtc is not null && endUtc is not null && endUtc <= startUtc)
        {
            return Problem(detail: "結束時間必須晚於開始時間。", statusCode: StatusCodes.Status400BadRequest, title: "INVALID_TIME_RANGE");
        }

        var document = await csvExportService.ExportAsync(
            currentUserService.GetRequiredUserId(),
            new CsvExportRequest(startUtc, endUtc, categoryId, search),
            cancellationToken);

        return File(document.Content, "text/csv; charset=utf-8", document.FileName);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] ulong? categoryId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (startUtc is not null && endUtc is not null && endUtc <= startUtc)
        {
            return Problem(detail: "結束時間必須晚於開始時間。", statusCode: StatusCodes.Status400BadRequest, title: "INVALID_TIME_RANGE");
        }

        var result = await timeEntryService.ListAsync(
            currentUserService.GetRequiredUserId(), startUtc, endUtc, categoryId, search, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateTimeEntryRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await timeEntryService.CreateAsync(currentUserService.GetRequiredUserId(), request, cancellationToken);
        return result.Succeeded ? Ok(result.Entry) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpPatch("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(ulong id, [FromBody] UpdateTimeEntryRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await timeEntryService.UpdateAsync(currentUserService.GetRequiredUserId(), id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Entry) : MapFailure(result.ErrorCode!, result.ErrorMessage!);
    }

    [HttpDelete("{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(ulong id, CancellationToken cancellationToken)
    {
        var result = await timeEntryService.DeleteAsync(currentUserService.GetRequiredUserId(), id, cancellationToken);
        return result.Succeeded
            ? NoContent()
            : Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status404NotFound, title: result.ErrorCode);
    }

    private IActionResult MapFailure(string errorCode, string errorMessage) => errorCode switch
    {
        "INVALID_TIME_RANGE" => Problem(detail: errorMessage, statusCode: StatusCodes.Status400BadRequest, title: errorCode),
        "CATEGORY_NOT_FOUND" or "TIME_ENTRY_NOT_FOUND" => Problem(detail: errorMessage, statusCode: StatusCodes.Status404NotFound, title: errorCode),
        _ => Problem(detail: errorMessage, statusCode: StatusCodes.Status500InternalServerError, title: errorCode),
    };
}
