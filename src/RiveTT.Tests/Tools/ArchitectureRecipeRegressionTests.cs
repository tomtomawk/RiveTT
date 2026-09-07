using Newtonsoft.Json.Linq;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

public class ArchitectureRecipeRegressionTests
{
    [Fact]
    public void PointBatchesExposeSkippedCountAndStructuredWarnings()
    {
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Elements", "CreatePointBasedElementTool.cs"));
        Assert.Contains("skipped = dataToken.Count() - (dryRun ? details.Count : createdIds.Count)", source);
        Assert.Contains("            warnings,", source);
    }

    [Fact]
    public void SystemTypeDiscoveryReportsExcludedLoadableMatches()
    {
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Project", "ListSystemTypesTool.cs"));
        Assert.Contains("excludedLoadableTypeCount = includeLoadable ? 0 : types.Count(type => type is FamilySymbol)", source);
        Assert.Contains("Set includeLoadable: true", source);
        Assert.True(source.IndexOf("var needle =", StringComparison.Ordinal) < source.IndexOf("var excludedLoadableTypeCount", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("K", 0, "alphabetic", "K")]
    [InlineData("RK", 0, "alphabetic", "RK")]
    [InlineData("RK", 2, "alphabetic", "RM")]
    [InlineData("Z", 1, "alphabetic", "AA")]
    [InlineData("ZZ", 1, "alphabetic", "AAA")]
    [InlineData("7", 2, "numeric", "9")]
    [InlineData("01", 1, "numeric", "02")]
    [InlineData("A1", 1, "numeric", "A2")]
    public void GridSequencesPreserveStartAndIncrementWithoutFallback(string start, int index, string style, string expected)
        => Assert.Equal(expected, GridLabels.Generate(start, index, style));

    [Theory]
    [InlineData("P", "numeric")]
    [InlineData("A'", "alphabetic")]
    [InlineData("", "alphabetic")]
    [InlineData("A", "unknown")]
    public void UnsupportedGridLabelsAreRejectedInsteadOfSilentlyBecomingDefaults(string start, string style)
        => Assert.Throws<ArgumentException>(() => GridLabels.Generate(start, 0, style));

    [Fact]
    public void RoomFlagsIndependentlyControlPlacementAndEnclosure()
    {
        foreach (var unplaced in new[] { false, true })
        foreach (var unenclosed in new[] { false, true })
        {
            Assert.True(RoomInclusion.Include(true, 15, unplaced, unenclosed));
            Assert.Equal(unenclosed, RoomInclusion.Include(true, 0, unplaced, unenclosed));
            Assert.Equal(unplaced, RoomInclusion.Include(false, 0, unplaced, unenclosed));
        }
    }

    [Fact]
    public void SurfaceAndLineUseTheSameExplicitLevelContract()
    {
        var surfaceSource = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Elements", "CreateSurfaceBasedElementTool.cs"));
        Assert.Contains("LineBaseConstraint.Parse(item)", surfaceSource);
        Assert.DoesNotContain("baseLevelMm", surfaceSource);
        var constraint = LineBaseConstraint.Parse(JObject.Parse("""{"baseLevel":512913,"baseOffset":2500}"""));
        Assert.Equal(512913L, constraint.LevelId);
        Assert.Equal(2500, constraint.RelativeOffsetMm(2890));
        var absolute = LineBaseConstraint.Parse(JObject.Parse("""{"baseElevationMm":2890,"baseOffset":2500}"""));
        Assert.Null(absolute.LevelId);
        Assert.Equal(2500, absolute.RelativeOffsetMm(2890));
        Assert.Throws<ArgumentException>(() => LineBaseConstraint.Parse(
            JObject.Parse("""{"baseLevelId":512913,"baseElevationMm":2890}""")));
    }

    [Fact]
    public void SurfaceIdsAreAddedOnlyAfterSuccessfulCommit()
    {
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Elements", "CreateSurfaceBasedElementTool.cs"));
        Assert.True(source.IndexOf("tx.Commit()", StringComparison.Ordinal) <
                    source.IndexOf("createdIds.Add(", StringComparison.Ordinal));
        Assert.Contains("baseElevationMm = actualOffset.HasValue", source);
    }

    [Fact]
    public void OpeningTotalsAreDeduplicatedBeforePerRoomTruncationAndParametersKeepTheirUnits()
    {
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Elements", "GetRoomOpeningsTool.cs"));
        Assert.Contains("distinctDoors.UnionWith(allDoors.", source);
        Assert.Contains("distinctWindows.UnionWith(allWindows.", source);
        Assert.DoesNotContain("totalDoors   += allDoors.Count", source);
        Assert.Contains("ParameterValueFormatter.Format(parameter)", source);
    }
}
