using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Batched background flush of the telemetry queue to {endpoint}/v1/events.
/// 5 s HTTP timeout, no aggressive retry (failed batches simply stay queued
/// for the next flush). Never throws from public entry points.
/// </summary>
public class TelemetrySender : IDisposable
{
    private const int MaxBatch = 100;
    private const string ClientKey = "rc-public-2026";

    private readonly TelemetryConfig _config;
    private readonly TelemetryQueue _queue;
    private readonly HttpClient _http;
    private Timer? _timer;
    private int _flushing;

    public event Action<KnownIssueMatch>? KnownIssueMatched;

    public TelemetrySender(TelemetryConfig config, TelemetryQueue queue,
        HttpMessageHandler? handler = null)
    {
        _config = config;
        _queue = queue;
        try
        {
            // net48 host: default protocols may exclude TLS 1.2 (same fix as UpdateChecker).
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch { }
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(5);
        _http.DefaultRequestHeaders.Add("X-RC-Key", ClientKey);
    }

    /// <summary>Start the periodic 5-minute flush timer.</summary>
    public void Start()
    {
        try
        {
            _timer = new Timer(_ => FlushOnce(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        catch { }
    }

    /// <summary>Called by the reporter after each enqueue: flush early at 20 pending.</summary>
    public void NotifyEnqueued()
    {
        try
        {
            if (_queue.PendingLineCount >= 20)
                ThreadPool.QueueUserWorkItem(_ => FlushOnce());
        }
        catch { }
    }

    /// <summary>One flush pass. True when the queue is empty afterwards or was empty.</summary>
    public bool FlushOnce()
    {
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return false;
        try
        {
            var batch = _queue.PeekBatch(MaxBatch);
            if (batch.Events.Count == 0)
            {
                if (batch.LineCount > 0) _queue.RemoveLines(batch.LineCount); // all-malformed
                return true;
            }

            var payload = JsonConvert.SerializeObject(new { events = batch.Events });
            var url = _config.Endpoint.TrimEnd('/') + "/v1/events";
            var response = _http.PostAsync(url,
                new StringContent(payload, Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode) return false;

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            _queue.RemoveLines(batch.LineCount);
            RaiseKnownIssues(body);
            return true;
        }
        catch
        {
            return false; // stays queued; next flush retries
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void RaiseKnownIssues(string body)
    {
        try
        {
            var parsed = JsonConvert.DeserializeObject<EventsResponse>(body);
            if (parsed?.KnownIssues == null) return;
            foreach (var m in parsed.KnownIssues)
            {
                try { KnownIssueMatched?.Invoke(m); } catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { _timer?.Dispose(); } catch { }
        try { FlushOnce(); } catch { }   // best-effort shutdown flush
        try { _http.Dispose(); } catch { }
    }

    private class EventsResponse
    {
        [JsonProperty("accepted")] public int Accepted { get; set; }
        [JsonProperty("knownIssues")] public KnownIssueMatch[]? KnownIssues { get; set; }
    }
}
