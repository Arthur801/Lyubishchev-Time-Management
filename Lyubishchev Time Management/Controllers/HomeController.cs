using Lyubishchev_Time_Management.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Lyubishchev_Time_Management.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            return RedirectToAction("Login", "Account");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // GlobalExceptionHandler stashes the trace ID it already logged under before
            // re-executing this action; fall back to Activity/TraceIdentifier only for a direct
            // hit on this route (e.g. manual navigation) where no exception actually occurred.
            var requestId = HttpContext.Items["TraceId"] as string ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return View(new ErrorViewModel { RequestId = requestId });
        }
    }
}
