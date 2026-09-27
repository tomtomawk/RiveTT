using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Measures the geometry of a family instance or type in a chosen frame — family coordinates
/// for describe_family, model coordinates for the footprints validate_dwelling deducts from the
/// free circles.
///
/// Never the bounding box: get_BoundingBox includes the invisible clearance geometry families
/// carry (a 1.40 x 1.90 m bed measured 2.85 x 3.90 m, field report 2026-09-24). The visible
/// footprint is the extent of the solids of volume > 0 that are visible in 3D.
/// </summary>
public static class FamilyExtents
{
    /// <summary>Walks the geometry of <paramref name="element"/> into <paramref name="extent"/>.</summary>
    /// <param name="toFrame">Transform from the element's geometry space (model) to the frame wanted.</param>
    /// <param name="solidsOnly">Solids of volume &gt; 0 only (the visible footprint), or every curve and mesh too.</param>
    public static Extent Measure(Element element, Transform toFrame, Options options, bool solidsOnly)
    {
        var extent = new Extent();
        GeometryElement? geometry;
        try { geometry = element.get_Geometry(options); } catch { geometry = null; }
        if (geometry != null) Walk(geometry, toFrame, solidsOnly, extent, depth: 0);
        return extent;
    }

    /// <summary>
    /// The visible footprint of an instance in plan, in MODEL millimetres: its local visible
    /// extent turned into a (possibly rotated) rectangle by the instance transform. Null when the
    /// instance has no visible solid.
    /// </summary>
    public static List<(double X, double Y)>? PlanFootprintMm(FamilyInstance instance, ViewDetailLevel detailLevel = ViewDetailLevel.Fine)
    {
        Transform transform;
        try { transform = instance.GetTransform(); } catch { return null; }
        var local = Measure(instance, transform.Inverse, new Options { DetailLevel = detailLevel }, solidsOnly: true);
        if (local.IsEmpty) return null;
        var corners = new[]
        {
            new XYZ(local.MinX, local.MinY, 0), new XYZ(local.MaxX, local.MinY, 0),
            new XYZ(local.MaxX, local.MaxY, 0), new XYZ(local.MinX, local.MaxY, 0)
        };
        return corners.Select(c => transform.OfPoint(c)).Select(p => (p.X * MmPerFoot, p.Y * MmPerFoot)).ToList();
    }

    private static void Walk(GeometryElement geometry, Transform toFrame, bool solidsOnly, Extent extent, int depth)
    {
        if (depth > 8) return;
        foreach (var item in geometry)
        {
            switch (item)
            {
                case Solid solid when solid.Volume > 1e-9:
                    foreach (Edge edge in solid.Edges)
                        foreach (var point in SafeTessellate(edge))
                            extent.Add(toFrame.OfPoint(point));
                    break;
                case Curve curve when !solidsOnly:
                    foreach (var point in SafeTessellate(curve))
                        extent.Add(toFrame.OfPoint(point));
                    break;
                case Mesh mesh when !solidsOnly:
                    foreach (var point in mesh.Vertices)
                        extent.Add(toFrame.OfPoint(point));
                    break;
                case GeometryInstance nested:
                    // Symbol geometry is in the symbol's space; its Transform takes it to ours.
                    GeometryElement? symbolGeometry;
                    try { symbolGeometry = nested.GetSymbolGeometry(); } catch { symbolGeometry = null; }
                    if (symbolGeometry != null)
                        Walk(symbolGeometry, toFrame.Multiply(nested.Transform), solidsOnly, extent, depth + 1);
                    break;
            }
        }
    }

    private static IEnumerable<XYZ> SafeTessellate(Edge edge)
    {
        try { return edge.Tessellate(); } catch { return Array.Empty<XYZ>(); }
    }

    private static IEnumerable<XYZ> SafeTessellate(Curve curve)
    {
        try { return curve.Tessellate(); } catch { return Array.Empty<XYZ>(); }
    }

    /// <summary>Min/max accumulator in feet, reported in millimetres.</summary>
    public sealed class Extent
    {
        public double MinX { get; private set; } = double.MaxValue;
        public double MinY { get; private set; } = double.MaxValue;
        public double MinZ { get; private set; } = double.MaxValue;
        public double MaxX { get; private set; } = double.MinValue;
        public double MaxY { get; private set; } = double.MinValue;
        public double MaxZ { get; private set; } = double.MinValue;

        public bool IsEmpty => MinX > MaxX;

        public void Add(XYZ point)
        {
            MinX = Math.Min(MinX, point.X); MaxX = Math.Max(MaxX, point.X);
            MinY = Math.Min(MinY, point.Y); MaxY = Math.Max(MaxY, point.Y);
            MinZ = Math.Min(MinZ, point.Z); MaxZ = Math.Max(MaxZ, point.Z);
        }

        public double Width => (MaxX - MinX) * MmPerFoot;
        public double Depth => (MaxY - MinY) * MmPerFoot;
        public double CenterX => (MinX + MaxX) / 2 * MmPerFoot;
        public double CenterY => (MinY + MaxY) / 2 * MmPerFoot;

        public object ToReport() => new
        {
            x = new[] { R(MinX), R(MaxX) },
            y = new[] { R(MinY), R(MaxY) },
            z = new[] { R(MinZ), R(MaxZ) },
            widthX = Math.Round(Width, 1),
            depthY = Math.Round(Depth, 1),
            heightZ = Math.Round((MaxZ - MinZ) * MmPerFoot, 1),
            centerFromOrigin = new[] { Math.Round(CenterX, 1), Math.Round(CenterY, 1) }
        };

        private static double R(double feet) => Math.Round(feet * MmPerFoot, 1);
    }
}
