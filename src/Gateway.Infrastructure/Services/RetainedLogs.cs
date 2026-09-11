using System.Globalization;
using System.Text.RegularExpressions;
using Gateway.Domain.Models;

namespace Gateway.Infrastructure.Services;

// ponytail: scan retained text files per query; add an indexed store when log volume warrants it.
public static class RetainedLogs
{
    private static readonly Regex Header = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w+)\] (.*)$");
    private static readonly Regex Request = new(@"^\[ProxyRequest\] \[(\w+)\] (.*?) -> (\d{3}) \(([\d.,]+)ms\)$");

    public static object Query(string directory, int page, int pageSize, string? level,
        string? search, int? status, DateTimeOffset? before)
    {
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 200 ||
            (level != null && level is not ("Info" or "Warning" or "Error" or "Fatal" or "Debug" or "Verbose")) ||
            (status.HasValue && status is < 100 or > 599))
            throw new ArgumentException("Invalid log filters: page >= 1, pageSize 1–100, search <= 200, valid level and HTTP status required.");
        var cutoff = before ?? DateTimeOffset.UtcNow;
        var files = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "gateway-*.log").OrderDescending(StringComparer.Ordinal).ToArray()
            : [];
        var items = new List<LogEntry>();
        long total = 0;
        DateTime? oldest = null, newest = null;
        // Each file is read with sharing enabled while Serilog continues writing.
        // Newest file first; reverse each file's events, never a client-side latest-N slice.
        foreach (var file in files)
        {
            foreach (var entry in Read(file).Reverse())
            {
                if (entry.Path == null || RequestLogScope.IsInternal(entry.Path) || entry.Timestamp > cutoff.UtcDateTime) continue;
                oldest = oldest == null || entry.Timestamp < oldest ? entry.Timestamp : oldest;
                newest = newest == null || entry.Timestamp > newest ? entry.Timestamp : newest;
                if (level != null && entry.Level != level || status != null && entry.StatusCode != status ||
                    !string.IsNullOrEmpty(search) && !entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                if (total >= ((long)page - 1) * pageSize && items.Count < pageSize) items.Add(entry);
                total++;
            }
        }
        return new { items, page, pageSize, total, totalPages = (long)Math.Ceiling((double)total / pageSize),
            hasPrevious = page > 1, hasNext = (long)page * pageSize < total, before = cutoff,
            source = "Proxied backend request events (logs/gateway-*.log)",
            retainedFiles = files.Length, oldest, newest,
            retention = "Daily rolling files; up to 31 files by default, not guaranteed days. Only tagged proxy request events are shown; legacy untagged events cannot prove proxy origin and are omitted. Application warnings/errors remain in server files for diagnostics, outside this view. IP/request ID are not stored in this text format." };
    }

    private static IEnumerable<LogEntry> Read(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        LogEntry? entry = null;
        while (reader.ReadLine() is { } line)
        {
            var header = Header.Match(line);
            if (!header.Success)
            {
                if (entry != null) entry = entry with { Message = entry.Message + "\n" + line };
                continue;
            }
            if (entry != null) yield return entry;
            var timestamp = DateTimeOffset.ParseExact(header.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture).UtcDateTime;
            var level = header.Groups[2].Value switch { "INF" => "Info", "WRN" => "Warning", "ERR" => "Error", "FTL" => "Fatal", "DBG" => "Debug", "VRB" => "Verbose", var other => other };
            var message = header.Groups[3].Value;
            var request = Request.Match(message);
            double? duration = request.Success && double.TryParse(request.Groups[4].Value.Replace(',', '.'), CultureInfo.InvariantCulture, out var ms) ? ms : null;
            entry = new LogEntry(timestamp, level, message, request.Success ? request.Groups[2].Value : null,
                request.Success ? int.Parse(request.Groups[3].Value, CultureInfo.InvariantCulture) : null, duration,
                request.Success ? request.Groups[1].Value : null);
        }
        if (entry != null) yield return entry;
    }
}
