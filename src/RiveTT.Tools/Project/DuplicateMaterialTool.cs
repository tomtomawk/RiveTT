using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Duplicates an existing material with a new name, copying all properties and assets.
/// </summary>
[ToolSafety(false, false)]
public class DuplicateMaterialTool : IRiveTTTool
{
    public string Name => "duplicate_material";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates an existing material with a new name, copying color, class, transparency, and optionally appearance/structural/thermal assets.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var sourceMaterialId   = input["sourceMaterialId"]?.Value<long?>();
        var sourceMaterialName = input["sourceMaterialName"]?.Value<string>();
        var newName            = input["newName"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide a name for the duplicate material");

        if (sourceMaterialId == null && string.IsNullOrWhiteSpace(sourceMaterialName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Provide sourceMaterialId or sourceMaterialName",
                suggestion: "Use list_materials to find the source material");

        try
        {
            Material? source = null;

            if (sourceMaterialId.HasValue)
            {
                source = doc.GetElement(new ElementId(sourceMaterialId.Value)) as Material;
            }

            if (source == null && !string.IsNullOrWhiteSpace(sourceMaterialName))
            {
                source = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(sourceMaterialName, StringComparison.OrdinalIgnoreCase));
            }

            if (source == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Source material not found (id={sourceMaterialId}, name={sourceMaterialName})",
                    suggestion: "Use list_materials to list available materials");

            ElementId newMatId;

            using (var tx = new Transaction(doc, "RiveTT: Duplicate Material"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                newMatId = Material.Create(doc, newName);
                var newMat = doc.GetElement(newMatId) as Material;
                if (newMat == null)
                {
                    tx.RollBack();
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, "Failed to create duplicate material",
                    suggestion: "Revit refuses the duplicate when the new name is already taken. List the existing materials with get_materials and retry with a free name.");
                }

                // Copy basic properties
                newMat.MaterialClass = source.MaterialClass;
                newMat.MaterialCategory = source.MaterialCategory;
                if (source.Color != null && source.Color.IsValid)
                    newMat.Color = source.Color;
                newMat.Transparency = source.Transparency;
                newMat.Shininess = source.Shininess;
                newMat.Smoothness = source.Smoothness;

                // Copy appearance asset
                if (source.AppearanceAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcAsset = doc.GetElement(source.AppearanceAssetId) as AppearanceAssetElement;
                        if (srcAsset != null)
                        {
                            var dupAsset = srcAsset.Duplicate($"{newName}_Appearance");
                            newMat.AppearanceAssetId = dupAsset.Id;
                        }
                    }
                    catch { /* appearance asset duplication not critical */ }
                }

                // Copy structural asset
                if (source.StructuralAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcPropSet = doc.GetElement(source.StructuralAssetId) as PropertySetElement;
                        if (srcPropSet != null)
                        {
                            var dupPropSet = PropertySetElement.Create(doc, srcPropSet.GetStructuralAsset());
                            newMat.StructuralAssetId = dupPropSet.Id;
                        }
                    }
                    catch { /* structural asset copy not critical */ }
                }

                // Copy thermal asset
                if (source.ThermalAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcPropSet = doc.GetElement(source.ThermalAssetId) as PropertySetElement;
                        if (srcPropSet != null)
                        {
                            var dupPropSet = PropertySetElement.Create(doc, srcPropSet.GetThermalAsset());
                            newMat.ThermalAssetId = dupPropSet.Id;
                        }
                    }
                    catch { /* thermal asset copy not critical */ }
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            long newIdValue;
            newIdValue = newMatId.Value;

            return RiveTTResult<object>.Ok(new
            {
                materialId = newIdValue,
                name = newName,
                sourceName = source.Name,
                message = $"Material '{source.Name}' duplicated as '{newName}'"
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"duplicate_material could not duplicate material: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
