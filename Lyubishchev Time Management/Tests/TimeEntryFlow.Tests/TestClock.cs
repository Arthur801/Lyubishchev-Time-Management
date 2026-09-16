using Lyubishchev_Time_Management.Infrastructure.Clock;

namespace TimeEntryFlow.Tests;

internal sealed class TestClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
