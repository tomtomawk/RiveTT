using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Read-only diagnosis of a room whose enclosure is wrong: where the boundary leaks.
///
/// A room that came out at 170 m² instead of 27 m² took about fifteen calls to explain — probes
/// with create_room(dryRun) point by point, then a hand computation of a wall's volume to guess
/// the opening (field report 0.5.2). Revit's own warnings say THAT rooms overlap, never WHERE.
/// This tool answers where, from two sources:
///   1. the room's boundary loops, with each segment's element and coordinates, and the
///      discontinuities between consecutive segments;
///   2. the room-bounding walls and separation lines around the room's point whose END is joined
///      to nothing — the free ends, which are where a leak starts.
/// The second is a HEURISTIC (an end is "joined" when another bounding curve passes within half
/// that wall's thickness plus a tolerance of it): it names candidates, nearest first, and does not
/// prove the leak.
/// </summary>
[ToolSafety(true, false)]
public sealed class DiagnoseRoomEnclosureTool : IRiveTTTool
{
    public string Name => "diagnose_room_enclosure";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Read-only: shows where a room's enclosure is open or wrong. Returns the room's boundary loops (each segment's " +
        "element id, category and start/end in mm), the discontinuities between segments, and the room-bounding walls and " +
        "separation lines within radiusMm of the room point whose end is joined to nothing (freeEnds, nearest first) — where " +
        "a leak starts. freeEnds is a heuristic that names candidates; it does not prove the leak. Works on an unenclosed " +
        "room (area 0) too, which has no boundary loop.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var roomId = input["roomId"]?.Value<long>() ?? 0;
        var radiusMm = input["radiusMm"]?.Value<double>() ?? 6000;
        var toleranceMm = input["toleranceMm"]?.Value<double>() ?? 5;
        if (roomId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "roomId is required",
                suggestion: "Read room ids with filter_elements (OST_Rooms) or get_elements_in_spatial_volume.");

        try
        {
            if (doc.GetElement(new ElementId(roomId)) is not Room room)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"Element {roomId} is not a room");

            var areaM2 = Math.Round(room.Area * 0.09290304, 3);
            var point = (room.Location as LocationPoint)?.Point;
            var level = doc.GetElement(room.LevelId) as Level;

            var loops = new List<object>();
            var gaps = new List<object>();
            IList<IList<BoundarySegment>>? boundary = null;
            try
            {
                boundary = room.GetBoundarySegments(new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                });
            }
            catch
            {
                // An unplaced or unenclosed room has none: reported below through enclosed=false.
            }

            var toleranceFt = toleranceMm / MmPerFoot;
            var loopIndex = 0;
            foreach (var loop in boundary ?? (IList<IList<BoundarySegment>>)new List<IList<BoundarySegment>>())
            {
                var segments = new List<object>();
                for (var i = 0; i < loop.Count; i++)
                {
                    var curve = loop[i].GetCurve();
                    var element = doc.GetElement(loop[i].ElementId);
                    segments.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(loop[i].ElementId),
                        category = element?.Category?.Name,
                        name = element?.Name,
                        startMm = Mm(curve.GetEndPoint(0)),
                        endMm = Mm(curve.GetEndPoint(1)),
                        lengthMm = Math.Round(curve.Length * MmPerFoot, 1)
                    });

