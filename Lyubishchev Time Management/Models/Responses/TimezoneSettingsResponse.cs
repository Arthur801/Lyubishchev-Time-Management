using Lyubishchev_Time_Management.Infrastructure.Time;

namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record TimezoneSettingsResponse(string TimeZoneId, IReadOnlyList<TimeZoneOption> Options);
