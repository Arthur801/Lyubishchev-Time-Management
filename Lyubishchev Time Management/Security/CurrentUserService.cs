using System.Security.Claims;

namespace Lyubishchev_Time_Management.Security;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor)
{
    public ulong GetRequiredUserId()
    {
        var subject = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
        return ulong.TryParse(subject, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}
