using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class CategoryRequest
{
    [Required, StringLength(100)]
    public string? Name { get; set; }

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public string? Color { get; set; }
}
