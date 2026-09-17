using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class TagRequest
{
    [Required, StringLength(100)]
    public string? Name { get; set; }
}
