using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates an extrusion with a free profile in the ACTIVE FAMILY document (a .rfa opened with
/// open_family): the geometry of a bar, a grille, a decorative motif. No dedicated tool did it, so every
/// such family went through send_code_to_revit (field report, ferronnerie session).
///
/// The profile is given as loops of segments in millimetres, in the coordinates of the sketch plane:
///   line   {type:"line",  start:{x,y}, end:{x,y}}
///   arc    {type:"arc",   start:{x,y}, mid:{x,y}, end:{x,y}}        three points, the middle one ON the arc
///   nurbs  {type:"nurbs", degree, knots:[...], controlPoints:[{x,y}...], weights?:[...]}
/// NURBS segments are created with NurbSpline.CreateCurve, never from a HermiteSpline: a Hermite spline in a
/// sketch is not an editable spline. Before anything is created the loops are validated (closed, no
/// self-intersection, no segment under 1.2 mm), and the response says which curve types Revit really made —
/// NurbSpline.CreateCurve may return a simpler Line or Arc, and that is reported, not hidden.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class CreateFamilyExtrusionTool : IRiveTTTool
{
    private const double MinSegmentMm = 1.2;
    private const double ClosureToleranceMm = 0.1;

    public string Name => "create_family_extrusion";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Creates an extrusion with a free profile in the ACTIVE FAMILY document (open one with open_family first). " +
        "loops: closed loops of segments in mm, in sketch-plane coordinates {x,y}: line {start,end}, arc {start,mid,end} " +
        "(mid ON the arc), nurbs {degree,knots,controlPoints,weights?}. Sketch plane: plane = plan | front | side, or " +
        "origin {x,y,z} mm + normal {x,y,z}; the response gives the plane axes. start/end: extrusion offsets from the plane " +
        "in mm; isSolid (default true, false = void); materialName. dryRun (default) validates the loops (closed, no " +
        "self-intersection, no segment under 1.2 mm) and reports the curve types Revit would really create. NURBS are " +
        "created with NurbSpline.CreateCurve, never HermiteSpline (not editable in a sketch).";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");
        if (!doc.IsFamilyDocument)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{doc.Title}' is a project, not a family: an extrusion with a free profile is created in a family document.",
                suggestion: "Open the family with open_family (activate it), then call create_family_extrusion.");

        var dryRun = ToolHelpers.GetDryRun(input);
        var startMm = input["start"]?.Value<double>() ?? 0;
        var endMm = input["end"]?.Value<double?>();
        var isSolid = input["isSolid"]?.Value<bool>() ?? true;
        var materialName = input["materialName"]?.Value<string>();

        if (endMm == null || Math.Abs(endMm.Value - startMm) < MinSegmentMm)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "end is required and must differ from start by at least 1.2 mm",
                suggestion: "start and end are the extrusion offsets from the sketch plane, in mm.");
        if (input["loops"] is not JArray loopTokens || loopTokens.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "loops is required: an array of closed loops of segments",
                suggestion: "[[{\"type\":\"line\",\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10,\"y\":0}}, ...]]");

        try
        {
            if (!TryBuildPlane(input, out var plane, out var planeError)) return planeError!;

            var issues = new List<string>();
            var loops = new List<List<Curve>>();
            var requestedTypes = new List<string>();
            for (var l = 0; l < loopTokens.Count; l++)
            {
                var curves = new List<Curve>();
                if (loopTokens[l] is not JArray segmentTokens || segmentTokens.Count == 0)
                {
                    issues.Add($"loop {l}: must be a non-empty array of segments.");
                    continue;
                }
                for (var s = 0; s < segmentTokens.Count; s++)
                {
                    var curve = BuildCurve(segmentTokens[s], plane!, l, s, issues, requestedTypes);
                    if (curve != null) curves.Add(curve);
                }
                loops.Add(curves);
            }

            if (issues.Count == 0) ValidateLoops(loops, issues);
            if (issues.Count > 0)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"The profile is not valid: {issues.Count} problem(s). Nothing was created.",
                    suggestion: "Fix the listed problems: loops must be closed (each segment ends where the next starts), "
                              + "must not cross themselves or each other, and no segment may be shorter than 1.2 mm.",
                    context: new Dictionary<string, object> { ["issues"] = issues.Take(40).ToArray(), ["modelChanged"] = false });

            var createdTypes = loops.SelectMany(loop => loop).GroupBy(c => c.GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count());
            var simplified = DescribeSimplified(loops, requestedTypes);

            Material? material = null;
            if (!string.IsNullOrWhiteSpace(materialName))
            {
                material = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                    .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));
                if (material == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                        $"Material '{materialName}' not found in the family.",
                        suggestion: "Names in the family: " + string.Join(", ",
                            new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Select(m => m.Name).Take(20)));
            }

            var planeInfo = new
            {
                originMm = new { x = Math.Round(plane!.Origin.X * MmPerFoot, 3), y = Math.Round(plane.Origin.Y * MmPerFoot, 3), z = Math.Round(plane.Origin.Z * MmPerFoot, 3) },
                xAxis = Vec(plane.XVec), yAxis = Vec(plane.YVec), normal = Vec(plane.Normal)
            };

            if (dryRun)
                return RiveTTResult<object>.Ok(new
                {
                    message = $"DryRun: {loops.Count} loop(s), {loops.Sum(l => l.Count)} segment(s) are valid and would form a "
                              + $"{(isSolid ? "solid" : "void")} extrusion from {startMm} to {endMm} mm. Nothing was created.",
                    dryRun = true, mutated = false, previewMethod = "declared", valid = true,
                    plane = planeInfo, loopCount = loops.Count, segmentCount = loops.Sum(l => l.Count),
                    curveTypes = createdTypes, simplifiedCurves = simplified,
                    material = material?.Name
                });

            using var tx = new Transaction(doc, "RiveTT: Family Extrusion");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

            var sketchPlane = SketchPlane.Create(doc, plane);
            var profile = new CurveArrArray();
            foreach (var loop in loops)
            {
                var array = new CurveArray();
                foreach (var curve in loop) array.Append(curve);
                profile.Append(array);
            }

            var extrusion = doc.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, endMm.Value / MmPerFoot);
            extrusion.StartOffset = startMm / MmPerFoot;
            extrusion.EndOffset = endMm.Value / MmPerFoot;
            if (material != null)
                extrusion.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(material.Id);

            var actualTypes = new Dictionary<string, int>();
            foreach (CurveArray array in extrusion.Sketch.Profile)
                foreach (Curve curve in array)
                    actualTypes[curve.GetType().Name] = actualTypes.GetValueOrDefault(curve.GetType().Name) + 1;
            var extrusionId = ToolHelpers.GetElementIdValue(extrusion.Id);

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported errors (a profile Revit refuses is usually self-touching or too small) and retry.");

            return RiveTTResult<object>.Ok(new
            {
                extrusionId,
                isSolid,
                plane = planeInfo,
                startMm,
                endMm,
                loopCount = loops.Count,
                segmentCount = loops.Sum(l => l.Count),
                requestedCurveTypes = createdTypes,
                createdCurveTypes = actualTypes,
                simplifiedCurves = simplified,
                material = material?.Name
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"create_family_extrusion could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Validate the profile with dryRun first. The full call, its duration and this error are in "
                    + "%LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static object Vec(XYZ v) => new { x = Math.Round(v.X, 4), y = Math.Round(v.Y, 4), z = Math.Round(v.Z, 4) };

    // ── Sketch plane ───────────────────────────────────────────────────────

    private static bool TryBuildPlane(JObject input, out Plane? plane, out RiveTTResult<object>? error)
    {
        plane = null;
        error = null;
        XYZ origin = XYZ.Zero;
        if (input["origin"] is JObject originToken)
            origin = new XYZ(Num(originToken["x"]) / MmPerFoot, Num(originToken["y"]) / MmPerFoot, Num(originToken["z"]) / MmPerFoot);

        XYZ xAxis, yAxis;
        if (input["normal"] is JObject normalToken)
        {
            var normal = new XYZ(Num(normalToken["x"]), Num(normalToken["y"]), Num(normalToken["z"]));
            if (normal.GetLength() < 1e-9)
            {
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "normal must be a non-zero vector {x,y,z}");
                return false;
            }
            normal = normal.Normalize();
            // The plane's X axis: horizontal when the plane is not horizontal (up × normal), else global X.
            var up = XYZ.BasisZ;
            xAxis = Math.Abs(normal.DotProduct(up)) > 0.999 ? XYZ.BasisX : up.CrossProduct(normal).Normalize();
            yAxis = normal.CrossProduct(xAxis).Normalize();
        }
        else
        {
            var preset = (input["plane"]?.Value<string>() ?? "front").ToLowerInvariant();
            switch (preset)
            {
                case "plan": xAxis = XYZ.BasisX; yAxis = XYZ.BasisY; break;            // normal +Z
                case "front": xAxis = XYZ.BasisX; yAxis = XYZ.BasisZ; break;           // normal -Y
                case "side": xAxis = XYZ.BasisY; yAxis = XYZ.BasisZ; break;            // normal +X
                default:
                    error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"plane '{preset}' is not recognized",
                        suggestion: "Use plan, front, side, or origin + normal.");
                    return false;
            }
        }
        plane = Plane.CreateByOriginAndBasis(origin, xAxis, yAxis);
        return true;
    }

    private static double Num(JToken? token) => token?.Value<double?>() ?? 0;

    // ── Segments ───────────────────────────────────────────────────────────

    private static XYZ? Point(JToken? token, Plane plane, string where, List<string> issues)
    {
        if (token?["x"] == null || token["y"] == null)
        {
            issues.Add($"{where}: a point must be {{x, y}} in mm, in the sketch plane.");
            return null;
        }
        var u = token["x"]!.Value<double>() / MmPerFoot;
        var v = token["y"]!.Value<double>() / MmPerFoot;
        return plane.Origin + plane.XVec * u + plane.YVec * v;
    }

    private static Curve? BuildCurve(JToken segment, Plane plane, int loop, int index, List<string> issues, List<string> requested)
    {
        var where = $"loop {loop} segment {index}";
        var type = segment["type"]?.Value<string>()?.ToLowerInvariant();
        try
        {
            switch (type)
            {
                case "line":
                {
                    var a = Point(segment["start"], plane, where + " start", issues);
                    var b = Point(segment["end"], plane, where + " end", issues);
                    if (a == null || b == null) return null;
                    if (a.DistanceTo(b) * MmPerFoot < MinSegmentMm)
                    {
                        issues.Add($"{where}: line shorter than {MinSegmentMm} mm.");
                        return null;
                    }
                    requested.Add("Line");
                    return Line.CreateBound(a, b);
                }
                case "arc":
                {
                    var a = Point(segment["start"], plane, where + " start", issues);
                    var m = Point(segment["mid"], plane, where + " mid", issues);
                    var b = Point(segment["end"], plane, where + " end", issues);
                    if (a == null || m == null || b == null) return null;
                    requested.Add("Arc");
                    return Arc.Create(a, b, m);
                }
                case "nurbs":
                {
                    var degree = segment["degree"]?.Value<int>() ?? 0;
                    var knots = segment["knots"]?.ToObject<List<double>>();
                    var pointTokens = segment["controlPoints"] as JArray;
                    var weights = segment["weights"]?.ToObject<List<double>>();
                    if (degree < 1 || knots == null || pointTokens == null)
                    {
                        issues.Add($"{where}: nurbs needs degree (>= 1), knots and controlPoints.");
                        return null;
                    }
                    var points = pointTokens.Select((t, i) => Point(t, plane, $"{where} controlPoint {i}", issues)).ToList();
                    if (points.Any(p => p == null)) return null;
                    if (points.Count <= degree)
                    {
                        issues.Add($"{where}: nurbs needs more control points ({points.Count}) than its degree ({degree}).");
                        return null;
                    }
                    if (knots.Count != degree + points.Count + 1)
                    {
                        issues.Add($"{where}: nurbs needs degree + controlPoints + 1 = {degree + points.Count + 1} knots, got {knots.Count}.");
                        return null;
                    }
                    if (knots.Zip(knots.Skip(1), (a, b) => b < a).Any(decreasing => decreasing))
                    {
                        issues.Add($"{where}: nurbs knots must be non-decreasing.");
                        return null;
                    }
                    if (weights != null && (weights.Count != points.Count || weights.Any(w => w <= 0)))
                    {
                        issues.Add($"{where}: weights must match the control points in number and be positive.");
                        return null;
                    }
                    requested.Add("NurbSpline");
                    var list = points.Select(p => p!).ToList();
                    return weights == null
                        ? NurbSpline.CreateCurve(degree, knots, list)
                        : NurbSpline.CreateCurve(degree, knots, list, weights);
                }
                default:
                    issues.Add($"{where}: type '{type}' is not line, arc or nurbs.");
                    return null;
            }
        }
        catch (Exception ex)
        {
            issues.Add($"{where}: Revit refused the curve: {ex.Message}");
            return null;
        }
    }

    /// <summary>Segments whose requested type is not the type Revit produced (a NURBS made a Line or an Arc).</summary>
    private static List<object> DescribeSimplified(List<List<Curve>> loops, List<string> requested)
    {
        var result = new List<object>();
        var flat = loops.SelectMany(l => l).ToList();
        for (var i = 0; i < flat.Count && i < requested.Count; i++)
            if (flat[i].GetType().Name != requested[i])
                result.Add(new { segment = i, requested = requested[i], created = flat[i].GetType().Name });
        return result;
    }

    // ── Validation ─────────────────────────────────────────────────────────

    /// <summary>Closure of each loop, then crossings between non-adjacent curves, within and across loops.</summary>
    private static void ValidateLoops(List<List<Curve>> loops, List<string> issues)
    {
        for (var l = 0; l < loops.Count; l++)
        {
            var loop = loops[l];
            for (var i = 0; i < loop.Count; i++)
            {
                var next = loop[(i + 1) % loop.Count];
                var gap = loop[i].GetEndPoint(1).DistanceTo(next.GetEndPoint(0)) * MmPerFoot;
                if (gap > ClosureToleranceMm)
                    issues.Add($"loop {l}: segment {i} ends {gap:F2} mm from where segment {(i + 1) % loop.Count} starts"
                               + (i == loop.Count - 1 ? " (the loop is not closed)." : "."));
                if (loop[i].Length * MmPerFoot < MinSegmentMm)
                    issues.Add($"loop {l}: segment {i} is {loop[i].Length * MmPerFoot:F2} mm long, under {MinSegmentMm} mm.");
            }
        }
        if (issues.Count > 0) return;

        var all = loops.SelectMany((loop, l) => loop.Select((curve, i) => (Loop: l, Index: i, Curve: curve, Count: loop.Count))).ToList();
        for (var a = 0; a < all.Count && issues.Count < 40; a++)
        {
            for (var b = a + 1; b < all.Count; b++)
            {
                var sameLoop = all[a].Loop == all[b].Loop;
                if (sameLoop)
                {
                    var distance = Math.Abs(all[a].Index - all[b].Index);
                    if (distance == 1 || distance == all[a].Count - 1) continue;   // adjacent: they touch by design
                }
                var relation = all[a].Curve.Intersect(all[b].Curve, CurveIntersectResultOption.Simple).Result;
                if (relation != SetComparisonResult.Disjoint)
                    issues.Add($"loop {all[a].Loop} segment {all[a].Index} touches or crosses loop {all[b].Loop} segment {all[b].Index} ({relation}).");
            }
        }
    }
}
