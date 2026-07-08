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

    /// <summary>Start the periodic 5-minute flush timer. Idempotent: a prior
    /// timer (if any) is disposed first so calling Start() twice never
    /// orphans a live timer.</summary>
    public void Start()
    {
        try
        {
            _timer?.Dispose();
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
            var subscribers = KnownIssueMatched;
            if (subscribers == null) return;
            foreach (var m in parsed.KnownIssues)
            {
                // Invoke each subscriber individually (not via a single
                // multicast Invoke) so one throwing subscriber cannot abort
                // the delegate chain and skip subscribers registered after it.
                foreach (Action<KnownIssueMatch> handler in subscribers.GetInvocationList())
                {
                    try { handler(m); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Disposes the timer, waiting (up to 6 s — just above the 5 s
    /// HTTP timeout) for any in-flight callback to finish before disposing
    /// the HttpClient. Timer.Dispose() alone does NOT wait for a running
    /// callback, so without this an in-flight FlushOnce can still be inside
    /// _http.PostAsync when _http.Dispose() runs (ObjectDisposedException).
    /// Never throws.</summary>
    public void Dispose()
    {
        try
        {
            var timer = _timer;
            _timer = null;
            if (timer != null)
            {
                using (var waitHandle = new ManualResetEvent(false))
                {
                    // Dispose(WaitHandle) signals the handle once all in-flight callbacks finish.
                    if (timer.Dispose(waitHandle))
                        waitHandle.WaitOne(TimeSpan.FromSeconds(6));
                }
            }
        }
        catch { }
        try { FlushOnce(); } catch { }   // best-effort shutdown flush
        try { _http.Dispose(); } catch { }
    }

    private class EventsResponse
    {
        [JsonProperty("accepted")] public int Accepted { get; set; }
        [JsonProperty("knownIssues")] public KnownIssueMatch[]? KnownIssues { get; set; }
    }
}
