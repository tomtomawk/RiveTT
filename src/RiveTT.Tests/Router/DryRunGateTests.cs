using System.IO;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Plugin;
using Xunit;

namespace RiveTT.Tests.Router;

/// <summary>
/// The dryRun gate.
///
/// EnrichResult used to derive the preview claim from the CALLER's request:
/// <c>if (dryRun &amp;&amp; !isReadOnly) { obj["dryRun"] = true; obj["mutated"] = false; }</c>.
/// For the 79 write tools that never read dryRun, that meant the tool ran, wrote to
/// the model in a transaction, and the response still said nothing had been modified.
/// An agent following SKILL.md ("dryRun: true dès que l'outil le propose") had no way
/// to tell a real preview from a silent mutation.
///
/// The property pinned here: a preview is a claim about what the TOOL did, never about
/// what the caller asked for. A tool that cannot preview is refused, not executed.
///
/// The refusal tests are plain [Fact]: Route answers before reaching any Revit type. The
/// SUCCESS tests are [RequiresRevitDbApiFact], because EnrichResult reads the active
/// document's Application.VersionNumber and that loads Autodesk.Revit.DB — absent on a
/// machine without Revit, where they would fail instead of skipping. Reproduce that
/// machine locally with REVIT_INSTALL_DIR pointed at an empty directory.
/// </summary>
public class DryRunGateTests
{
    [ToolSafety(false, true, supportsDryRun: true)]
    private sealed class PreviewingWriteTool : RecordingToolBase
    {
        public override string Name => "delete_previewable";
    }

    [ToolSafety(false, true)]
    private sealed class ApplyOnlyWriteTool : RecordingToolBase
    {
        public override string Name => "create_grid_like";
    }

    [ToolSafety(true, false)]
    private sealed class ReadTool : RecordingToolBase
    {
        public override string Name => "get_thing";
    }

    private abstract class RecordingToolBase : IRiveTTTool
    {
        public bool WasExecuted { get; private set; }
        public abstract string Name { get; }
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Records execution.";

        public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
        {
            WasExecuted = true;
            return RiveTTResult<object>.Ok(new { created = 3 });
        }
    }

    private static RiveTTRouter CreateRouter(out RiveTTSession session, params IRiveTTTool[] tools)
    {
        session = new RiveTTSession(new SessionStore());
        session.WriteAccess.Set(writesAllowed: true, "test");
        var auditPath = Path.Combine(Path.GetTempPath(),
            "rc-dryrun-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        var router = new RiveTTRouter(session, new FakeAnalyzer(), new AuditLogger(auditPath));
        foreach (var tool in tools) router.RegisterTool(tool);
        return router;
    }

    [Fact]
    public void DryRun_OnToolThatCannotPreview_IsRefusedAndTheToolNeverRuns()
    {
        var tool = new ApplyOnlyWriteTool();
        var router = CreateRouter(out _, tool);

        var result = router.Route(tool.Name, new JObject { ["dryRun"] = true });

        Assert.False(result.Success);
        Assert.Equal(RiveTTErrorCode.InvalidInput, result.Error!.Code);
        // The model must be untouched: this is the regression that mattered.
        Assert.False(tool.WasExecuted);
        Assert.Equal(false, result.Error.Context!["supportsDryRun"]);
        Assert.Equal(false, result.Error.Context!["modelChanged"]);
        // "nothing was modified" and "nothing ran" read the same to an agent unless
        // the refusal says which one it is.
        Assert.Contains("nothing was executed at all", result.Error.Suggestion!);
    }

    [Fact]
    public void DryRun_RefusalNamesThePublicToolWhenGiven()
    {
        var tool = new ApplyOnlyWriteTool();
        var router = CreateRouter(out _, tool);

        var result = router.Route(tool.Name, new JObject { ["dryRun"] = true },
            publicToolName: "create_grid");

        Assert.False(result.Success);
        Assert.Contains("create_grid", result.Error!.Message);
    }

    [RequiresRevitDbApiFact]
    public void DryRun_OnToolThatPreviews_RunsAndIsStamped()
    {
        var tool = new PreviewingWriteTool();
        var router = CreateRouter(out _, tool);

        var result = router.Route(tool.Name, new JObject { ["dryRun"] = true });

        Assert.True(result.Success);
        Assert.True(tool.WasExecuted);
        var data = JObject.FromObject(result.Data!);
        Assert.True(data["dryRun"]!.Value<bool>());
        Assert.False(data["mutated"]!.Value<bool>());
        Assert.True(data["execution"]!["supportsDryRun"]!.Value<bool>());
    }

    [RequiresRevitDbApiFact]
    public void WriteWithoutDryRun_IsNeverStampedAsAPreview()
    {
        var tool = new ApplyOnlyWriteTool();
        var router = CreateRouter(out _, tool);

        var result = router.Route(tool.Name, new JObject());

        Assert.True(result.Success);
        Assert.True(tool.WasExecuted);
        var data = JObject.FromObject(result.Data!);
        Assert.Null(data["dryRun"]);
        Assert.Null(data["mutated"]);
        Assert.False(data["execution"]!["supportsDryRun"]!.Value<bool>());
    }

    [RequiresRevitDbApiFact]
    public void DryRun_OnAReadTool_IsIgnoredNotRefused()
    {
        // A read tool cannot mutate, so a caller passing dryRun defensively everywhere
        // loses nothing. Refusing it would break that caller for no gain.
        var tool = new ReadTool();
        var router = CreateRouter(out _, tool);

        var result = router.Route(tool.Name, new JObject { ["dryRun"] = true });

        Assert.True(result.Success);
        Assert.True(tool.WasExecuted);
        var data = JObject.FromObject(result.Data!);
        Assert.Null(data["mutated"]);
    }

    [Fact]
    public void WriteLock_IsCheckedBeforeTheDryRunContract()
    {
        // Two refusals can apply at once. The lock must win: it is the fact about the
        // session, and answering "does not support dryRun" on a locked session would
        // send an agent rewriting its call instead of asking for the unlock.
        var tool = new ApplyOnlyWriteTool();
        var router = CreateRouter(out var session, tool);
        session.WriteAccess.Set(writesAllowed: false, "test");

        var result = router.Route(tool.Name, new JObject { ["dryRun"] = true });

        Assert.False(result.Success);
        Assert.Equal(RiveTTErrorCode.PermissionDenied, result.Error!.Code);
        Assert.False(tool.WasExecuted);
    }

    [Fact]
    public void Coverage_CountsWriteToolsThatPreview_AndSurvivesReinitialize()
    {
        var router = CreateRouter(out var session,
            new PreviewingWriteTool(), new ApplyOnlyWriteTool(), new ReadTool());

        Assert.Equal((1, 2), session.DryRunCoverage);

        // get_server_capabilities reads this after any number of document changes;
        // Reinitialize clears the Store, which is why it does not live there.
        session.Reinitialize(new Core.Discovery.DocumentCapabilities(), "fr");
        Assert.Equal((1, 2), session.DryRunCoverage);
    }
}
