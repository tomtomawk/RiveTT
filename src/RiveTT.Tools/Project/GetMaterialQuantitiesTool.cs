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

namespace RiveTT.Tools.Project;

/// <summary>
/// Calculates total area and volume of materials across selected or all elements.
/// Heavy query — can take time on large models.
/// </summary>
[ToolSafety(true, false)]
public class GetMaterialQuantitiesTool : IRiveTTTool
{
    public string Name => "get_material_quantities";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Calculates total area and volume of materials across selected or all elements. Heavy query — can take time on large models.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var categoryFilters      = input["categoryFilters"]?.ToObject<List<string>>() ?? new List<string>();
        var selectedElementsOnly = input["selectedElementsOnly"]?.Value<bool>() ?? false;
        var maxResults           = input["maxResults"]?.Value<int>() ?? 50;
        var maxElements          = input["maxElements"]?.Value<int>() ?? 20000;

        try
        {
            List<Element> elements;

            if (selectedElementsOnly)
            {
                var uiDoc = new UIDocument(doc);
                var selectedIds = uiDoc.Selection.GetElementIds();
                elements = selectedIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            }
            else
            {
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                if (categoryFilters.Count > 0)
                {
                    var catIds = new List<ElementId>();
                    foreach (var catName in categoryFilters)
                    {
                        var catId = CategoryResolver.ResolveToId(doc, catName);
                        if (catId != null && catId != ElementId.InvalidElementId)
                            catIds.Add(catId);
                    }
                    if (catIds.Count > 0)
                        collector = collector.WherePasses(new ElementMulticategoryFilter(catIds, false));
                }

                elements = collector.ToList();
            }

            // GetMaterialArea/GetMaterialVolume are geometry-backed and run on the
            // Revit UI thread: an unbounded full-model pass freezes Revit far past
            // the 120s dispatcher timeout, and partial sums would be silently wrong.
            // Over-cap is therefore a structured failure, not a truncated result.
            if (elements.Count > maxElements)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"{elements.Count} elements match, above the cap of {maxElements}. Processing them all would freeze Revit's UI thread.",
                    suggestion: "Narrow the query with categoryFilters or selectedElementsOnly, or raise maxElements explicitly if you accept the wait.");

            // Accumulate material quantities
            var materialData = new Dictionary<ElementId, (string name, string matClass, double area, double volume, int elementCount, List<long> elementIds)>();

            // Budget kept under the 120s dispatcher timeout so the caller receives
            // this structured error instead of the generic dispatcher Timeout while
            // Revit's UI thread keeps grinding.
            const int TimeBudgetMs = 90000;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var processed = 0;

            foreach (var elem in elements)
            {
                if ((++processed % 500) == 0 && elapsed.ElapsedMilliseconds > TimeBudgetMs)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.Timeout,
                        $"Time budget exceeded after {processed}/{elements.Count} elements; partial totals would be misleading and were discarded.",
                        suggestion: "Narrow the query with categoryFilters or selectedElementsOnly.");

                ICollection<ElementId> matIds;
                try { matIds = elem.GetMaterialIds(false); }
                catch { continue; }

                foreach (var matId in matIds)
                {
                    double area = 0, volume = 0;
                    try { area = elem.GetMaterialArea(matId, false); } catch { }
                    try { volume = elem.GetMaterialVolume(matId); } catch { }

                    if (!materialData.ContainsKey(matId))
                    {
                        var mat = doc.GetElement(matId) as Material;
                        materialData[matId] = (
                            mat?.Name ?? "Unknown",
                            mat?.MaterialClass ?? "",
                            0, 0, 0,
                            new List<long>()
                        );
                    }

                    var entry = materialData[matId];
                    long elemIdLong;
                    elemIdLong = elem.Id.Value;
                    materialData[matId] = (
                        entry.name, entry.matClass,
                        entry.area + area,
                        entry.volume + volume,
                        entry.elementCount + 1,
                        entry.elementIds
                    );
                    entry.elementIds.Add(elemIdLong);
                }
            }

            var totalCount = materialData.Count;
            var truncated = totalCount > maxResults;

            var materials = materialData
                .OrderByDescending(kv => kv.Value.volume)
                .Take(maxResults)
                .Select(kv => new
                {
                    materialId = kv.Key.Value,
                    materialName  = kv.Value.name,
                    materialClass = kv.Value.matClass,
                    area          = Math.Round(kv.Value.area, 4),
                    volume        = Math.Round(kv.Value.volume, 4),
                    elementCount  = kv.Value.elementCount
                }).ToList();

            return RiveTTResult<object>.Ok(new
            {
                totalMaterials = materials.Count,
                totalCount,
                truncated,
                totalArea  = Math.Round(materialData.Values.Sum(v => v.area), 4),
                totalVolume = Math.Round(materialData.Values.Sum(v => v.volume), 4),
                materials
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"get_material_quantities could not get material quantities: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
