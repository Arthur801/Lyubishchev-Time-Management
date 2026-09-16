namespace Lyubishchev_Time_Management.Models.Entities;

public class Category
{
    public ulong Id { get; set; }

    public ulong UserId { get; set; }

    public required string Name { get; set; }

    public required string Color { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public required User User { get; set; }

    public ICollection<TimeEntry> TimeEntries { get; } = new List<TimeEntry>();
}
