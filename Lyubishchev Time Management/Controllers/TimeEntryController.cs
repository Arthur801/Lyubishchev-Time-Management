using Lyubishchev_Time_Management.Models.ViewModels;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

[Authorize]
public sealed class TimeEntryController(TimeEntryService timeEntryService, CurrentUserService currentUserService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await timeEntryService.GetCategoryOptionsAsync(currentUserService.GetRequiredUserId(), cancellationToken);
        return View(new HistoryViewModel(categories));
    }
}
