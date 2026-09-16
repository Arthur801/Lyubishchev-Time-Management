namespace Lyubishchev_Time_Management.Infrastructure.Clock;

public interface IClock
{
    DateTime UtcNow { get; }
}
