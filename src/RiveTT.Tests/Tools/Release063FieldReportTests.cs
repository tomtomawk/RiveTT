using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Jobs;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Tools.CodeExecution;
using RiveTT.Tools.Elements;
using RiveTT.Tools.Meta;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// 0.6.3, second field report (EQS_IRM, 2026-10-15): the lifetime of a script run with fromScript, the time limits
/// and background jobs, the breakdown of deletedCount, regroup, and the sandbox hint for `dynamic`.
/// </summary>
public class Release063FieldReportTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    // --- 1. fromScript keeps REUSABLE ------------------------------------------------------------------

    [Fact]
    public void AReusableScriptRunWithFromScriptStaysReusableWithoutAnExplicitFlag()
    {
        // Without edits and with edits: both resolve the lifetime from the version being run.
        Assert.True(SendCodeToRevitTool.ResolveReusable(null, "// REUSABLE"));
        Assert.True(SendCodeToRevitTool.ResolveReusable(null, "  // reusable"));
    }

    [Fact]
    public void ATempScriptStaysTempAndNewCodeDefaultsToTemp()
    {
        Assert.False(SendCodeToRevitTool.ResolveReusable(null, "// TEMP"));
        Assert.False(SendCodeToRevitTool.ResolveReusable(null, null));
    }

    [Fact]
    public void OnlyAnExplicitFalseTurnsAReusableScriptBackIntoTemp()
    {
        Assert.False(SendCodeToRevitTool.ResolveReusable(false, "// REUSABLE"));
        Assert.True(SendCodeToRevitTool.ResolveReusable(true, "// TEMP"));
    }

    [Fact]
    public void TheLifetimeIsReadFromTheHeaderOfTheLatestSavedFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "RiveTT-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "20261015_100000_polylignes.cs"), "// TEMP\n// Name: polylignes\n// ====\ncode();");
            File.WriteAllText(Path.Combine(folder, "20261015_110000_polylignes.cs"), "// REUSABLE\n// Name: polylignes\n// ====\ncode();");

            var code = SendCodeToRevitTool.LoadSavedScript(folder, "polylignes", out var path, out var error);
            Assert.Null(error);
            Assert.NotNull(code);
            Assert.EndsWith("110000_polylignes.cs", path);
            Assert.True(SendCodeToRevitTool.ResolveReusable(null, File.ReadLines(path!).First()));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void TheWrapperDowngradesOnlyThroughLifetimeAndNeverSendsAnAmbiguousFalseWithFromScript()
    {
        var wrapper = Source("RiveTT.Server", "Tools", "ProjectTools.cs");
        Assert.Contains("string? lifetime = null", wrapper);
        Assert.Contains("else if (reusable || fromScript == null)", wrapper);
        var tool = Source("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs");
        Assert.Contains("ResolveReusable(reusableRequested, ReadFirstLine(resolvedFrom))", tool);
        Assert.Contains("scriptLifetimeInherited", tool);
    }

    // --- 2. job registry, get_job_status, time limits -------------------------------------------------------

    [Fact]
    public void AJobIsRunningUntilCompletedThenReportsItsResult()
    {
        var id = JobRegistry.NewId();
        JobRegistry.RegisterRunning(id, "send_code_to_revit", "async");
        Assert.True(JobRegistry.TryGet(id, out var job));
        Assert.Equal("running", job.Status);

        JobRegistry.Complete(id, RiveTTResult<object>.Ok(new { converted = 24 }));
        Assert.Equal("completed", job.Status);
        Assert.NotNull(job.CompletedUtc);

        var failed = JobRegistry.NewId();
        JobRegistry.RegisterRunning(failed, "send_code_to_revit", "after_timeout");
        JobRegistry.Complete(failed, RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, "boom"));
        Assert.True(JobRegistry.TryGet(failed, out var failedJob));
        Assert.Equal("failed", failedJob.Status);
    }

    [Fact]
    public void ARegistrationAfterTheCompletionKeepsTheCompletedRecord()
    {
        // The tool can finish between the end of the caller's wait and the registration of the job.
        var id = JobRegistry.NewId();
        JobRegistry.RegisterRunning(id, "t", "after_timeout");
        JobRegistry.Complete(id, RiveTTResult<object>.Ok(new { ok = true }));
        JobRegistry.RegisterRunning(id, "t", "after_timeout");
        Assert.True(JobRegistry.TryGet(id, out var job));
        Assert.Equal("completed", job.Status);
    }

    [Fact]
    public void CompletingAnUntrackedIdIsIgnored()
    {
        var id = JobRegistry.NewId();
        JobRegistry.Complete(id, RiveTTResult<object>.Ok(new { }));
        Assert.False(JobRegistry.IsTracked(id));
    }

    [Fact]
    public void TheRegistryIsBoundedAndNeverDropsARunningJob()
    {
        JobRegistry.Clear();
        var running = JobRegistry.NewId();
        JobRegistry.RegisterRunning(running, "t", "async");
        for (var i = 0; i < 80; i++)
        {
            var id = JobRegistry.NewId();
            JobRegistry.RegisterRunning(id, "t", "async");
            JobRegistry.Complete(id, RiveTTResult<object>.Ok(new { i }));
        }
        Assert.True(JobRegistry.All().Count <= 51);
        Assert.True(JobRegistry.TryGet(running, out var job));
        Assert.Equal("running", job.Status);
        JobRegistry.Clear();
    }

    [Fact]
    public void GetJobStatusReportsRunningCompletedAndUnknownJobs()
    {
        var tool = new GetJobStatusTool();
        var id = JobRegistry.NewId();
        JobRegistry.RegisterRunning(id, "send_code_to_revit", "async");

        var running = JObject.FromObject(tool.Execute(new JObject { ["jobId"] = id }, null!).Data!);
        Assert.Equal("running", (string?)running["status"]);
        Assert.Contains("do not re-send", (string?)running["message"]);

        JobRegistry.Complete(id, RiveTTResult<object>.Ok(new { converted = 24 }));
        var done = JObject.FromObject(tool.Execute(new JObject { ["jobId"] = id }, null!).Data!);
        Assert.Equal("completed", (string?)done["status"]);
        Assert.Equal(24, (int?)done["result"]!["converted"]);

        var unknown = tool.Execute(new JObject { ["jobId"] = "job_nope" }, null!);
        Assert.False(unknown.Success);
        Assert.Equal(RiveTTErrorCode.ElementNotFound, unknown.Error!.Code);

        var list = JObject.FromObject(tool.Execute(new JObject(), null!).Data!);
        Assert.True((int?)list["jobCount"] >= 1);
    }

    [Fact]
    public void GetJobStatusAnswersOffTheRevitThreadAndSendCodeIsTheAsyncCapableTool()
    {
        Assert.IsAssignableFrom<RiveTT.Core.Tools.IInlineTool>(new GetJobStatusTool());
        Assert.IsAssignableFrom<RiveTT.Core.Tools.IAsyncCapableTool>(new SendCodeToRevitTool());

        var router = Source("RiveTT.Plugin", "RiveTTRouter.cs");
        Assert.Contains("tool is IInlineTool", router);
        Assert.Contains("tool is IAsyncCapableTool && input[\"async\"]?.Value<bool>() == true", router);
        Assert.Contains("_dispatcher.StartJob(tool, input, _session)", router);
    }

    [Fact]
    public void ATimeoutSaysWhetherTheToolIsStillRunningAndWhereItsResultIsKept()
    {
        var dispatcher = Source("RiveTT.Plugin", "Threading", "RevitThreadDispatcher.cs");
        Assert.Contains("toolStillRunning", dispatcher);
        Assert.Contains("JobRegistry.RegisterRunning(jobId, tool.Name, \"after_timeout\"", dispatcher);
        Assert.Contains("TryGetCompleted(jobId", dispatcher);
        // Registered BEFORE the pending state is cleared, so a tool finishing right after still reaches the registry.
        var timedOut = dispatcher.IndexOf("RiveTTResult<object> TimedOut(", StringComparison.Ordinal);
        Assert.True(timedOut > 0);
        Assert.True(dispatcher.IndexOf("JobRegistry.RegisterRunning(jobId, tool.Name, \"after_timeout\"", timedOut, StringComparison.Ordinal)
                    < dispatcher.IndexOf("_handler.ClearPreparedExecution();", timedOut, StringComparison.Ordinal));

        var handler = Source("RiveTT.Plugin", "Threading", "ToolExecutionHandler.cs");
        Assert.Contains("JobRegistry.Complete(jobId, result)", handler);
        Assert.Contains("session.Cache.InvalidateAll()", handler);
    }

    [Fact]
    public void CaptureViewHasItsOwnBudgetBelowTheClientsAndReportsWhereTheTimeWent()
    {
        var tool = Source("RiveTT.Tools", "Views", "CaptureViewTool.cs");
        Assert.Contains("public int CommandTimeoutSeconds => 90;", tool);
        Assert.Contains("FitBudgetMs", tool);
        Assert.Contains("ExportStartLimitMs", tool);
        Assert.Contains("catch (BudgetExceededException budget)", tool);
        Assert.Contains("RiveTTErrorCode.Timeout", tool);
        Assert.Contains("timingsMs", tool);
        Assert.Contains("Coarse", tool);
        Assert.Contains("pixelSize", tool);
    }

    [Fact]
    public void SendCodeIsPublishedWithAsyncAndTheJobToolIsPublished()
    {
        var wrapper = Source("RiveTT.Server", "Tools", "ProjectTools.cs");
        Assert.Contains("bool @async = false", wrapper);
        Assert.Contains("p[\"async\"] = true", wrapper);
        Assert.Contains("Name = \"get_job_status\"", Source("RiveTT.Server", "Tools", "MetaTools.cs"));
    }

    // --- 3. deletedCount breakdown ---------------------------------------------------------------------------

    [Fact]
    public void TheDeletedBreakdownSeparatesScaffoldingFromTheRestAndCountsPerClass()
    {
        var deleted = Enumerable.Repeat(("ModelLine", true), 1135)
            .Concat(Enumerable.Repeat(("ModelArc", true), 392))
            .Append(("Group", false))
            .Concat(Enumerable.Repeat(("Sketch", true), 4))
            .Concat(Enumerable.Repeat(((string?)null, false), 3).Select(x => ((string?)x.Item1, x.Item2)))
            .Select(x => ((string?)x.Item1, x.Item2))
            .ToList();

        var summary = DeletedBreakdown.Summarize(deleted);

        Assert.Equal(1, summary.TopLevelCount);
        Assert.Equal(1135 + 392 + 4, summary.AuxiliaryCount);
        Assert.Equal(3, summary.UnclassifiedCount);
        Assert.Equal(1135, summary.ByClass["ModelLine"]);
        Assert.Equal(392, summary.ByClass["ModelArc"]);
        Assert.Equal(1, summary.ByClass["Group"]);
        Assert.Equal(1135 + 392 + 1 + 4 + 3, deleted.Count);
    }

    [Fact]
    public void TheDeletedBreakdownCapsTheNumberOfClassesAndKeepsTheRemainder()
    {
        var deleted = Enumerable.Range(0, 20).Select(i => ((string?)$"Class{i:00}", false)).ToList();
        var summary = DeletedBreakdown.Summarize(deleted);
        Assert.Equal(DeletedBreakdown.MaxClasses + 1, summary.ByClass.Count);
        Assert.Equal(5, summary.ByClass["(other classes)"]);
        Assert.Equal(20, summary.ByClass.Values.Sum());
    }

    [Fact]
    public void TheChangeReportsCarryTheBreakdown()
    {
        var executor = Source("RiveTT.Tools", "CodeExecution", "RoslynExecutor.cs");
        foreach (var key in new[] { "deletedTopLevelCount", "deletedAuxiliaryCount", "deletedUnclassifiedCount", "deletedByClass" })
            Assert.Contains(key, executor);
        Assert.Equal(2, executor.Split("AddDeletedBreakdown(report);").Length - 1);
        Assert.Contains("ClassSnapshotLimit", executor);
    }

    // --- 4. groups -----------------------------------------------------------------------------------------------

    [Fact]
    public void ManageModelGroupsUngroupsAndRegroupsAndTheWrapperPublishesElementIds()
    {
        var tool = Source("RiveTT.Tools", "Elements", "ManageModelGroupsTool.cs");
        Assert.Contains("\"ungroup\" => Ungroup(doc, input)", tool);
        Assert.Contains("\"regroup\" => Regroup(doc, input)", tool);
        Assert.Contains("doc.FamilyCreate.NewGroup(elementIds)", tool);
        Assert.Contains("doc.Create.NewGroup(elementIds)", tool);
        Assert.Contains("input[\"elementIds\"]", tool);
        Assert.Contains("releasedCount", tool);
        Assert.Contains("notFoundIds", tool);
        var wrapper = Source("RiveTT.Server", "Tools", "StoreyAndGroupTools.cs");
        Assert.Contains("[\"elementIds\"] = elementIdsArray", wrapper);
        Assert.Contains("ungroup | regroup", wrapper);
    }

    // --- 5. sandbox ---------------------------------------------------------------------------------------------------

    [Fact]
    public void TheDynamicRefusalNamesTheAlternatives()
    {
        var refusal = CodeSandbox.Validate("dynamic x = 1; return x;");
        Assert.NotNull(refusal);
        Assert.False(refusal!.Success);
        Assert.Contains("dynamic", refusal.Error!.Message);
        Assert.Contains("named tuples", refusal.Error.Suggestion);
        Assert.Contains("anonymous types", refusal.Error.Suggestion);
        Assert.Equal("", CodeSandboxV2.SuggestionFor(new[] { "System.IO" }));
    }
}
