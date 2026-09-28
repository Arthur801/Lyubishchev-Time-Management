using System.Net;
using System.Net.Http.Json;
using Lyubishchev_Time_Management.Models.Responses;
using Microsoft.Extensions.Options;

namespace Lyubishchev_Time_Management.Services;

public sealed class TrendAnalysisClient(
    HttpClient httpClient,
    IOptions<AiTrendAnalysisOptions> options,
    ILogger<TrendAnalysisClient> logger)
{
    private readonly AiTrendAnalysisOptions _options = options.Value;

    public async Task<TrendAnalysisContent> AnalyzeAsync(TrendAnalysisServiceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.InternalToken))
        {
            logger.LogWarning("AI trend analysis was requested but the internal service token is not configured.");
            throw new TrendAnalysisUnavailableException("AI_ANALYSIS_UNAVAILABLE", "時間趨勢分析目前尚未設定。", HttpStatusCode.ServiceUnavailable);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "analyze-time-trend")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("X-AI-Internal-Token", _options.InternalToken);

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var analysis = await response.Content.ReadFromJsonAsync<TrendAnalysisContent>(cancellationToken);
                return analysis ?? throw new TrendAnalysisUnavailableException("AI_ANALYSIS_UNAVAILABLE", "時間趨勢分析暫時無法使用。", HttpStatusCode.ServiceUnavailable);
            }

            var status = response.StatusCode == HttpStatusCode.TooManyRequests
                ? HttpStatusCode.TooManyRequests
                : response.StatusCode == HttpStatusCode.GatewayTimeout
                    ? HttpStatusCode.GatewayTimeout
                    : HttpStatusCode.ServiceUnavailable;
            var errorCode = status == HttpStatusCode.TooManyRequests
                ? "AI_ANALYSIS_RATE_LIMITED"
                : status == HttpStatusCode.GatewayTimeout
                    ? "AI_ANALYSIS_TIMEOUT"
                    : "AI_ANALYSIS_UNAVAILABLE";
            var messageText = status == HttpStatusCode.TooManyRequests
                ? "請稍後再嘗試產生時間趨勢分析。"
                : status == HttpStatusCode.GatewayTimeout
                    ? "時間趨勢分析逾時，請稍後再試。"
                    : "時間趨勢分析暫時無法使用。";
            logger.LogWarning("AI trend service returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new TrendAnalysisUnavailableException(errorCode, messageText, status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI trend service request timed out.");
            throw new TrendAnalysisUnavailableException("AI_ANALYSIS_TIMEOUT", "時間趨勢分析逾時，請稍後再試。", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "AI trend service could not be reached.");
            throw new TrendAnalysisUnavailableException("AI_ANALYSIS_UNAVAILABLE", "時間趨勢分析暫時無法使用。", HttpStatusCode.ServiceUnavailable);
        }
    }
}

public sealed class TrendAnalysisUnavailableException(string errorCode, string message, HttpStatusCode statusCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public HttpStatusCode StatusCode { get; } = statusCode;
}
