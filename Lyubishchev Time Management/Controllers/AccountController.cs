using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }
}
