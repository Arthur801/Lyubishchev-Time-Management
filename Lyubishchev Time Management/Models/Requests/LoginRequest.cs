using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "請輸入電子郵件。")]
    [EmailAddress(ErrorMessage = "請輸入有效的電子郵件。")]
    [MaxLength(320, ErrorMessage = "電子郵件長度不可超過 320 個字元。")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "請輸入密碼。")]
    [MaxLength(200, ErrorMessage = "密碼長度不可超過 200 個字元。")]
    public string Password { get; set; } = string.Empty;
}
