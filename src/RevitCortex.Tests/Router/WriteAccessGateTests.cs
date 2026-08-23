using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Tools.Meta;
using Xunit;

namespace RevitCortex.Tests.Router;

/// <summary>
/// The ribbon write lock. These tests pin the property that makes it a lock and
/// not a suggestion: while it is closed, nothing that could write runs, and
/// nothing reachable through the pipe can open it.
/// </summary>
public class WriteAccessGateTests
{
    private static CortexRouter CreateRouter(out CortexSession session, bool writesAllowed)
    {
        session = new CortexSession(new SessionStore());
        session.WriteAccess.Set(writesAllowed, "test");
        var router = new CortexRouter(session, new FakeAnalyzer());
        router.RegisterTool(new FakeTool { Name = "create_thing" });
        router.RegisterTool(new FakeTool { Name = "get_thing" });
        return router;
    }

    [Fact]
    public void ReadOnly_WriteTool_IsRefusedWithPermissionDenied()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("create_thing", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains("read-only", result.Error.Message);
        // The refusal has to say where the switch is; an agent cannot guess a
        // ribbon panel from an error code.
        Assert.Contains("MCPRVTT27", result.Error.Suggestion!);
        Assert.Equal(false, result.Error.Context!["writesAllowed"]);
        Assert.Equal(false, result.Error.Context!["modelChanged"]);
    }

    [Fact]
    public void ReadOnly_DryRunDoesNotBypassTheLock()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("create_thing", new JObject { ["dryRun"] = true });

        // A preview is a tool's own promise, not a permission boundary. Trusting
        // it would make the lock only as strong as the weakest of ~250 tools.
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public void ReadOnly_ReadToolStillAnswers()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("get_thing", new JObject());

        Assert.True(result.Success);
    }

    [Fact]
    public void ReadOnly_IsReportedInTheExecutionBlock()
    {
        var router = CreateRouter(out _, writesAllowed: false);

        var result = router.Route("get_thing", new JObject());

        var execution = (JObject)((JObject)result.Data!)["execution"]!;
        Assert.False(execution["writesAllowed"]!.Value<bool>());
        Assert.True(execution["toolReadOnly"]!.Value<bool>());
    }

    [Fact]
    public void WritesAllowed_WriteToolRuns()
    {
        var router = CreateRouter(out _, writesAllowed: true);

        var result = router.Route("create_thing", new JObject());

        Assert.True(result.Success);
        var execution = (JObject)((JObject)result.Data!)["execution"]!;
        Assert.True(execution["writesAllowed"]!.Value<bool>());
    }

    [Fact]
    public void Lock_SurvivesDocumentReinitialization()
    {
        var router = CreateRouter(out var session, writesAllowed: false);

        // Opening, closing or saving-as a document must not hand write access
        // back: the lock describes the session, not the document.
        session.Reinitialize(new Core.Discovery.DocumentCapabilities(), "fr");

        Assert.False(session.WriteAccess.WritesAllowed);
        Assert.False(router.Route("create_thing", new JObject()).Success);
    }

    [Fact]
    public void Policy_ReportsWhoChangedItAndOnlyFlipsOnce()
    {
        var session = new CortexSession(new SessionStore());

        Assert.True(session.WriteAccess.Set(false, "startup"));
        Assert.Equal("startup", session.WriteAccess.ChangedBy);
        // Re-selecting the current mode is a no-op, so a second ribbon click on
        // the same button reports nothing to the user.
        Assert.False(session.WriteAccess.Set(false, "ribbon"));
        Assert.True(session.WriteAccess.Set(true, "ribbon"));
        Assert.Equal("ribbon", session.WriteAccess.ChangedBy);
    }

    [Fact]
    public void NoPublishedToolWritesToThePolicy()
    {
        // The guarantee is structural, so it is asserted on the source of the
        // whole tool catalogue rather than on one router instance: no tool may
        // call Set on the policy. A tool that could would be the first thing an
        // agent reaches for after a refusal, and the lock would mean nothing.
        var toolsRoot = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "RevitCortex.Tools"));
        var offenders = Directory
            .EnumerateFiles(toolsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("obj", "")) &&
                           File.ReadAllText(file).Contains("WriteAccess.Set("))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CapabilityContract_AnnouncesTheLockAndItsState()
    {
        var session = new CortexSession(new SessionStore());
        session.WriteAccess.Set(false, "startup");

        var result = new GetServerCapabilitiesTool().Execute(new JObject(), session);

        var payload = JObject.FromObject(result.Data!);
        Assert.True(payload["readOnlyModeExists"]!.Value<bool>());
        Assert.False(payload["writesAllowed"]!.Value<bool>());
        Assert.True(payload["readOnlyMode"]!["active"]!.Value<bool>());
        Assert.False(payload["readOnlyMode"]!["toolsCanUnlock"]!.Value<bool>());
        Assert.Equal("startup", payload["readOnlyMode"]!["changedBy"]!.Value<string>());
    }
}
