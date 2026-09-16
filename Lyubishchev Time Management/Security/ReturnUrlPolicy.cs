namespace Lyubishchev_Time_Management.Security;

public static class ReturnUrlPolicy
{
    public static bool IsSafeLocalUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//")
            && !returnUrl.StartsWith("/\\");
    }
}
