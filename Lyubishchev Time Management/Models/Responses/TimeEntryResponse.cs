namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record TimeEntryResponse(
    ulong Id,
    string? Name,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    ulong? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    IReadOnlyList<string> Tags,
    long DurationSeconds);
