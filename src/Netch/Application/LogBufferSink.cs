using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace Netch.Application;

public sealed class LogBufferSink : ILogEventSink
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<LogEntryDto> _entries = new();
    private long _nextId;

    public IReadOnlyList<LogEntryDto> GetEntries(bool includeDebug = false)
    {
        return _entries.Where(entry => includeDebug || entry.Level != "DEBUG").ToArray();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }

        AppEvents.Publish("logs.cleared");
    }

    public void Emit(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => "DEBUG",
            LogEventLevel.Warning => "WARNING",
            LogEventLevel.Error or LogEventLevel.Fatal => "ERROR",
            _ => "INFO"
        };

        var message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        if (logEvent.Exception is not null)
            message = $"{message}: {logEvent.Exception.Message}";

        var entry = new LogEntryDto(Interlocked.Increment(ref _nextId), logEvent.Timestamp, level, message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity)
            _entries.TryDequeue(out _);

        if (level != "DEBUG")
            AppEvents.Publish("logs.added", entry);
    }
}
