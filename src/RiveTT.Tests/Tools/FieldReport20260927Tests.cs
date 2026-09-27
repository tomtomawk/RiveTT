using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Design;
using RiveTT.Tools.Utilities;
using RiveTT.Tools.Views;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// The defects found while drawing a typical floor with 0.6.0 on 2026-09-27 (docs/CHANGELOG_0.6.1.md).
/// The pure parts are tested for real; what only a live Revit session can prove is locked on the
/// source, and listed for live verification in docs/CHANGELOG_0.3.0.md §6.
/// </summary>
public class FieldReport20260927Tests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    // --- place_in_room: the wall behind the footprint, not the room's extent -------------------

    /// <summary>The T3 living room of the session: an L, whose top is the entrance notch.</summary>
    private static readonly Pt[] LShapedLivingRoom =
    {
        new(180, 180), new(6400, 180), new(6400, 4230), new(5100, 4230), new(5100, 4180), new(180, 4180)
    };

    [Fact]
    public void GoingNorthInAnLShapedRoomMeetsThePartitionBehindTheSinkNotTheFarEndOfTheL()
    {
        // Sink strip x 2500..3700: the partition there is at 4180; the room's extreme Y is 4230.
        var wall = PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(3100, 3800), 'N', 2501, 3699);
        Assert.Equal(4180, wall);
        // In the entrance strip the far end is the right answer.
        Assert.Equal(4230, PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(5700, 3800), 'N', 5200, 6300));
    }

    [Fact]
    public void TheOtherThreeDirectionsFindTheirWalls()
    {
        Assert.Equal(180, PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(3100, 2000), 'S', 2501, 3699));
        Assert.Equal(6400, PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(3100, 2000), 'E', 1000, 1600));
        Assert.Equal(180, PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(3100, 2000), 'W', 1000, 1600));
        // A strip that runs into the notch meets its vertical edge first going east.
        Assert.Equal(5100, PlanGeometry.FirstBoundary(LShapedLivingRoom, new Pt(3100, 4200), 'E', 4185, 4225));
    }

    [Fact]
    public void PlaceInRoomPushesAgainstTheFirstWallAndDefaultsToFlush()
    {
        var tool = Source("RiveTT.Tools", "Elements", "PlaceInRoomTool.cs");
        Assert.Contains("PlanGeometry.FirstBoundary(outline, target, plus", tool);
        Assert.DoesNotContain("dy = rMaxY - marginMm - fMaxY", tool);
        Assert.Contains("input[\"marginMm\"]?.Value<double>() ?? 0;", tool);
        Assert.Contains("footprintBasis", tool);
        Assert.Contains("cover less than half of what", tool);
        Assert.Contains("The insertion point (", tool);

        var server = Source("RiveTT.Server", "Tools", "ArchitectureTools.cs");
        Assert.Contains("double marginMm = 0,", server);
        Assert.Contains("p[\"footprintBasis\"] = footprintBasis", server);
    }

    // --- create_stair: Revit's strict ceiling ---------------------------------------------------

    private static readonly StairRunPlanner.RunInput Down = new(7200, 3360, 7200, 1400); // 7 treads
    private static readonly StairRunPlanner.RunInput Up = new(8800, 1400, 8800, 3360);   // 7 treads

    [Fact]
    public void AHeightJustAboveARiserBoundaryNeedsOneMoreRiserAsRevitSays()
    {
        // R+2 - R+1 in the session's model: 5 610.000000000197 - 2 889.99999999999.
        var height = 5610.000000000197 - 2889.9999999999895;
        var plan = StairRunPlanner.Build(new[] { Down, Up }, height, 170, 280, 1200);

        Assert.Equal(17, plan.DesiredRisers);
        Assert.Equal(16, plan.EstimatedTotalRisers);
        Assert.Contains(plan.Warnings, w => w.Contains("riser boundary"));
        // The preview now says what the real call found: lengthen, not "16 risers, fine".
        Assert.Contains(plan.Warnings, w => w.Contains("Lengthen the runs by about 280 mm"));
    }

    [Fact]
    public void AnExactHeightKeepsItsRiserCountAndNeedsNoWarning()
    {
        var plan = StairRunPlanner.Build(new[] { Down, Up }, 2720, 170, 280, 1200);
        Assert.Equal(16, plan.DesiredRisers);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void TheRatioComputedInFeetWinsOverTheMillimetreOne()
    {
        // An exact ratio in feet must not become 16.000000000000004 through the conversion.
        var plan = StairRunPlanner.Build(new[] { Down, Up }, 2720.0000000000005, 170, 280, 1200, heightToMaxRiser: 16.0);
        Assert.Equal(16, plan.DesiredRisers);
    }

    [Fact]
    public void TheRealStairCallTrustsRevitsRiserCountNotTheEstimate()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreateStairTool.cs");
        Assert.DoesNotContain("warnings.AddRange(plan.Warnings)", tool);
        Assert.Contains("created.DesiredRisersNumber", tool);
        Assert.Contains("heightFt / maxRiserFt", tool);
    }

    // --- create_room_separation_line: the plan's level -----------------------------------------

    [Fact]
    public void RoomSeparationLinesAreDrawnAtThePlansLevel()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreateCurveElementTools.cs");
        Assert.Contains("var elevationFt = plan.GenLevel?.Elevation ?? origin!.Z;", tool);
        Assert.Contains("was ignored: room separation lines are drawn at the", tool);
        Assert.DoesNotContain("z sets the sketch plane elevation", tool);
    }

    // --- capture_view: fit-to-page arithmetic ---------------------------------------------------

    [Fact]
    public void AnImageTallerThanItsCropIsScaledByTheWidthAndCentredVertically()
    {
        // The session's second capture: crop 14 460 x 3 500 mm from (0, 7300), image 1800 x 754.
        var frame = new CaptureMapping.ViewFrame(true, 0, 7300, 14460, 3500, "FloorPlan", UpIsNorth: true);
        var mapping = JObject.FromObject(CaptureMapping.Compute(frame, 1800, 754));

        // Measured on the image: 8.03 mm/px and about 160 px of margin above and below.
        Assert.Equal(8.033, mapping["mmPerPixel"]!.Value<double>(), 3);
        var crop = mapping["cropPx"]!.Values<double>().ToArray();
        Assert.Equal(0, crop[0], 1);
        Assert.InRange(crop[1], 158, 160);
        Assert.Equal(1800, crop[2], 1);
        var origin = mapping["originTopLeftMm"]!.Values<double>().ToArray();
        Assert.Equal(0, origin[0], 1);
        Assert.InRange(origin[1], 7300 + 158 * 8.033, 7300 + 160 * 8.033);
        Assert.False(mapping["verified"]!.Value<bool>());
    }

    [Fact]
    public void TheTemporaryCaptureViewShrinksTheAnnotationCrop()
    {
        var tool = Source("RiveTT.Tools", "Views", "CaptureViewTool.cs");
        Assert.Contains("MinimiseAnnotationCropOffsets(shape)", tool);
        Assert.DoesNotContain("(mmPerPixelX + mmPerPixelY) / 2", tool);
    }

    // --- create_point_based_element: flips on every category -----------------------------------

    [Fact]
    public void AFlipRequestedOnAnEtelIsAppliedOrRefusedAloud()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreatePointBasedElementTool.cs");
        Assert.Contains("else if (facingFlipped)", tool);
        Assert.Contains("cannot flip \" +", tool);
        Assert.Contains("instance.CanFlipFacing", tool);
    }

    // --- describe_family: from the insertion point ---------------------------------------------

    [Fact]
    public void FamilyExtentsAreMeasuredFromTheInsertionPoint()
    {
        var tool = Source("RiveTT.Tools", "Elements", "DescribeFamilyTool.cs");
        Assert.Contains("InsertionFrame(instance)", tool);
        Assert.Contains("frame.Origin = location.Point;", tool);
        Assert.Contains("geometryOriginOffsetMm", tool);
        Assert.DoesNotContain("instance.GetTransform().Inverse : Transform.Identity", tool);
        // The probe of an unplaced type is always rolled back.
        Assert.Contains("probe.RollBack()", tool);
    }

    // --- The server's icon and process name -----------------------------------------------------

    [Fact]
    public void TheServerIconHasClassicBitmapsForEverySmallSizeAndAPngAt256()
    {
        var bytes = File.ReadAllBytes(RepositoryFile.Path("src", "RiveTT.Server", "rivett-server.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new System.Collections.Generic.List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var size = bytes[entry] == 0 ? 256 : bytes[entry];
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            var isPng = bytes[offset] == 0x89 && bytes[offset + 1] == (byte)'P';
            // Task Manager and the taskbar read the small entries: PNG is only safe at 256 px.
            Assert.Equal(size >= 256, isPng);
            sizes.Add(size);
        }
        Assert.Contains(16, sizes);
        Assert.Contains(24, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(256, sizes);
    }

    [Fact]
    public void TheServerProcessCarriesItsIconAndAReadableDescription()
    {
        var project = Source("RiveTT.Server", "RiveTT.Server.csproj");
        Assert.Contains("<ApplicationIcon>rivett-server.ico</ApplicationIcon>", project);
        Assert.Contains("<AssemblyTitle>", project);
    }
}
