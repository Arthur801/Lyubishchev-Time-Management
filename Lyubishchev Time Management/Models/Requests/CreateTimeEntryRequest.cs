using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class CreateTimeEntryRequest
{
    [StringLength(500)]
    public string? Name { get; set; }

    [Required(ErrorMessage = "請提供開始時間。")]
    public DateTime? StartTimeUtc { get; set; }

    [Required(ErrorMessage = "請提供結束時間。")]
    public DateTime? EndTimeUtc { get; set; }

    public ulong? CategoryId { get; set; }

    public List<string>? Tags { get; set; }
}
