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
/// Reads the TOP surface of floors and roofs: its highest and lowest point and the slope, in three
/// elevation systems. A ramp modelled as a sloped slab could not be checked: the level elevation says
/// nothing about where its upper face ends up (field report, ramp at 7.17 m NGF at the high point).
/// The top surface is every upward-facing planar face of the element's solids; its highest and lowest
/// vertices are read from the face boundaries, which is exact for planar slabs and roofs.
/// </summary>
[ToolSafety(true, false)]
public sealed class MeasureFloorTopTool : IRiveTTTool
{
    public string Name => "measure_floor_top";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Read-only: the top surface of floors and roofs (elementIds). For each element: its highest and lowest point of " +
        "the upward-facing surface with x/y in mm, and their elevation as internalMm (from the internal origin), " +
        "projectMm (from the project base point) and sharedMm (shared coordinates: NGF when the project is " +
        "georeferenced to it); slopePercent of the largest upward planar face; the level. Curved top surfaces are " +
        "measured on their boundary vertices only, and flagged.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var ids = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (ids.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "elementIds is required",
                suggestion: "Floor or roof element ids, from filter_elements (OST_Floors, OST_Roofs).");

        try
        {
            double projectBaseZ = 0;
            try { projectBaseZ = BasePoint.GetProjectBasePoint(doc).Position.Z; } catch { /* no base point */ }
            double originSharedFt = 0;
            try { originSharedFt = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation; } catch { /* none */ }

            object Elevations(double z) => new
            {
                internalMm = Math.Round(z * MmPerFoot, 1),
                projectMm = Math.Round((z - projectBaseZ) * MmPerFoot, 1),
                sharedMm = Math.Round((z + originSharedFt) * MmPerFoot, 1)
            };

            var results = new List<object>();
            var notFound = new List<long>();
            foreach (var id in ids)
            {
                var element = doc.GetElement(new ElementId(id));
                if (element == null) { notFound.Add(id); continue; }

                var topFaces = new List<(PlanarFace Face, double Area)>();
                var curvedTop = false;
                foreach (var solid in Solids(element))
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace planar)
                        {
                            if (planar.FaceNormal.Z > 0.01) topFaces.Add((planar, planar.Area));
                        }
                        else
                        {
                            var range = face.GetBoundingBox();
                            var middle = new UV((range.Min.U + range.Max.U) / 2, (range.Min.V + range.Max.V) / 2);
                            if (face.ComputeNormal(middle).Z > 0.01) curvedTop = true;
                        }
                    }
                }

                if (topFaces.Count == 0)
                {
                    results.Add(new { elementId = id, found = true, category = element.Category?.Name,
                        error = "No upward-facing planar face: not a floor/roof with a measurable top surface." });
                    continue;
                }

                var vertices = topFaces.SelectMany(t => t.Face.GetEdgesAsCurveLoops())
                    .SelectMany(loop => loop).Select(curve => curve.GetEndPoint(0)).ToList();
                var high = vertices.OrderByDescending(p => p.Z).First();
                var low = vertices.OrderBy(p => p.Z).First();
                var biggest = topFaces.OrderByDescending(t => t.Area).First().Face;
                var nz = Math.Min(1, biggest.FaceNormal.Z);
                var slopePercent = nz > 1e-9 ? Math.Round(Math.Sqrt(Math.Max(0, 1 - nz * nz)) / nz * 100, 2) : (double?)null;

                var level = doc.GetElement(element.LevelId) as Level
                            ?? doc.GetElement(element.get_Parameter(BuiltInParameter.ROOF_BASE_LEVEL_PARAM)?.AsElementId()
                                              ?? ElementId.InvalidElementId) as Level;

                results.Add(new
                {
                    elementId = id,
                    found = true,
                    category = element.Category?.Name,
                    levelName = level?.Name,
                    highPoint = new { xMm = Math.Round(high.X * MmPerFoot, 1), yMm = Math.Round(high.Y * MmPerFoot, 1), elevation = Elevations(high.Z) },
                    lowPoint = new { xMm = Math.Round(low.X * MmPerFoot, 1), yMm = Math.Round(low.Y * MmPerFoot, 1), elevation = Elevations(low.Z) },
                    heightDifferenceMm = Math.Round((high.Z - low.Z) * MmPerFoot, 1),
                    slopePercentOfLargestFace = slopePercent,
                    upwardFaceCount = topFaces.Count,
                    warnings = curvedTop
                        ? new[] { "The element also has a curved upward face: only the planar faces' vertices were measured." }
                        : Array.Empty<string>()
                });
            }

            return RiveTTResult<object>.Ok(new
            {
                measured = results.Count,
                notFoundIds = notFound,
                elevationReference = new
                {
                    projectBasePointInternalMm = Math.Round(projectBaseZ * MmPerFoot, 1),
                    internalOriginSharedMm = Math.Round(originSharedFt * MmPerFoot, 1)
                },
                results
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"measure_floor_top could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids with a read tool before retrying. The full call, its duration and this error "
                    + "are in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static IEnumerable<Solid> Solids(Element element)
    {
        var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false });
        if (geometry == null) yield break;
        foreach (var obj in geometry)
        {
            if (obj is Solid solid && solid.Faces.Size > 0) yield return solid;
            else if (obj is GeometryInstance instance)
                foreach (var inner in instance.GetInstanceGeometry())
                    if (inner is Solid innerSolid && innerSolid.Faces.Size > 0) yield return innerSolid;
        }
    }
}
