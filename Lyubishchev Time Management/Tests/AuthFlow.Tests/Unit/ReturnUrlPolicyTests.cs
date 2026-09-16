using Lyubishchev_Time_Management.Security;
using Xunit;

namespace AuthFlow.Tests.Unit;

public sealed class ReturnUrlPolicyTests
{
    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/History?range=today")]
    public void IsSafeLocalUrl_accepts_application_relative_urls(string returnUrl)
    {
        Assert.True(ReturnUrlPolicy.IsSafeLocalUrl(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("Dashboard")]
    public void IsSafeLocalUrl_rejects_missing_or_non_local_urls(string? returnUrl)
    {
        Assert.False(ReturnUrlPolicy.IsSafeLocalUrl(returnUrl));
    }
}
