using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Returns the currently selected elements in the Revit UI, with their geometry.
///
/// Ids and categories alone were not enough to act on a selection: in the field session of
/// 2026-09-24 four detail lines forming a closed outline were read as an axis, and the only
/// way to see their end points was send_code_to_revit — refused while RiveTT was locked.
/// This read tool now returns the curves (end points, length), the location points, the
/// bounding boxes in millimetres, and an analysis of how the selected curves connect.
/// </summary>
[ToolSafety(true, false)]
public class GetSelectedElementsTool : IRiveTTTool
{
    public string Name => "get_selected_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns the currently selected elements in the Revit UI: id, name, category and OST_* code, type, level, and (includeGeometry, default true) curve end points and length, location point, bounding box in mm, plus which selected curves form closed loops.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var limit = input["limit"]?.Value<int>() ?? 500;
        var includeGeometry = input["includeGeometry"]?.Value<bool>() ?? true;

        try
        {
            var uiDoc = new UIDocument(doc);
            var selected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();

            var returned = limit > 0 ? selected.Take(limit).ToList() : selected;
            // The loops are read on the WHOLE selection: a contour cut by the page limit would
            // read as an open chain.
            var segments = new List<CurveChains.Segment>();
            if (includeGeometry)
                foreach (var element in selected)
                    AddSegment(element, segments);

            var elements = returned.Select(e =>
            {
                var item = new JObject
                {
                    ["id"] = ToolHelpers.GetElementIdValue(e.Id),
                    ["uniqueId"] = e.UniqueId,
                    ["name"] = SafeName(e),
                    ["category"] = e.Category?.Name,
                    ["categoryBic"] = CategoryResolver.DescribeBuiltInCategory(e.Category)
                };
                var typeId = e.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType type)
                {
                    item["typeId"] = ToolHelpers.GetElementIdValue(type.Id);
                    item["typeName"] = type.Name;
                    if (type is FamilySymbol symbol) item["familyName"] = symbol.FamilyName;
                }
                if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId && doc.GetElement(e.LevelId) is Level level)
                {
                    item["levelId"] = ToolHelpers.GetElementIdValue(level.Id);
                    item["levelName"] = level.Name;
                }

                if (includeGeometry)
                {
                    var geometry = DescribeGeometry(e);
                    if (geometry != null) item["geometry"] = geometry;
                }
                return item;
            }).ToList();

            var result = new JObject
            {
                ["message"] = selected.Count == 0
                    ? "No elements are currently selected"
                    : returned.Count < selected.Count
                        ? $"Found {selected.Count} selected element(s); returning the first {returned.Count} (limit)."
                        : $"Found {selected.Count} selected element(s)",
                // The counter describes the SELECTION, not the page returned: a trimmed list
                // must never be mistaken for the whole of it.
                ["selectedCount"] = selected.Count,
                ["returnedCount"] = returned.Count,
                ["truncated"] = returned.Count < selected.Count,
                ["units"] = "mm",
                ["elements"] = new JArray(elements)
            };

            if (includeGeometry && segments.Count > 0)
            {
                var chains = CurveChains.Analyze(segments);
                result["curveAnalysis"] = new JObject
                {
                    ["curveCount"] = segments.Count,
                    ["reading"] = CurveChains.Reading(chains),
                    ["chains"] = JArray.FromObject(chains.Select(chain => new
                    {
                        elementIds = chain.ElementIds,
                        closed = chain.Closed,
                        lengthMm = chain.LengthMm,
                        areaM2 = chain.AreaM2,
                        widthMm = chain.WidthMm,
                        heightMm = chain.HeightMm
                    }))
                };
            }

            return RiveTTResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"get_selected_elements could not retrieve selected elements: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static Curve? CurveOf(Element element) => element switch
    {
        CurveElement curveElement => SafeCurve(() => curveElement.GeometryCurve),
        _ => element.Location is LocationCurve locationCurve ? SafeCurve(() => locationCurve.Curve) : null
    };

    private static void AddSegment(Element element, List<CurveChains.Segment> segments)
    {
        var curve = CurveOf(element);
        if (curve == null || !curve.IsBound) return;
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        segments.Add(new CurveChains.Segment(ToolHelpers.GetElementIdValue(element.Id),
            start.X * MmPerFoot, start.Y * MmPerFoot, end.X * MmPerFoot, end.Y * MmPerFoot,
            curve.Length * MmPerFoot));
    }

    private static JObject? DescribeGeometry(Element element)
    {
        var geometry = new JObject();
        var curve = CurveOf(element);

        if (curve != null && curve.IsBound)
        {
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            geometry["kind"] = "curve";
            geometry["curveType"] = curve switch { Line => "Line", Arc => "Arc", _ => curve.GetType().Name };
            geometry["start"] = Point(start);
            geometry["end"] = Point(end);
            geometry["lengthMm"] = Math.Round(curve.Length * MmPerFoot, 1);
            if (curve is Arc arc)
            {
                geometry["center"] = Point(arc.Center);
                geometry["radiusMm"] = Math.Round(arc.Radius * MmPerFoot, 1);
            }
        }
        else if (element.Location is LocationPoint locationPoint)
        {
            geometry["kind"] = "point";
            geometry["point"] = Point(locationPoint.Point);
            try { geometry["rotationDeg"] = Math.Round(locationPoint.Rotation * 180 / Math.PI, 2); } catch { }
        }

        var box = SafeBox(element);
        if (box != null)
        {
            geometry["boundingBoxMm"] = new JObject
            {
                ["min"] = Point(box.Min),
                ["max"] = Point(box.Max),
                // A family's box includes invisible clearance geometry: it is NOT its visible
                // footprint (a 1.40 x 1.90 m bed measured 2.85 x 3.90 m). See describe_family.
                ["note"] = element is FamilyInstance ? "includes invisible clearances of the family" : null
            };
        }

        return geometry.HasValues ? geometry : null;
    }

    private static JObject Point(XYZ point) => new()
    {
        ["x"] = Math.Round(point.X * MmPerFoot, 1),
        ["y"] = Math.Round(point.Y * MmPerFoot, 1),
        ["z"] = Math.Round(point.Z * MmPerFoot, 1)
    };

    private static Curve? SafeCurve(Func<Curve?> read)
    {
        try { return read(); } catch { return null; }
    }

    private static BoundingBoxXYZ? SafeBox(Element element)
    {
        try { return element.get_BoundingBox(null); } catch { return null; }
    }

    private static string? SafeName(Element element)
    {
        try { return element.Name; } catch { return null; }
    }
}