                    var next = loop[(i + 1) % loop.Count].GetCurve();
                    var gap = curve.GetEndPoint(1).DistanceTo(next.GetEndPoint(0));
                    if (gap > toleranceFt)
                        gaps.Add(new
                        {
                            loop = loopIndex,
                            gapMm = Math.Round(gap * MmPerFoot, 1),
                            fromMm = Mm(curve.GetEndPoint(1)),
                            toMm = Mm(next.GetEndPoint(0)),
                            afterElementId = ToolHelpers.GetElementIdValue(loop[i].ElementId),
                            beforeElementId = ToolHelpers.GetElementIdValue(loop[(i + 1) % loop.Count].ElementId)
                        });
                }
                loops.Add(new { loop = loopIndex, segmentCount = segments.Count, segments });
                loopIndex++;
            }

            var freeEnds = point != null && level != null
                ? FindFreeEnds(doc, level, point, radiusMm / MmPerFoot, toleranceFt)
                : new List<object>();

            var enclosed = room.Area > 1e-6;
            var notes = new List<string>();
            if (!enclosed)
                notes.Add("The room is NOT enclosed (area 0): it has no boundary loop. Read freeEnds, nearest first, for where the boundary stops.");
            if (point == null)
                notes.Add("The room is not placed (no location point): freeEnds could not be searched.");
            if (gaps.Count > 0)
                notes.Add($"{gaps.Count} discontinuity(ies) between consecutive boundary segments: see gaps.");

            return RiveTTResult<object>.Ok(new
            {
                roomId,
                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
                roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(),
                levelName = level?.Name,
                enclosed,
                areaM2,
                roomPointMm = point == null ? null : Mm(point),
                loopCount = loops.Count,
                loops,
                gaps,
                freeEnds,
                searchRadiusMm = radiusMm,
                toleranceMm,
                heuristic = "freeEnds names candidates (a bounding wall or separation line end joined to nothing), nearest first; it does not prove the leak.",
                notes
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"diagnose_room_enclosure could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the room id with a read tool before retrying. The full call, its duration and "
                    + "this error are in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static object Mm(XYZ p) => new { x = Math.Round(p.X * MmPerFoot, 1), y = Math.Round(p.Y * MmPerFoot, 1) };

    private sealed record BoundingCurve(long ElementId, string Kind, string? TypeName, Curve Curve, double HalfWidthFeet);

    /// <summary>
    /// The ends of room-bounding walls and separation lines on the room's level, within the radius of
    /// the room point, that no OTHER bounding curve reaches (within that curve's half-thickness plus
    /// the tolerance). Nearest to the room point first.
    /// </summary>
    private static List<object> FindFreeEnds(Document doc, Level level, XYZ roomPoint, double radiusFeet, double toleranceFt)
    {
        var curves = new List<BoundingCurve>();

        foreach (var wall in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls)
                     .WhereElementIsNotElementType().OfType<Wall>())
        {
            if (wall.LevelId != level.Id) continue;
            if (wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.AsInteger() == 0) continue;
            if (wall.Location is not LocationCurve location) continue;
            curves.Add(new BoundingCurve(ToolHelpers.GetElementIdValue(wall.Id), "wall",
                wall.WallType?.Name, location.Curve, wall.Width / 2));
        }

        foreach (var line in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RoomSeparationLines)
                     .WhereElementIsNotElementType().OfType<ModelCurve>())
        {
            if (line.GeometryCurve != null)
                curves.Add(new BoundingCurve(ToolHelpers.GetElementIdValue(line.Id), "roomSeparationLine", null,
                    line.GeometryCurve, 0));
        }

        var free = new List<(double Distance, object Item)>();
        foreach (var candidate in curves)
        {
            for (var end = 0; end <= 1; end++)
            {
                var p = candidate.Curve.GetEndPoint(end);
                var flat = new XYZ(p.X, p.Y, roomPoint.Z);
                var distanceToRoom = flat.DistanceTo(roomPoint);
                if (distanceToRoom > radiusFeet) continue;

                var joined = curves.Any(other =>
                    other.ElementId != candidate.ElementId
                    && DistanceInPlan(other.Curve, p) <= other.HalfWidthFeet + toleranceFt);
                if (joined) continue;

                free.Add((distanceToRoom, new
                {
                    elementId = candidate.ElementId,
                    kind = candidate.Kind,
                    typeName = candidate.TypeName,
                    end = end == 0 ? "start" : "end",
                    pointMm = Mm(p),
                    distanceToRoomPointMm = Math.Round(distanceToRoom * MmPerFoot, 1)
                }));
            }
        }

        return free.OrderBy(entry => entry.Distance).Take(20).Select(entry => entry.Item).ToList();
    }

    /// <summary>Distance from the point to the curve, measured in plan (both flattened to the point's Z).</summary>
    private static double DistanceInPlan(Curve curve, XYZ point)
    {
        var a = curve.GetEndPoint(0);
        var b = curve.GetEndPoint(1);
        var from = new XYZ(a.X, a.Y, point.Z);
        var to = new XYZ(b.X, b.Y, point.Z);
        // An arc wall is measured on its chord: the heuristic is for straight partitions.
        if (from.DistanceTo(to) < 1e-9) return from.DistanceTo(point);
        return Line.CreateBound(from, to).Distance(point);
    }
}
