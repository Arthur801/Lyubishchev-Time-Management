using Lyubishchev_Time_Management.Models.Requests;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyubishchev_Time_Management.Controllers;

public sealed class AccountController(AuthService authService) : Controller
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, [FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await authService.LoginAsync(request.Email, request.Password, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status401Unauthorized, title: result.ErrorCode);
        }

        SetAuthCookie(result.Token!);

        var redirectUrl = ReturnUrlPolicy.IsSafeLocalUrl(returnUrl) ? returnUrl! : "/Dashboard";
        return Ok(new { redirectUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await authService.RegisterAsync(request.Email, request.Password, cancellationToken);
        if (!result.Succeeded)
        {
            return Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status409Conflict, title: result.ErrorCode);
        }

        SetAuthCookie(result.Token!);
        return Ok(new { redirectUrl = "/Dashboard" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthConstants.CookieName);
        return RedirectToAction(nameof(Login));
    }

    private void SetAuthCookie(string token)
    {
        Response.Cookies.Append(AuthConstants.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(AuthConstants.CookieExpirationHours),
        });
    }
}
