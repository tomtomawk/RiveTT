using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class ErrorReporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-r-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (ErrorReporter reporter, TelemetryQueue queue, TelemetryConfig config) Make(
        bool consented = true, long durThreshold = 10000, long bytesThreshold = 512000)
    {
        Directory.CreateDirectory(_dir);
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings,
            "{\"BottleneckDurationMs\":" + durThreshold +
            ",\"BottleneckResponseBytes\":" + bytesThreshold + "}");
        var config = TelemetryConfig.Load(settings);
        if (consented) config.MarkConsent(true);
        config = TelemetryConfig.Load(settings);
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var env = new TelemetryEnvironment
        {
            PluginVersion = "1.0.40", RevitVersion = "2025",
            Target = "R25", OsMajor = "Windows 10.0", Locale = "it"
        };
        return (new ErrorReporter(config, queue, sender: null, env), queue, config);
    }

    [Fact]
    public void Record_Failure_QueuesErrorEvent_WithFingerprint()
    {
        var (r, q, _) = Make();
        r.Record("create_dimensions", success: false, errorCode: "InvalidInput",
            message: "Element 12345 does not exist", failureStage: "tool",
            durationMs: 10, responseBytes: 20);

        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("error", evt.Kind);
        Assert.Equal("create_dimensions", evt.Tool);
        Assert.Matches("^[0-9a-f]{16}$", evt.Fingerprint);
        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.False(string.IsNullOrEmpty(evt.InstallationId));
        Assert.False(string.IsNullOrEmpty(evt.Timestamp));
    }

    [Fact]
    public void Record_ConsentMissing_IsCompleteNoOp()
    {
        var (r, q, _) = Make(consented: false);
        r.Record("t", false, "Unknown", "boom", "tool", 1, 1);
        Assert.Equal(0, q.PendingLineCount); // not even queued
    }

    [Fact]
    public void Record_TemplatedSafeMessage_SendsSanitizedText()
    {
        var (r, q, _) = Make();
        r.Record("t", false, "InvalidInput", "Element 12345 does not exist", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("templated", evt.MessageOrigin);
        Assert.Contains("does not exist", evt.SanitizedMessage);
    }

    [Fact]
    public void Record_UnknownErrorCode_NeverSendsText()
    {
        var (r, q, _) = Make();
        r.Record("t", false, "Unknown", "Unhandled exception: boom at C:\\x", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("exception", evt.MessageOrigin);
        Assert.Null(evt.SanitizedMessage);
    }

    [Fact]
    public void Record_SuccessUnderThresholds_NoEvent()
    {
        var (r, q, _) = Make();
        r.Record("t", true, null, null, "tool", durationMs: 5, responseBytes: 5);
        Assert.Equal(0, q.PendingLineCount);
    }

    [Fact]
    public void Record_SuccessOverDuration_QueuesBottleneck()
    {
        var (r, q, _) = Make(durThreshold: 1);
        r.Record("export_to_excel", true, null, null, "tool", durationMs: 50, responseBytes: 5);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("bottleneck", evt.Kind);
        Assert.Null(evt.ErrorCode);
    }

    [Fact]
    public void Record_RepeatedFailureSameFingerprint_RaisesAtThreshold_Once()
    {
        var (r, _, _) = Make();
        var raised = new List<int>();
        r.RepeatedFailureDetected += (fp, count) => raised.Add(count);

        for (int i = 0; i < 5; i++)
            r.Record("t", false, "InvalidInput", "Element 1 does not exist", "tool", 1, 1);

        Assert.Single(raised);      // fires exactly once, at the threshold
        Assert.Equal(3, raised[0]); // default ZipPromptFailureThreshold
    }

    [Fact]
    public void Record_TemplatedBareNameMidSentence_NeverSendsText()
    {
        // Structured code, but the template embeds a bare (unquoted) interpolated
        // name with no adjacent punctuation — the worst-case leak the sanitizer
        // alone would wave through. Must be classed exception-origin, no text.
        var (r, q, _) = Make();
        r.Record("tag_rooms", false, "TransactionFailed",
            "Failed to tag room Strutture object reference not set", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("exception", evt.MessageOrigin);
        Assert.Null(evt.SanitizedMessage);
    }

    [Fact]
    public void Record_StructuredCodeButExMessageEmbedded_NeverSendsText()
    {
        // A structured (non-Unknown) code whose template still appended ex.Message.
        // Even though gate #1 passes, the embedded exception phrase carries a
        // capitalized proper-noun-shaped token -> must fail closed.
        var (r, q, _) = Make();
        r.Record("some_tool", false, "TransactionFailed",
            "Save failed: The DESKTOP model is locked by Mario", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("exception", evt.MessageOrigin);
        Assert.Null(evt.SanitizedMessage);
    }

    [Fact]
    public void Record_QuotedNameInTemplate_StillTransmits_NameRedacted()
    {
        // The common safe shape: name is single-quoted, so RxQuoted redacts it.
        // Text still transmits (templated) but the name must NOT appear.
        var (r, q, _) = Make();
        r.Record("create_level", false, "InvalidInput",
            "A level named 'Strutture' already exists", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("templated", evt.MessageOrigin);
        Assert.DoesNotContain("strutture", evt.SanitizedMessage!.ToLowerInvariant());
    }
}
