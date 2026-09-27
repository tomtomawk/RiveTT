using System;
using System.IO;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Source guards for the defects of the 2026-09-24 field session (docs/retex/2026-09-24) whose
/// behaviour only a live Revit session can prove. Each assertion names the defect it locks;
/// the live checks are listed in docs/CHANGELOG_0.3.0.md §6.
/// </summary>
public class FieldReport20260924SourceTests
{
    private static string Tool(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src", "RiveTT.Tools" }.Concat(parts).ToArray()));

    [Fact]
    public void LevelBasedFurnitureIsMeasuredAndPutBackAtItsElevation()
    {
        // 04 §2.2: +2.89 m at R+1, the level elevation counted twice.
        var source = Tool("Elements", "CreatePointBasedElementTool.cs");
        Assert.Contains("ElevationCorrection.Apply(doc, instance, locationPoint.Z)", source);
        Assert.Contains("zCorrectedByMm", source);
        // A z meant relative and sent absolute is refused rather than buried a storey down.
        Assert.Contains("offsetFromLevelMm <= -1000", source);
    }

    [Fact]
    public void AnInvalidHostIsAFailureNotAFreeStandingDoor()
    {
        var source = Tool("Elements", "CreatePointBasedElementTool.cs");
        Assert.DoesNotContain("is not a valid wall. Using auto-detection.", source);
        Assert.Contains("HostWallFinder.Find(", source);
    }

    [Fact]
    public void BatchesReportWhichItemFailedByItsKey()
    {
        foreach (var file in new[] { "CreatePointBasedElementTool.cs", "CreateLineBasedElementTool.cs" })
        {
            var source = Tool("Elements", file);
            Assert.Contains("failed.Add(new", source);
            Assert.Contains("keyed[\"key\"]", source);
        }
    }

    [Fact]
    public void RoomTagsGetTheRequestedType()
    {
        // The type was looked up, then never applied: NewRoomTag places the default one.
        var source = Tool("Annotations", "TagRoomsTool.cs");
        Assert.Contains("tag.ChangeTypeId(tagType.Id)", source);
        Assert.Contains("onePerParameter", source);
    }

    [Fact]
    public void CaptureLeavesNothingInTheModel()
    {
        var source = Tool("Views", "CaptureViewTool.cs");
        var finallyAt = source.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyAt > 0);
        Assert.Contains("group.RollBack()", source.Substring(finallyAt));
        Assert.Contains("Directory.Delete(directory, recursive: true)", source.Substring(finallyAt));
        Assert.Contains("File.ReadAllBytes(file)", source);
        Assert.Contains("[ToolSafety(true, false)]", source);
    }

    [Fact]
    public void ReadonlyScriptsAreAlwaysRolledBack()
    {
        var source = Tool("CodeExecution", "RoslynExecutor.cs");
        var branch = source.IndexOf("case \"readonly\":", StringComparison.Ordinal);
        Assert.True(branch > 0);
        var body = source.Substring(branch, source.IndexOf("default: // auto", branch, StringComparison.Ordinal) - branch);
        Assert.Contains("finally", body);
        Assert.Contains("new[] { uiGroup, txGroup }", body);
        Assert.Contains("group.RollBack()", body);
        Assert.DoesNotContain("Assimilate", body);
        Assert.DoesNotContain("Commit()", body);
    }

    [Fact]
    public void WallsAreAttachedOneTransactionEach()
    {
        var source = Tool("Elements", "AttachWallsTool.cs");
        Assert.Contains("foreach (var wallId in wallIds)", source);
        Assert.Contains("new Transaction(doc, $\"RiveTT: {action} wall {wallId}\")", source);
        Assert.Contains("relatedElements", source);
    }

    [Fact]
    public void FootprintsAreNeverTheBoundingBox()
    {
        // 04 §2.3: the box of a 1.40 x 1.90 bed measured 2.85 x 3.90 m.
        var source = Tool("Utilities", "FamilyExtents.cs");
        Assert.Contains("solid.Volume > 1e-9", source);
        Assert.DoesNotContain(".get_BoundingBox(", source);
    }
}
