namespace Lyubishchev_Time_Management.Services;

public sealed class AiTrendAnalysisOptions
{
    public const string SectionName = "AiTrendAnalysis";

    public string BaseUrl { get; init; } = "http://127.0.0.1:8011/";

    public string InternalToken { get; init; } = string.Empty;

    public int RequestTimeoutSeconds { get; init; } = 20;
}
