using System;
using System.Collections.Generic;
using System.Linq;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Groups selected curves into connected chains and says which ones close, in plain
/// millimetres so it can be tested without Revit.
///
/// Field report of 2026-09-24: four detail lines drawn as a closed outline were read as an
/// axis, and the swale they were meant to bound was built along them. A selection read as
/// ids and categories cannot tell a contour from a polyline; the chain analysis can, and it
/// comes back with the selection so the reading is not left to a guess.
/// </summary>
public static class CurveChains
{
    public sealed record Segment(long ElementId, double X0, double Y0, double X1, double Y1, double LengthMm);

    public sealed record Chain(IReadOnlyList<long> ElementIds, bool Closed, double LengthMm, double? AreaM2,
        double? WidthMm, double? HeightMm);

    /// <summary>
    /// Chains segments whose end points meet within <paramref name="toleranceMm"/> in plan.
    /// A chain is closed when every node has exactly two segment ends and it forms a single
    /// cycle. The area of a closed chain is the polygon of its vertices (arcs count as their
    /// chord), and its width/height are those of its bounding rectangle.
    /// </summary>
    public static IReadOnlyList<Chain> Analyze(IReadOnlyList<Segment> segments, double toleranceMm = 1.0)
    {
        // Node ids: end points merged within tolerance.
        var nodes = new List<(double X, double Y)>();
        int NodeOf(double x, double y)
        {
            for (var i = 0; i < nodes.Count; i++)
                if (Math.Abs(nodes[i].X - x) <= toleranceMm && Math.Abs(nodes[i].Y - y) <= toleranceMm)
                    return i;
            nodes.Add((x, y));
            return nodes.Count - 1;
        }

        var ends = segments.Select(s => (A: NodeOf(s.X0, s.Y0), B: NodeOf(s.X1, s.Y1))).ToList();

        // Connected components over segments sharing a node.
        var component = Enumerable.Repeat(-1, segments.Count).ToArray();
        var chains = new List<Chain>();
        for (var start = 0; start < segments.Count; start++)
        {
            if (component[start] >= 0) continue;
            var members = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            component[start] = chains.Count;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                members.Add(current);
                for (var other = 0; other < segments.Count; other++)
                {
                    if (component[other] >= 0) continue;
                    if (ends[other].A == ends[current].A || ends[other].A == ends[current].B ||
                        ends[other].B == ends[current].A || ends[other].B == ends[current].B)
                    {
                        component[other] = chains.Count;
                        queue.Enqueue(other);
                    }
                }
            }

            chains.Add(Describe(members, segments, ends, nodes));
        }

        return chains;
    }

    private static Chain Describe(List<int> members, IReadOnlyList<Segment> segments,
        List<(int A, int B)> ends, List<(double X, double Y)> nodes)
    {
        var degree = new Dictionary<int, int>();
        foreach (var index in members)
        {
            if (ends[index].A == ends[index].B) continue; // zero-length after merge: ignore
            degree[ends[index].A] = degree.GetValueOrDefault(ends[index].A) + 1;
            degree[ends[index].B] = degree.GetValueOrDefault(ends[index].B) + 1;
        }

        var closed = members.Count >= 2 && degree.Count > 0 && degree.Values.All(d => d == 2)
                     && degree.Count == members.Count(i => ends[i].A != ends[i].B);
        var length = members.Sum(i => segments[i].LengthMm);

        double? area = null, width = null, height = null;
        var usedNodes = degree.Keys.Select(n => nodes[n]).ToList();
        if (usedNodes.Count > 0)
        {
            width = Math.Round(usedNodes.Max(n => n.X) - usedNodes.Min(n => n.X), 1);
            height = Math.Round(usedNodes.Max(n => n.Y) - usedNodes.Min(n => n.Y), 1);
        }

        if (closed)
        {
            // Walk the cycle to order its vertices, then shoelace.
            var polygon = new List<(double X, double Y)>();
            var remaining = new HashSet<int>(members);
            var first = members[0];
            var node = ends[first].A;
            var segment = first;
            while (remaining.Count > 0)
            {
                remaining.Remove(segment);
                polygon.Add(nodes[node]);
                node = ends[segment].A == node ? ends[segment].B : ends[segment].A;
                var next = remaining.FirstOrDefault(i => ends[i].A == node || ends[i].B == node, -1);
                if (next < 0) break;
                segment = next;
            }
            var sum = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var (x0, y0) = polygon[i];
                var (x1, y1) = polygon[(i + 1) % polygon.Count];
                sum += x0 * y1 - x1 * y0;
            }
            area = Math.Round(Math.Abs(sum) / 2 / 1_000_000, 3);
        }

        return new Chain(members.Select(i => segments[i].ElementId).ToList(), closed,
            Math.Round(length, 1), area, width, height);
    }

    /// <summary>One sentence an agent can act on: contour or open line, and how many.</summary>
    public static string Reading(IReadOnlyList<Chain> chains)
    {
        if (chains.Count == 0) return "No curves in the selection.";
        var closed = chains.Count(c => c.Closed);
        var open = chains.Count - closed;
        var parts = new List<string>();
        if (closed > 0)
            parts.Add($"{closed} closed loop(s) — a CONTOUR (outline of an area), not an axis");
        if (open > 0)
            parts.Add($"{open} open chain(s) — a line or axis");
        return string.Join("; ", parts) + ".";
    }
}
