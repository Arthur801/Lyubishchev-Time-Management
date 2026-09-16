namespace Lyubishchev_Time_Management.Models.Entities;

public class TimeEntryTag
{
    public ulong TimeEntryId { get; set; }

    public ulong TagId { get; set; }

    public required TimeEntry TimeEntry { get; set; }

    public required Tag Tag { get; set; }
}
