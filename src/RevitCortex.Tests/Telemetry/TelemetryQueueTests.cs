using System;
using System.IO;
using System.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-q-" + Guid.NewGuid().ToString("N"));
    private string QueuePath => Path.Combine(_dir, "telemetry-queue.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static TelemetryEvent Evt(string id) =>
        new TelemetryEvent { EventId = id, Tool = "t", Fingerprint = "f" };

    [Fact]
    public void Enqueue_ThenPeek_ReturnsEventsInOrder()
    {
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a"));
        q.Enqueue(Evt("b"));
        var batch = q.PeekBatch(10);
        Assert.Equal(new[] { "a", "b" }, batch.Events.Select(e => e.EventId));
        Assert.Equal(2, batch.LineCount);
    }

    [Fact]
    public void RemoveLines_DropsOnlyTheBatch()
    {
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a")); q.Enqueue(Evt("b")); q.Enqueue(Evt("c"));
        var batch = q.PeekBatch(2);
        q.RemoveLines(batch.LineCount);
        var rest = q.PeekBatch(10);
        Assert.Equal(new[] { "c" }, rest.Events.Select(e => e.EventId));
    }

    [Fact]
    public void PeekBatch_SkipsMalformedLines_ButCountsThem()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(QueuePath, new[] { "{not json", "" });
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a"));
        var batch = q.PeekBatch(10);
        Assert.Single(batch.Events);
        Assert.Equal(3, batch.LineCount); // malformed lines are consumed with the batch
    }

    [Fact]
    public void Enqueue_OverCap_DropsOldest()
    {
        var q = new TelemetryQueue(QueuePath, maxBytes: 4096);
        for (int i = 0; i < 100; i++) q.Enqueue(Evt("evt-" + i.ToString("D3")));
        Assert.True(new FileInfo(QueuePath).Length <= 4096);
        var batch = q.PeekBatch(1000);
        Assert.Equal("evt-099", batch.Events.Last().EventId); // newest survived
        Assert.NotEqual("evt-000", batch.Events.First().EventId); // oldest dropped
    }

    [Fact]
    public void PendingLineCount_ReflectsQueue()
    {
        var q = new TelemetryQueue(QueuePath);
        Assert.Equal(0, q.PendingLineCount);
        q.Enqueue(Evt("a"));
        Assert.Equal(1, q.PendingLineCount);
    }
}
