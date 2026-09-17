using Lyubishchev_Time_Management.Infrastructure.Time;
using Xunit;

namespace TimeEntryFlow.Tests.Unit;

public sealed class TimeZoneCatalogTests
{
    private readonly TimeZoneCatalog catalog = new();

    [Theory]
    [InlineData("Asia/Taipei")]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public void IsSupported_accepts_listed_ids(string id)
    {
        Assert.True(catalog.IsSupported(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("asia/taipei")]
    public void IsSupported_rejects_missing_blank_or_unlisted_ids(string? id)
    {
        Assert.False(catalog.IsSupported(id));
    }

    [Theory]
    [InlineData("Asia/Taipei")]
    [InlineData("America/New_York")]
    public void ResolveOrUtc_resolves_a_real_TimeZoneInfo_for_listed_ids(string id)
    {
        var resolved = catalog.ResolveOrUtc(id);

        Assert.Equal(id, resolved.Id);
    }

    [Fact]
    public void ResolveOrUtc_falls_back_to_UTC_for_an_unsupported_id()
    {
        var resolved = catalog.ResolveOrUtc("Mars/Olympus_Mons");

        Assert.Equal(TimeZoneInfo.Utc, resolved);
    }

    [Fact]
    public void Options_contains_every_documented_timezone_exactly_once()
    {
        var ids = TimeZoneCatalog.Options.Select(o => o.Id).ToList();

        Assert.Equal(13, ids.Count);
        Assert.Equal(ids.Distinct(), ids);
    }
}
