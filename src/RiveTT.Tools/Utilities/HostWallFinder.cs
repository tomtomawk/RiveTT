using System.Linq;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Finds the wall an insertion point sits in: the one whose location line passes within half
/// its width (plus a tolerance) of the point in plan, and whose height spans the point's Z.
/// Shared by create_point_based_element (findHost) and the send_code_to_revit helper
/// FindHostWall, so a door placed by either path picks the same wall.
/// </summary>
public static class HostWallFinder
{
    /// <summary>
    /// The nearest qualifying wall, or null. With <paramref name="level"/>, only walls whose
    /// base level is that level are considered.
    /// </summary>
    public static Wall? Find(Document document, XYZ point, Level? level, double toleranceMm, out double distanceMm)
    {
        distanceMm = double.NaN;
        Wall? best = null;
        var bestDistance = double.MaxValue;
        var flat = new XYZ(point.X, point.Y, 0);
        var tolerance = toleranceMm / LengthUnits.MmPerFoot;
        // FindHostWall(Pt(x, y), level) passes z = 0: on an upper floor that is below every wall
        // of the level, the very Z = 0 trap the helper exists to avoid. With a level, a point
        // under it is tested one foot above the level instead.
        var z = level != null && point.Z < level.Elevation ? level.Elevation + 1.0 : point.Z;

        foreach (var wall in new FilteredElementCollector(document).OfClass(typeof(Wall)).Cast<Wall>())
        {
            if (level != null && wall.LevelId != level.Id) continue;
            if (wall.Location is not LocationCurve location) continue;

            var box = wall.get_BoundingBox(null);
            if (box != null && (z < box.Min.Z - 1e-6 || z > box.Max.Z + 1e-6)) continue;

            var flatCurve = Flatten(location.Curve);
            var projection = flatCurve?.Project(flat);
            if (projection == null) continue;

            // Beyond the ends of the wall the projection clamps to an end point: the distance
            // then grows past half the width, so an opening is never hosted off the wall's end.
            var distance = projection.Distance;
            if (distance > wall.Width / 2 + tolerance) continue;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = wall;
            }
        }

        if (best != null) distanceMm = bestDistance * LengthUnits.MmPerFoot;
        return best;
    }

    private static Curve? Flatten(Curve curve)
    {
        try
        {
            if (curve is Line)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                return Line.CreateBound(new XYZ(p0.X, p0.Y, 0), new XYZ(p1.X, p1.Y, 0));
            }
            return curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -curve.GetEndPoint(0).Z)));
        }
        catch
        {
            return null;
        }
    }
}
