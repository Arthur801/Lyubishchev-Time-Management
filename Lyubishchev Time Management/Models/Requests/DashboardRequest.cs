namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class DashboardRequest
{
    public string? Preset { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}
