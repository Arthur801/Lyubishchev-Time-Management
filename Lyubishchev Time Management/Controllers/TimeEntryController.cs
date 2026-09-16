using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

[Authorize]
public sealed class TimeEntryController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
