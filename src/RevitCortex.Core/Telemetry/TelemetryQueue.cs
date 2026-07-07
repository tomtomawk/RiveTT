using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>Result of a PeekBatch call: the parsed events plus how many raw
/// lines they span (used by RemoveLines to drop exactly the consumed batch,
/// including malformed lines that were skipped but still counted).</summary>
public class TelemetryBatch
{
    public List<TelemetryEvent> Events { get; }
    public int LineCount { get; }

    public TelemetryBatch(List<TelemetryEvent> events, int lineCount)
    {
        Events = events;
        LineCount = lineCount;
    }
}

/// <summary>
/// Durable JSONL queue for telemetry events. Capped (default 5 MB) with
/// drop-oldest overflow. Thread-safe via a single lock (same spirit as
/// AuditLogger). All operations swallow I/O failures: losing telemetry is
/// always preferable to affecting the host.
/// </summary>
public class TelemetryQueue
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _lock = new object();

    public TelemetryQueue(string path, long maxBytes = 5 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public int PendingLineCount
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return File.Exists(_path) ? File.ReadAllLines(_path).Length : 0;
                }
                catch { return 0; }
            }
        }
    }

    public void Enqueue(TelemetryEvent evt)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(_path,
                    JsonConvert.SerializeObject(evt, Formatting.None) + "\n");

                var info = new FileInfo(_path);
                if (info.Length > _maxBytes) CompactLocked();
            }
            catch { /* never crash the host */ }
        }
    }

    public TelemetryBatch PeekBatch(int maxEvents)
    {
        lock (_lock)
        {
            var events = new List<TelemetryEvent>();
            int lines = 0;
            try
            {
                if (!File.Exists(_path)) return new TelemetryBatch(events, 0);
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (events.Count >= maxEvents) break;
                    lines++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonConvert.DeserializeObject<TelemetryEvent>(line);
                        if (evt != null) events.Add(evt);
                    }
                    catch { /* malformed line: counted, skipped, removed with batch */ }
                }
            }
            catch { }
            return new TelemetryBatch(events, lines);
        }
    }

    public void RemoveLines(int lineCount)
    {
        if (lineCount <= 0) return;
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) return;
                var remaining = File.ReadAllLines(_path).Skip(lineCount).ToArray();
                File.WriteAllLines(_path, remaining);
            }
            catch { }
        }
    }

    // Drop oldest lines until under 80% of cap. Caller holds _lock.
    private void CompactLocked()
    {
        var lines = File.ReadAllLines(_path);
        long budget = (long)(_maxBytes * 0.8);
        var kept = new List<string>();
        long size = 0;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            size += lines[i].Length + 1;
            if (size > budget) break;
            kept.Add(lines[i]);
        }
        kept.Reverse();
        File.WriteAllLines(_path, kept);
    }
}
