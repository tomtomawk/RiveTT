using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Caching;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists all materials in the project with optional filtering by material class or name.
/// </summary>
[ToolSafety(true, false)]
public class GetMaterialsTool : IRiveTTTool, ICacheableTool
{
    public string Name => "list_materials";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all materials in the project with optional filtering by material class or name.";
    public CacheScope CacheScope => CacheScope.Document;
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var materialClass = input["materialClass"]?.Value<string>() ?? "";
        var nameFilter    = input["nameFilter"]?.Value<string>() ?? "";

        try
        {
            var allMaterials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>();

            if (!string.IsNullOrEmpty(materialClass))
                allMaterials = allMaterials.Where(m =>
                    string.Equals(m.MaterialClass, materialClass, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(nameFilter))
                allMaterials = allMaterials.Where(m =>
                    m.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            var materials = allMaterials.Select(m => new
            {
                id = m.Id.Value,
                name             = m.Name,
                materialClass    = m.MaterialClass,
                materialCategory = m.MaterialCategory,
                color            = FormatColor(m.Color),
                transparency     = m.Transparency,
                shininess        = m.Shininess,
                smoothness       = m.Smoothness,
                hasAppearanceAsset = m.AppearanceAssetId != ElementId.InvalidElementId,
                hasStructuralAsset = m.StructuralAssetId != ElementId.InvalidElementId,
                hasThermalAsset    = m.ThermalAssetId != ElementId.InvalidElementId
            }).ToList();

            return RiveTTResult<object>.Ok(new
            {
                materialCount = materials.Count,
                materials
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"list_materials could not get materials: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static string? FormatColor(Color? color)
    {
        if (color == null || !color.IsValid) return null;
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }
}
