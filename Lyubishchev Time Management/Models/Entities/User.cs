namespace Lyubishchev_Time_Management.Models.Entities;

public class User
{
    public ulong Id { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public required string TimeZoneId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public RunningTimer? RunningTimer { get; set; }

    public ICollection<TimeEntry> TimeEntries { get; } = new List<TimeEntry>();

    public ICollection<Category> Categories { get; } = new List<Category>();

    public ICollection<Tag> Tags { get; } = new List<Tag>();
}
