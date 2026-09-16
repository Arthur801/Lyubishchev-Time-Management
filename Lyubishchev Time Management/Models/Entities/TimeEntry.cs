namespace Lyubishchev_Time_Management.Models.Entities;

public class TimeEntry
{
    public ulong Id { get; set; }

    public ulong UserId { get; set; }

    public ulong? CategoryId { get; set; }

    public string? Name { get; set; }

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public required User User { get; set; }

    public Category? Category { get; set; }

    public ICollection<TimeEntryTag> TimeEntryTags { get; } = new List<TimeEntryTag>();
}
