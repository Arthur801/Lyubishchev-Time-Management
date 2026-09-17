namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class UpdateTimezoneRequest
{
    // Intentionally unannotated: missing, blank and unsupported IDs must all surface as the same
    // INVALID_TIME_ZONE error code, so UserSettingsService owns the full validation itself instead
    // of splitting it between ModelState and the service.
    public string? TimeZoneId { get; set; }
}
