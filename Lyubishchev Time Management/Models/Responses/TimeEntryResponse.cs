namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record TimeEntryResponse(
    ulong Id,
    string? Name,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    ulong? CategoryId,
    long DurationSeconds);
