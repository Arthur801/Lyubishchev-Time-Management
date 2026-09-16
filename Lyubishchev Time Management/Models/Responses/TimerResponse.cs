namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record TimerResponse(bool IsRunning, DateTime? StartedAtUtc);
