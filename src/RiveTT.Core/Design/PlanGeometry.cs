using System;
using System.Collections.Generic;
using System.Linq;

namespace RiveTT.Core.Design;

/// <summary>A plan point in millimetres.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// Plan geometry for the dwelling rules, in millimetres, with no Revit dependency: the same
/// code checks a design sent as JSON (validate_spec, on the server) and a dwelling read back
/// from the model (validate_dwelling, in Revit).
/// </summary>
public static class PlanGeometry
{
    public static double Area(IReadOnlyList<Pt> polygon)
    {
        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }

    public static bool Contains(IReadOnlyList<Pt> polygon, Pt p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared < 1e-12) return (p - a).Length;
        var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared));
        return (p - new Pt(a.X + t * ab.X, a.Y + t * ab.Y)).Length;
    }

    public static double DistanceToBoundary(IReadOnlyList<Pt> polygon, Pt p)
    {
        var best = double.MaxValue;
        for (var i = 0; i < polygon.Count; i++)
            best = Math.Min(best, DistanceToSegment(p, polygon[i], polygon[(i + 1) % polygon.Count]));
        return best;
    }

    /// <summary>Distance from a point to a polygon: 0 inside, else to its boundary.</summary>
    public static double DistanceTo(IReadOnlyList<Pt> polygon, Pt p) =>
        Contains(polygon, p) ? 0 : DistanceToBoundary(polygon, p);

    public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<Pt> polygon) =>
        (polygon.Min(p => p.X), polygon.Min(p => p.Y), polygon.Max(p => p.X), polygon.Max(p => p.Y));

    /// <summary>
    /// Length along which the boundaries of two polygons coincide — edges parallel, within
    /// <paramref name="toleranceMm"/> of each other, overlapping in projection.
    /// </summary>
    public static double SharedBoundaryLength(IReadOnlyList<Pt> a, IReadOnlyList<Pt> b, double toleranceMm)
        => SharedSegments(a, b, toleranceMm).Sum(s => (s.B - s.A).Length);

    /// <summary>The coinciding stretches themselves, as segments lying on polygon <paramref name="a"/>.</summary>
    public static List<(Pt A, Pt B)> SharedSegments(IReadOnlyList<Pt> a, IReadOnlyList<Pt> b, double toleranceMm)
    {
        var result = new List<(Pt, Pt)>();
        for (var i = 0; i < a.Count; i++)
        {
            var a0 = a[i];
            var a1 = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
            {
                var overlap = CollinearOverlap(a0, a1, b[j], b[(j + 1) % b.Count], toleranceMm);
                if (overlap != null) result.Add(overlap.Value);
            }
        }
        return result;
    }

    /// <summary>
    /// The part of segment a0-a1 that segment b0-b1 runs along (parallel, within the
    /// tolerance), as a segment on a0-a1; null when they do not run together.
    /// </summary>
    public static (Pt A, Pt B)? CollinearOverlap(Pt a0, Pt a1, Pt b0, Pt b1, double toleranceMm)
    {
        var direction = a1 - a0;
        var length = direction.Length;
        if (length < 1e-6) return null;
        var ux = direction.X / length;
        var uy = direction.Y / length;

        // Both ends of b within tolerance of the LINE through a.
        double Offset(Pt p) => Math.Abs((p.X - a0.X) * uy - (p.Y - a0.Y) * ux);
        if (Offset(b0) > toleranceMm || Offset(b1) > toleranceMm) return null;

        double Along(Pt p) => (p.X - a0.X) * ux + (p.Y - a0.Y) * uy;
        var s0 = Math.Max(0, Math.Min(Along(b0), Along(b1)));
        var s1 = Math.Min(length, Math.Max(Along(b0), Along(b1)));
        if (s1 - s0 < 1) return null;
        return (new Pt(a0.X + ux * s0, a0.Y + uy * s0), new Pt(a0.X + ux * s1, a0.Y + uy * s1));
    }

    /// <summary>Length of segment s covered by segment c lying along it within the tolerance.</summary>
    public static double CoveredLength((Pt A, Pt B) s, (Pt A, Pt B) c, double toleranceMm)
    {
        var overlap = CollinearOverlap(s.A, s.B, c.A, c.B, toleranceMm);
        return overlap == null ? 0 : (overlap.Value.B - overlap.Value.A).Length;
    }
}

