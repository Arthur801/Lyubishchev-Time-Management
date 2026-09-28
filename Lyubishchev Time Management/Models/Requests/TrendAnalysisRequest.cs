namespace Lyubishchev_Time_Management.Models.Requests;

public sealed class TrendAnalysisRequest
{
    public string? Preset { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}
