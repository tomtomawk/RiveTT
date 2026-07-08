using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetrySenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-s-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"accepted\":1,\"knownIssues\":[]}";
        public string? LastRequestBody;
        public string? LastUrl;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastRequestBody = request.Content == null ? null
                : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body)
            };
        }
    }

    private (TelemetrySender sender, TelemetryQueue queue, FakeHandler handler) Make()
    {
        Directory.CreateDirectory(_dir);
        var config = TelemetryConfig.Load(Path.Combine(_dir, "settings.json"));
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var handler = new FakeHandler();
        var sender = new TelemetrySender(config, queue, handler);
        return (sender, queue, handler);
    }

    [Fact]
    public void FlushOnce_EmptyQueue_NoRequest_ReturnsTrue()
    {
        var (sender, _, handler) = Make();
        Assert.True(sender.FlushOnce());
        Assert.Null(handler.LastUrl);
    }

    [Fact]
    public void FlushOnce_Success_PostsBatch_AndDequeues()
    {
        var (sender, queue, handler) = Make();
        queue.Enqueue(new TelemetryEvent { EventId = "e1", Fingerprint = "f1" });

        Assert.True(sender.FlushOnce());
        Assert.EndsWith("/v1/events", handler.LastUrl);
        Assert.Contains("\"eventId\":\"e1\"", handler.LastRequestBody);
        Assert.Equal(0, queue.PendingLineCount);
    }

    [Fact]
    public void FlushOnce_ServerError_KeepsQueue()
    {
        var (sender, queue, handler) = Make();
        handler.Status = HttpStatusCode.InternalServerError;
        queue.Enqueue(new TelemetryEvent { EventId = "e1" });

        Assert.False(sender.FlushOnce());
        Assert.Equal(1, queue.PendingLineCount);
    }

    [Fact]
    public void FlushOnce_KnownIssueInResponse_RaisesEvent()
    {
        var (sender, queue, handler) = Make();
        handler.Body = "{\"accepted\":1,\"knownIssues\":[{\"fingerprint\":\"f1\",\"issueId\":\"RC-014\",\"status\":\"fixed\",\"fixVersion\":\"1.0.42\"}]}";
        queue.Enqueue(new TelemetryEvent { EventId = "e1", Fingerprint = "f1" });

        var matches = new List<KnownIssueMatch>();
        sender.KnownIssueMatched += matches.Add;
        sender.FlushOnce();

        Assert.Single(matches);
        Assert.Equal("RC-014", matches[0].IssueId);
    }
}
