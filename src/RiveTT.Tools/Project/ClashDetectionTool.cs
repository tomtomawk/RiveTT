using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Detects geometric intersections (clashes) between two sets of elements.
/// </summary>
[ToolSafety(true, false)]
public class ClashDetectionTool : ICortexTool
{
    public string Name => "clash_detection";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Detects geometric intersections (clashes) between two sets of elements. Uses true solid-geometry intersection by default (bounding-box pre-filter + ElementIntersectsElementFilter); set useSolidGeometry=false for a faster bbox-only approximation.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryA = input["categoryA"]?.Value<string>() ?? input["category1"]?.Value<string>();
        var categoryB = input["categoryB"]?.Value<string>() ?? input["category2"]?.Value<string>();
        var elementIdsA = input["elementIdsA"]?.ToObject<List<long>>() ?? new List<long>();
        var elementIdsB = input["elementIdsB"]?.ToObject<List<long>>() ?? new List<long>();
        var toleranceMm = input["tolerance"]?.Value<double>() ?? 0;
        var maxResults = input["maxResults"]?.Value<int>() ?? 100;
        // True solid-geometry intersection (default) vs. bbox-only approximation.
        var useSolidGeometry = input["useSolidGeometry"]?.Value<bool>() ?? true;

        try
        {
            // Resolve set A
            List<Element> setA;
            if (elementIdsA.Count > 0)
            {
                setA = elementIdsA.Select(id =>
                {
                    return doc.GetElement(new ElementId(id));
                }).Where(e => e != null).ToList()!;
            }
            else if (!string.IsNullOrEmpty(categoryA))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryA!);
                setA = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType().ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryA or elementIdsA required");
            }

            // Resolve set B
            List<Element> setB;
            if (elementIdsB.Count > 0)
            {
                setB = elementIdsB.Select(id =>
                {
                    return doc.GetElement(new ElementId(id));
                }).Where(e => e != null).ToList()!;
            }
            else if (!string.IsNullOrEmpty(categoryB))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryB!);
                setB = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType().ToList();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryB or elementIdsB required");
            }

            // The detection pass itself lives in ClashFinder so workflow_clash_review runs
            // exactly this one and cannot drift back to a bbox-only answer.
            var found = ClashFinder.Find(
                doc, setA, setB, toleranceMm / MmPerFoot, maxResults, useSolidGeometry);

            return CortexResult<object>.Ok(new
            {
                setACount = setA.Count,
                setBCount = setB.Count,
                method = found.Method,
                clashCount = found.Hits.Count,
                truncated = found.Truncated,
                maxResults,
                clashes = found.Hits
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Clash detection failed: {ex.Message}",
                suggestion: "Check that both categories exist in this document (list them with "
                          + "analyze_model_statistics) and lower maxResults on a very large model.");
        }
    }
}
