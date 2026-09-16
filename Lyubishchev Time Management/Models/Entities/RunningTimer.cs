namespace Lyubishchev_Time_Management.Models.Entities;

public class RunningTimer
{
    public ulong UserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public required User User { get; set; }
}
