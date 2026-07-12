using System.Collections.Concurrent;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;

namespace Gateway.Infrastructure.Services;

public class LogBuffer : ILogBuffer
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 500;

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);
    }

    public List<LogEntry> GetRecent(int count = 100) =>
        _entries.Reverse().Take(count).Reverse().ToList();

    public void AddInfo(string message, string? path = null, int? statusCode = null, double? durationMs = null,
        string? method = null, string? clientIp = null, string? destination = null, string? requestId = null) =>
        Add(new LogEntry(DateTime.UtcNow, "Info", message, path, statusCode, durationMs,
            method, clientIp, destination, requestId));

    public void AddError(string message, string? path = null, string? method = null, string? clientIp = null, string? requestId = null) =>
        Add(new LogEntry(DateTime.UtcNow, "Error", message, path, null, null,
            method, clientIp, null, requestId));

    public void AddWarning(string message, string? path = null, string? method = null, string? clientIp = null, string? requestId = null) =>
        Add(new LogEntry(DateTime.UtcNow, "Warning", message, path, null, null,
            method, clientIp, null, requestId));
}
