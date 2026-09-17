namespace Lyubishchev_Time_Management.Infrastructure.Time;

public sealed record TimeZoneOption(string Id, string DisplayName);

public sealed class TimeZoneCatalog
{
    public static readonly IReadOnlyList<TimeZoneOption> Options =
    [
        new("UTC", "協調世界時間 (UTC)"),
        new("Asia/Taipei", "台北 (UTC+08:00)"),
        new("Asia/Tokyo", "東京 (UTC+09:00)"),
        new("Asia/Singapore", "新加坡 (UTC+08:00)"),
        new("Asia/Hong_Kong", "香港 (UTC+08:00)"),
        new("Europe/London", "倫敦"),
        new("Europe/Paris", "巴黎"),
        new("America/New_York", "紐約"),
        new("America/Chicago", "芝加哥"),
        new("America/Denver", "丹佛"),
        new("America/Los_Angeles", "洛杉磯"),
        new("Australia/Sydney", "雪梨"),
        new("Pacific/Auckland", "奧克蘭"),
    ];

    public bool IsSupported(string? id) => !string.IsNullOrEmpty(id) && Options.Any(option => option.Id == id);

    // Callers must have already gone through IsSupported for user-facing validation (e.g. the
    // settings update flow); this fallback only guards reads of stored data that may predate the
    // current whitelist or have been corrupted by manual repair, so it never throws.
    public TimeZoneInfo ResolveOrUtc(string? id) => IsSupported(id) ? TimeZoneInfo.FindSystemTimeZoneById(id!) : TimeZoneInfo.Utc;
}
