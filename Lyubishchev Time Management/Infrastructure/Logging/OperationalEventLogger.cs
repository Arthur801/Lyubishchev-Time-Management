using Microsoft.Extensions.Logging;

namespace Lyubishchev_Time_Management.Infrastructure.Logging;

/// <summary>
/// Emits safe operational-failure summaries for Timer/CSV export boundaries: only primitive
/// identifiers and booleans, never CSV bytes, search text, request bodies, tokens, cookies, or
/// connection strings.
/// </summary>
public sealed class OperationalEventLogger(ILogger<OperationalEventLogger> logger) : IOperationalEventLogger
{
    private static readonly EventId TimerTransactionFailedEvent = new(1001, nameof(TimerTransactionFailed));
    private static readonly EventId CsvExportFailedEvent = new(1002, nameof(CsvExportFailed));

    public void TimerTransactionFailed(ulong userId, string operation, Exception exception) =>
        logger.LogError(TimerTransactionFailedEvent, exception, "Timer transaction failed. UserId={UserId} Operation={Operation}", userId, operation);

    public void CsvExportFailed(ulong userId, bool hasRange, bool hasCategoryFilter, bool hasSearch, Exception exception) =>
        logger.LogError(
            CsvExportFailedEvent,
            exception,
            "CSV export failed. UserId={UserId} HasRange={HasRange} HasCategoryFilter={HasCategoryFilter} HasSearch={HasSearch}",
            userId, hasRange, hasCategoryFilter, hasSearch);
}
