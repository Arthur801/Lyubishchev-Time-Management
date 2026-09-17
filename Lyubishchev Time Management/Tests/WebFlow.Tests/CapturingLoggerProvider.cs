using Microsoft.Extensions.Logging;

namespace WebFlow.Tests;

public sealed record CapturedLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> State);

/// <summary>
/// An in-memory <see cref="ILoggerProvider"/> so tests can assert exactly what a real
/// <see cref="ILogger"/> call recorded -- level, event ID, formatted message, exception, and
/// structured state -- without depending on Console/journal output.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLogEntry> _entries = [];

    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var stateItems = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToList()
                : [];

            var entry = new CapturedLogEntry(logLevel, eventId, formatter(state, exception), exception, stateItems);
            lock (owner._entries)
            {
                owner._entries.Add(entry);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
