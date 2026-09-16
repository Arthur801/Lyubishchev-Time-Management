using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "請輸入電子郵件。")]
    [EmailAddress(ErrorMessage = "請輸入有效的電子郵件。")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "請輸入密碼。")]
    public string Password { get; set; } = string.Empty;
}
