namespace Lyubishchev_Time_Management.Models.Responses;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
