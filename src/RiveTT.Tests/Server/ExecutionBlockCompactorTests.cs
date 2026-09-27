using Newtonsoft.Json.Linq;
using RiveTT.Plugin;
using RiveTT.Server.Connection;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// The session half of the execution block goes out when it changes, never when it would hide
/// a change or a version mismatch.
/// </summary>
public class ExecutionBlockCompactorTests
{
    private static JObject Response(string documentTitle = "ARCH.rvt", int processId = 4242, bool mismatch = false)
    {
        var execution = new JObject
        {
            ["connector"] = "RiveTT",
            ["pluginVersion"] = "0.6.0.0",
            ["mcpServerVersion"] = "0.6.0.0",
            ["revitVersion"] = "2026",
            ["revitProcessId"] = processId,
            ["documentTitle"] = documentTitle,
            ["mode"] = "automatic",
            ["toolReadOnly"] = true,
            ["toolDestructive"] = false,
            ["supportsDryRun"] = false,
            ["writesAllowed"] = false,
            ["cached"] = false
        };
        if (mismatch) execution["versionMismatch"] = new JObject { ["message"] = "mixed pair" };
        return new JObject { ["count"] = 1, ["execution"] = execution };
    }

    [Fact]
    public void AnUnchangedSessionIsSentOnce()
    {
        var compactor = new ExecutionBlockCompactor();
        var first = (JObject)compactor.Compact("list_walls", Response());
        var second = (JObject)compactor.Compact("list_walls", Response());

        Assert.Equal("ARCH.rvt", first["execution"]!["documentTitle"]!.Value<string>());
        var compact = (JObject)second["execution"]!;
        Assert.True(compact["sessionUnchanged"]!.Value<bool>());
        foreach (var field in ExecutionBlockCompactor.SessionFields) Assert.Null(compact[field]);
        // Which Revit and which file stay on every answer: the server process may outlive the
        // conversation that saw them first.
        Assert.Equal("ARCH.rvt", compact["documentTitle"]!.Value<string>());
        Assert.Equal(4242, compact["revitProcessId"]!.Value<int>());
        // What describes THIS call and the lock is always there.
        Assert.False(compact["writesAllowed"]!.Value<bool>());
        Assert.True(compact["toolReadOnly"]!.Value<bool>());
        Assert.NotNull(compact["supportsDryRun"]);
    }

    [Fact]
    public void AChangedTargetIsSentInFull()
    {
        var compactor = new ExecutionBlockCompactor();
        compactor.Compact("list_walls", Response());
        var otherDocument = (JObject)compactor.Compact("list_walls", Response(documentTitle: "FAMILLE.rfa"));
        Assert.Equal("FAMILLE.rfa", otherDocument["execution"]!["documentTitle"]!.Value<string>());
        Assert.Null(otherDocument["execution"]!["sessionUnchanged"]);

        var otherRevit = (JObject)compactor.Compact("list_walls", Response(documentTitle: "FAMILLE.rfa", processId: 7));
        Assert.Equal(7, otherRevit["execution"]!["revitProcessId"]!.Value<int>());
    }

    [Fact]
    public void AVersionMismatchIsNeverCompacted()
    {
        var compactor = new ExecutionBlockCompactor();
        compactor.Compact("list_walls", Response(mismatch: true));
        var again = (JObject)compactor.Compact("list_walls", Response(mismatch: true));
        Assert.NotNull(again["execution"]!["versionMismatch"]);
        Assert.NotNull(again["execution"]!["pluginVersion"]);
    }

    [Theory]
    [InlineData("get_project_info")]
    [InlineData("get_server_capabilities")]
    [InlineData("ping_revit")]
    public void SessionEstablishingToolsAlwaysAnswerInFull(string tool)
    {
        var compactor = new ExecutionBlockCompactor();
        compactor.Compact("list_walls", Response());
        var full = (JObject)compactor.Compact(tool, Response());
        Assert.Equal("ARCH.rvt", full["execution"]!["documentTitle"]!.Value<string>());
    }

    [Fact]
    public void FailuresAreLeftAlone()
    {
        var compactor = new ExecutionBlockCompactor();
        var failure = JObject.Parse("{\"success\":false,\"error\":{\"code\":\"PermissionDenied\",\"context\":{\"pluginVersion\":\"0.6.0.0\"}}}");
        var result = (JObject)compactor.Compact("create_wall", failure);
        Assert.Equal("0.6.0.0", result["error"]!["context"]!["pluginVersion"]!.Value<string>());
    }

    [Theory]
    [InlineData("probe-and-rollback", "BEFORE commit")]
    [InlineData("declared", "Nothing was executed")]
    [InlineData(null, "without running the operation")]
    public void EveryPreviewSaysWhereItStops(string? method, string expected)
    {
        Assert.Contains(expected, RiveTTRouter.DescribePreviewLimits(method));
    }
}
