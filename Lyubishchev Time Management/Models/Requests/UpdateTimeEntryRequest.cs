using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class UpdateTimeEntryRequest
{
    [StringLength(500)]
    public string? Name { get; set; }

    public DateTime? StartTimeUtc { get; set; }

    public DateTime? EndTimeUtc { get; set; }

    public ulong? CategoryId { get; set; }

    public List<string>? Tags { get; set; }
}
