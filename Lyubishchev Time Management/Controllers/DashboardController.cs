using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

public sealed class DashboardController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
