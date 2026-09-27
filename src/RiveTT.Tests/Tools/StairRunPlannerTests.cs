using System.IO;
using System.Linq;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// The arithmetic create_stair now shares between its preview and its build, on the case that
/// broke in the field (2026-09-24, bug 1.1): RDC to R+1, 2 890 mm, two runs of a U stair.
/// </summary>
public class StairRunPlannerTests
{
    private static readonly StairRunPlanner.RunInput RunA = new(0, 0, 2240, 0);        // 8 treads of 280
    private static readonly StairRunPlanner.RunInput RunB = new(2240, 1200, 280, 1200); // 7 treads, back along +Y offset

    [Fact]
    public void TwoRunsOfAUStairAreStackedAndReachTheTopLevel()
    {
        var plan = StairRunPlanner.Build(new[] { RunA, RunB }, 2890, 170, 280, 1100);

        Assert.Equal(17, plan.DesiredRisers);
        Assert.Equal(170, plan.RiserHeightMm);
        Assert.Equal(9, plan.Runs[0].EstimatedRisers);
        Assert.Equal(8, plan.Runs[1].EstimatedRisers);
        // The second run starts where the first one ends — not at 0, which is the defect.
        Assert.Equal(0, plan.Runs[0].EstimatedBaseMm);
        Assert.Equal(plan.Runs[0].EstimatedTopMm, plan.Runs[1].EstimatedBaseMm);
        Assert.Equal(1530, plan.Runs[0].EstimatedTopMm);
        Assert.Equal(2890, plan.Runs[1].EstimatedTopMm);
        Assert.Equal(17, plan.EstimatedTotalRisers);
        Assert.Empty(plan.Warnings);
        Assert.Empty(plan.Problems);
        Assert.Equal("U", plan.Junctions.Single().Layout);
    }

    [Fact]
    public void ShortRunsAreReportedWithTheLengthToAdd()
    {
        var shortB = new StairRunPlanner.RunInput(2240, 1200, 840, 1200); // 5 treads instead of 7
        var plan = StairRunPlanner.Build(new[] { RunA, shortB }, 2890, 170, 280, 1100);

        Assert.Equal(15, plan.EstimatedTotalRisers);
        var warning = Assert.Single(plan.Warnings);
        Assert.Contains("below the top level", warning);
        Assert.Contains("560 mm", warning); // 2 missing risers x 280 mm
    }

    [Fact]
    public void LongRunsAreReportedAsOvershooting()
    {
        var longB = new StairRunPlanner.RunInput(2240, 1200, -280, 1200); // 9 treads
        var plan = StairRunPlanner.Build(new[] { RunA, longB }, 2890, 170, 280, 1100);

        Assert.Equal(19, plan.EstimatedTotalRisers);
        Assert.Contains("overshoot", Assert.Single(plan.Warnings));
    }

    [Theory]
    [InlineData(2240, 0, 4200, 0, "straight", true)]      // straight, 0 gap: landing needed further but bridgeable
    [InlineData(2790, 550, 2790, 3000, "L", true)]       // L at the corner
    [InlineData(2240, 1200, 280, 1200, "U", true)]       // U, offset by width + well
    [InlineData(9000, 9000, 12000, 9000, "straight", false)] // far away: no landing bridges it
    [InlineData(1000, 0, 3000, 0, "straight", false)]    // starts behind the end of run A: overlap
    public void JunctionsAreClassifiedAndCheckedForAPossibleLanding(
        double x0, double y0, double x1, double y1, string layout, bool bridgeable)
    {
        var junction = StairRunPlanner.Classify(0, RunA, new StairRunPlanner.RunInput(x0, y0, x1, y1), 1100);
        Assert.Equal(layout, junction.Layout);
        Assert.Equal(bridgeable, junction.Bridgeable);
        if (!bridgeable) Assert.False(string.IsNullOrWhiteSpace(junction.Problem));
    }

    [Fact]
    public void TheBuilderStacksRunsInsteadOfPlacingThemAtTheBaseElevation()
    {
        // Source guard for the defect itself: the location line of a run after the first one
        // takes the previous run's top elevation, and the landing is checked before creation.
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Tools", "Elements", "CreateStairTool.cs"));
        Assert.Contains("stairsBaseFt + previousRun.TopElevation", source);
        Assert.Contains("StairsLanding.CanCreateAutomaticLanding(", source);
        Assert.Contains("requireLandings", source);
        // A scope rolled back by its own failure processing must not report success.
        Assert.Contains("if (created == null)", source);
    }
}
