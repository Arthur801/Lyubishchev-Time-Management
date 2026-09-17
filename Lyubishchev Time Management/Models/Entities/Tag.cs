namespace Lyubishchev_Time_Management.Models.Entities;

public class Tag
{
    public ulong Id { get; set; }

    public ulong UserId { get; set; }

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public required User User { get; set; }

    public ICollection<TimeEntryTag> TimeEntryTags { get; } = new List<TimeEntryTag>();
}
