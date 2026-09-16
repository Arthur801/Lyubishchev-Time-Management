using System.ComponentModel.DataAnnotations;

namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "請輸入電子郵件。")]
    [EmailAddress(ErrorMessage = "請輸入有效的電子郵件。")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "請輸入密碼。")]
    [MinLength(8, ErrorMessage = "密碼至少需要 8 個字元。")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "請再次輸入密碼。")]
    [Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼不一致。")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
