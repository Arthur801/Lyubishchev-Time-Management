namespace Lyubishchev_Time_Management.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