/// <summary>
/// A room rasterised on a regular grid, with each free cell's clearance: its distance to the
/// room boundary or to the nearest obstacle (equipment footprint). The accessibility checks
/// are questions about this field — does a Ø 1.50 m circle fit, does a 1.20 x 2.20 m
/// rectangle fit, can a 0.90 m wide disc travel through the corridor.
/// </summary>
public sealed class ClearanceField
{
    private readonly bool[,] _free;
    private readonly double[,] _clearance;
    private readonly IReadOnlyList<Pt> _room;
    private readonly List<IReadOnlyList<Pt>> _obstacles;
    private readonly List<(Pt A, Pt B)> _edges;
    private readonly int[,] _blockedPrefix; // (nx+1) x (ny+1) summed count of NOT free cells

    public double Step { get; }
    public double OriginX { get; }
    public double OriginY { get; }
    public int Nx { get; }
    public int Ny { get; }

    public ClearanceField(IReadOnlyList<Pt> room, IEnumerable<IReadOnlyList<Pt>> obstacles, double stepMm = 25)
    {
        Step = stepMm;
        var (minX, minY, maxX, maxY) = PlanGeometry.Bounds(room);
        OriginX = minX;
        OriginY = minY;
        Nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / stepMm));
        Ny = Math.Max(1, (int)Math.Ceiling((maxY - minY) / stepMm));

        var obstacleList = obstacles.Where(o => o.Count >= 3).ToList();
        var edges = new List<(Pt A, Pt B)>();
        _room = room;
        _obstacles = obstacleList;
        _edges = edges;
        for (var i = 0; i < room.Count; i++) edges.Add((room[i], room[(i + 1) % room.Count]));
        foreach (var obstacle in obstacleList)
            for (var i = 0; i < obstacle.Count; i++) edges.Add((obstacle[i], obstacle[(i + 1) % obstacle.Count]));

        _free = new bool[Nx, Ny];
        _clearance = new double[Nx, Ny];
        _blockedPrefix = new int[Nx + 1, Ny + 1];
        for (var ix = 0; ix < Nx; ix++)
        {
            for (var iy = 0; iy < Ny; iy++)
            {
                var p = CellCenter(ix, iy);
                var free = PlanGeometry.Contains(room, p) && !obstacleList.Any(o => PlanGeometry.Contains(o, p));
                _free[ix, iy] = free;
                if (!free) continue;
                var best = double.MaxValue;
                foreach (var (a, b) in edges) best = Math.Min(best, PlanGeometry.DistanceToSegment(p, a, b));
                _clearance[ix, iy] = best;
            }
        }
        for (var ix = 0; ix < Nx; ix++)
            for (var iy = 0; iy < Ny; iy++)
                _blockedPrefix[ix + 1, iy + 1] = (_free[ix, iy] ? 0 : 1)
                    + _blockedPrefix[ix, iy + 1] + _blockedPrefix[ix + 1, iy] - _blockedPrefix[ix, iy];
    }

    public Pt CellCenter(int ix, int iy) => new(OriginX + (ix + 0.5) * Step, OriginY + (iy + 0.5) * Step);

    /// <summary>Diameter of the largest free circle, and where its centre is.</summary>
    public (double DiameterMm, Pt Center) LargestCircle()
    {
        var best = 0.0;
        var center = new Pt(OriginX, OriginY);
        for (var ix = 0; ix < Nx; ix++)
            for (var iy = 0; iy < Ny; iy++)
                if (_free[ix, iy] && _clearance[ix, iy] > best)
                {
                    best = _clearance[ix, iy];
                    center = CellCenter(ix, iy);
                }
        return (2 * best, center);
    }

    /// <summary>
    /// The largest free circle, refined below the grid step: the best few cells are searched
    /// again at a fifth of the step with exact distances. On the grid alone the measure is up
    /// to one cell diagonal short, which is enough to call a 1.47 m circle 1.50 m or the
    /// reverse (review of 0.6.0).
    /// </summary>
    public (double DiameterMm, Pt Center) LargestCircleRefined(int candidates = 6)
    {
        var cells = new List<(double Clearance, int X, int Y)>();
        for (var ix = 0; ix < Nx; ix++)
            for (var iy = 0; iy < Ny; iy++)
                if (_free[ix, iy]) cells.Add((_clearance[ix, iy], ix, iy));
        if (cells.Count == 0) return (0, new Pt(OriginX, OriginY));

        var best = 0.0;
        var center = new Pt(OriginX, OriginY);
        var fine = Step / 5;
        foreach (var (_, cx, cy) in cells.OrderByDescending(c => c.Clearance).Take(candidates))
        {
            var seed = CellCenter(cx, cy);
            for (var dx = -Step; dx <= Step + 1e-9; dx += fine)
                for (var dy = -Step; dy <= Step + 1e-9; dy += fine)
                {
                    var p = new Pt(seed.X + dx, seed.Y + dy);
                    var clearance = ExactClearance(p);
                    if (clearance > best)
                    {
                        best = clearance;
                        center = p;
                    }
                }
        }
        return (2 * best, center);
    }

    private double ExactClearance(Pt p)
    {
        if (!PlanGeometry.Contains(_room, p) || _obstacles.Any(o => PlanGeometry.Contains(o, p))) return 0;
        var best = double.MaxValue;
        foreach (var (a, b) in _edges) best = Math.Min(best, PlanGeometry.DistanceToSegment(p, a, b));
        return best;
    }

    /// <summary>
    /// Where an axis-aligned free rectangle of this size fits, trying both orientations;
    /// null when it fits nowhere. Resolution: one grid step.
    /// </summary>
    public (Pt Min, Pt Max, bool Rotated)? FitRectangle(double widthMm, double depthMm)
    {
        foreach (var (w, d, rotated) in new[] { (widthMm, depthMm, false), (depthMm, widthMm, true) })
        {
            var cw = (int)Math.Ceiling(w / Step - 1e-9);
            var cd = (int)Math.Ceiling(d / Step - 1e-9);
            if (cw > Nx || cd > Ny) continue;
            for (var ix = 0; ix + cw <= Nx; ix++)
                for (var iy = 0; iy + cd <= Ny; iy++)
                    if (BlockedIn(ix, iy, cw, cd) == 0)
                        return (new Pt(OriginX + ix * Step, OriginY + iy * Step),
                                new Pt(OriginX + (ix + cw) * Step, OriginY + (iy + cd) * Step), rotated);
        }
        return null;
    }

    private int BlockedIn(int ix, int iy, int cw, int cd) =>
        _blockedPrefix[ix + cw, iy + cd] - _blockedPrefix[ix, iy + cd] - _blockedPrefix[ix + cw, iy] + _blockedPrefix[ix, iy];

    /// <summary>
    /// Can a disc of this diameter move through the whole free area? Answers with the number
    /// of separate regions its centre can reach: 0 = it fits nowhere, 1 = one continuous
    /// passage, more = a narrowing cuts the room in parts. Also returns the area of those
    /// regions, used to tell a real passage from the widening at a corner.
    /// </summary>
    public (int Regions, double AreaM2) PassageRegions(double discDiameterMm)
    {
        var radius = discDiameterMm / 2;
        var seen = new bool[Nx, Ny];
        var regions = 0;
        var cells = 0;
        for (var ix = 0; ix < Nx; ix++)
        {
            for (var iy = 0; iy < Ny; iy++)
            {
                if (seen[ix, iy] || !_free[ix, iy] || _clearance[ix, iy] < radius) continue;
                regions++;
                var queue = new Queue<(int, int)>();
                queue.Enqueue((ix, iy));
                seen[ix, iy] = true;
                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    cells++;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Nx || ny >= Ny || seen[nx, ny]) continue;
                        if (!_free[nx, ny] || _clearance[nx, ny] < radius) continue;
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }
        return (regions, cells * Step * Step / 1_000_000);
    }
}
