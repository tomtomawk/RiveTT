using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RiveTT.Core.Design;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// The outline of a room in plan millimetres, read at the FINISH face — where furniture is
/// pushed against and where free circles are measured. Shared by validate_dwelling and
/// place_in_room so that both agree on where a room's walls are.
/// </summary>
public static class RoomOutline
{
    /// <summary>The outer loop (largest area; the others are holes such as columns); arcs tessellated.</summary>
    public static List<Pt> OuterLoopMm(Room room) => LoopsMm(room).Outer;

    /// <summary>
    /// The outer loop and the inner ones — columns, shafts — that are not floor: the free-area
    /// checks must treat them as obstacles, not as room.
    /// </summary>
    public static (List<Pt> Outer, List<List<Pt>> Holes) LoopsMm(Room room)
    {
        var all = ReadLoops(room);
        if (all.Count == 0) return (new List<Pt>(), new List<List<Pt>>());
        var outer = all.OrderByDescending(loop => PlanGeometry.Area(loop)).First();
        return (outer, all.Where(loop => !ReferenceEquals(loop, outer)).ToList());
    }

    private static List<List<Pt>> ReadLoops(Room room)
    {
        IList<IList<BoundarySegment>>? loops;
        try
        {
            loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            });
        }
        catch
        {
            return new List<List<Pt>>();
        }
        var result = new List<List<Pt>>();
        if (loops == null || loops.Count == 0) return result;

        foreach (var loop in loops)
        {
            var candidate = new List<Pt>();
            foreach (var segment in loop)
            {
                var curve = segment.GetCurve();
                var points = curve is Line
                    ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) }
                    : curve.Tessellate().ToList();
                foreach (var point in points.Take(points.Count - 1))
                    candidate.Add(new Pt(point.X * MmPerFoot, point.Y * MmPerFoot));
            }
            if (candidate.Count >= 3 && PlanGeometry.Area(candidate) > 1) result.Add(candidate);
        }
        return result;
    }
}
