using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Telemetry;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterTelemetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-rt-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private class FailingTool : ICortexTool
    {
        public string Name => "failing_tool";
        public string Category => "Test";
        public string Description => "Fails for telemetry-hook tests.";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public CortexResult<object> Execute(JObject input, CortexSession session)
            => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Element 12345 does not exist");
    }

    private (CortexRouter router, TelemetryQueue queue) Make()
    {
        Directory.CreateDirectory(_dir);
        var settings = Path.Combine(_dir, "settings.json");
        var config = TelemetryConfig.Load(settings);
        config.MarkConsent(true);
        config = TelemetryConfig.Load(settings);
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var reporter = new ErrorReporter(config, queue, null, new TelemetryEnvironment());

        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new FakeAnalyzer(),
            auditLogger: new RevitCortex.Core.Security.AuditLogger(
                Path.Combine(_dir, "audit.jsonl")),
            errorReporter: reporter);
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools["failing_tool"] = new FailingTool();
        return (router, queue);
    }

    [Fact]
    public void Route_Failure_RecordsTelemetryEvent()
    {
        var (router, queue) = Make();
        router.Route("failing_tool", new JObject());

        var evt = queue.PeekBatch(10).Events.Single();
        Assert.Equal("failing_tool", evt.Tool);
        Assert.Equal("error", evt.Kind);
        Assert.Equal("tool", evt.FailureStage);
        Assert.Equal("InvalidInput", evt.ErrorCode);
    }
}
