using System.Linq;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// The reading get_selected_elements now attaches to a selection of curves. The case that
/// went wrong in the field (2026-09-24): four detail lines drawn as a closed outline, taken
/// for an axis, and a swale built along them.
/// </summary>
public class CurveChainsTests
{
    private static CurveChains.Segment S(long id, double x0, double y0, double x1, double y1) =>
        new(id, x0, y0, x1, y1, System.Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));

    [Fact]
    public void FourLinesDrawnAsARectangleAreOneClosedContour()
    {
        // Drawn in any order and direction: 12 m x 2 m outline.
        var chains = CurveChains.Analyze(new[]
        {
            S(1, 0, 0, 12000, 0),
            S(3, 12000, 2000, 0, 2000),
            S(2, 12000, 0, 12000, 2000),
            S(4, 0, 0, 0, 2000),
        });

        var chain = Assert.Single(chains);
        Assert.True(chain.Closed);
        Assert.Equal(28000, chain.LengthMm);
        Assert.Equal(24.0, chain.AreaM2);
        Assert.Equal(12000, chain.WidthMm);
        Assert.Equal(2000, chain.HeightMm);
        Assert.Contains("CONTOUR", CurveChains.Reading(chains));
    }

    [Fact]
    public void APolylineIsAnOpenChain()
    {
        var chains = CurveChains.Analyze(new[]
        {
            S(1, 0, 0, 5000, 0),
            S(2, 5000, 0, 9000, 3000),
            S(3, 9000, 3000, 15000, 3000),
        });

        var chain = Assert.Single(chains);
        Assert.False(chain.Closed);
        Assert.Null(chain.AreaM2);
        Assert.Contains("open chain", CurveChains.Reading(chains));
    }

    [Fact]
    public void EndPointsWithinToleranceStillConnect()
    {
        // A sketch snapped a fraction of a millimetre off still closes.
        var chains = CurveChains.Analyze(new[]
        {
            S(1, 0, 0, 1000, 0),
            S(2, 1000.4, 0.3, 1000, 1000),
            S(3, 1000, 1000, 0, 1000),
            S(4, 0, 1000, 0.2, -0.4),
        });
        Assert.True(Assert.Single(chains).Closed);
    }

    [Fact]
    public void SeparateGroupsAreSeparateChains()
    {
        var chains = CurveChains.Analyze(new[]
        {
            S(1, 0, 0, 1000, 0), S(2, 1000, 0, 1000, 1000), S(3, 1000, 1000, 0, 1000), S(4, 0, 1000, 0, 0),
            S(10, 5000, 0, 8000, 0),
        });

        Assert.Equal(2, chains.Count);
        Assert.Single(chains, c => c.Closed);
        Assert.Single(chains, c => !c.Closed && c.ElementIds.SequenceEqual(new long[] { 10 }));
        var reading = CurveChains.Reading(chains);
        Assert.Contains("1 closed loop", reading);
        Assert.Contains("1 open chain", reading);
    }

    [Fact]
    public void ATJunctionIsNotALoop()
    {
        var chains = CurveChains.Analyze(new[]
        {
            S(1, 0, 0, 2000, 0),
            S(2, 1000, 0, 1000, 1000),
            S(3, 2000, 0, 3000, 0),
        });
        Assert.All(chains, chain => Assert.False(chain.Closed));
    }
}
