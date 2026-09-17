namespace Lyubishchev_Time_Management.Infrastructure.Logging;

public interface IOperationalEventLogger
{
    void TimerTransactionFailed(ulong userId, string operation, Exception exception);

    void CsvExportFailed(ulong userId, bool hasRange, bool hasCategoryFilter, bool hasSearch, Exception exception);
}
