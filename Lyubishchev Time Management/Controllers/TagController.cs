using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

[Authorize]
public sealed class TagController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
