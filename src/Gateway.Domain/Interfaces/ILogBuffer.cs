using Gateway.Domain.Models;

namespace Gateway.Domain.Interfaces;

public interface ILogBuffer
{
    void Add(LogEntry entry);
    List<LogEntry> GetRecent(int count = 100);
    void AddInfo(string message, string? path = null, int? statusCode = null, double? durationMs = null,
        string? method = null, string? clientIp = null, string? destination = null, string? requestId = null);
    void AddError(string message, string? path = null, string? method = null, string? clientIp = null, string? requestId = null);
    void AddWarning(string message, string? path = null, string? method = null, string? clientIp = null, string? requestId = null);
}
