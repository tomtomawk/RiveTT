using System;
using System.IO;
using Xunit;

namespace RevitCortex.Tests.Tools;

public sealed class RoadmapImplementationTests
{
    [Fact]
    public void EveryCreateLevelWriteActionChecksDryRunBeforeTransaction()
    {
        var source = ReadSource("RevitCortex.Tools", "Elements", "CreateLevelTool.cs");
        AssertDryRunBeforeTransaction(source, "SetLevel", "RenameLevel");
        AssertDryRunBeforeTransaction(source, "RenameLevel", "DeleteLevel");
        AssertDryRunBeforeTransaction(source, "DeleteLevel", "ResolveLevel");
    }

    [Fact]
    public void WallCreationPreviewsAndReturnsActualConstraintGeometry()
    {
        var source = ReadSource("RevitCortex.Tools", "Elements", "CreateLineBasedElementTool.cs");
        Assert.Contains("ToolHelpers.GetDryRun(input)", source);
        Assert.Contains("resultingBaseElevationMm", source);
        Assert.Contains("actualBaseOffset", source);
        Assert.Contains("coordinates = \"absolute_project_coordinates_mm\"", source);
        Assert.Contains("offsets = \"relative_to_constraint_level_mm\"", source);
    }

    [Fact]
    public void ElementSearchImplementsVersionedPaginationAndResponseModes()
    {
        var source = ReadSource("RevitCortex.Tools", "Elements", "AIElementFilterTool.cs");
        Assert.Contains("nextCursor", source);
        Assert.Contains("appliedLimit", source);
        Assert.Contains("EncodeCursor(session.DocumentVersion", source);
        Assert.Contains("summary\" or \"idsonly\" or \"details", source);
    }

    [Fact]
    public void BulkScopesSupportStableSelectionTokensAndExplicitIds()
    {
        var resolver = ReadSource("RevitCortex.Tools", "Utilities", "ElementScopeResolver.cs");
        Assert.Contains("selectionToken", resolver);
        Assert.Contains("savedSelectionName", resolver);
        Assert.Contains("last_filter", resolver);
        Assert.Contains("elementIds", resolver);
        Assert.Contains("expiresAtUtc", ReadSource("RevitCortex.Tools", "Elements", "CaptureSelectionTool.cs"));
    }

    [Fact]
    public void CsvSyncUsesCentralParameterResolverAndCompactDiagnostics()
    {
        var source = ReadSource("RevitCortex.Tools", "Parameters", "SyncCsvParametersTool.cs");
        Assert.Contains("ParameterLookup.FindParameter", source);
        Assert.Contains("parameterMap", source);
        Assert.Contains("unmatchedHeaders", source);
        Assert.Contains("includeDetails", source);
        Assert.Contains("sampleLimit", source);
    }

    [Fact]
    public void NewStoreyWallAndGroupToolsAreDedicatedAndPreviewFirst()
    {
        foreach (var parts in new[]
                 {
                     new[] { "Project", "DuplicateStoreyTool.cs" },
                     new[] { "Elements", "DetachWallConstraintTool.cs" },
                     new[] { "Elements", "ManageModelGroupsTool.cs" }
                 })
        {
            var source = ReadSource("RevitCortex.Tools", parts);
            Assert.Contains("ToolHelpers.GetDryRun(input)", source);
            Assert.Contains("[ToolSafety(false, true)]", source);
        }
    }

    [Fact]
    public void UnsafeDocumentLifecycleOperationsRemainExplicitlyUnexposed()
    {
        var caps = ReadSource("RevitCortex.Tools", "Meta", "GetServerCapabilitiesTool.cs");
        Assert.Contains("open_document is not exposed", caps);
        Assert.Contains("edit_family is not exposed", caps);
    }

    private static void AssertDryRunBeforeTransaction(string source, string method, string nextMethod)
    {
        var start = source.IndexOf($"private static CortexResult<object> {method}(", StringComparison.Ordinal);
        var end = source.IndexOf("\n    private static ", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate {method}");
        var body = source.Substring(start, end - start);
        Assert.True(body.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal) >= 0);
        Assert.True(body.IndexOf("ToolHelpers.GetDryRun(input)", StringComparison.Ordinal) <
                    body.IndexOf("new Transaction(", StringComparison.Ordinal));
    }

    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 5];
        parts[0] = parts[1] = parts[2] = parts[3] = "..";
        parts[4] = project;
        relativeParts.CopyTo(parts, 5);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts)));
    }
}
