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
/// Deletes a material from the project. Defaults to dryRun=true.
///
/// It previously called session.RequestConfirmation, which is a no-op that always returns
/// true (RiveTT has no dialogs), so the "confirmation" in the old description was not a
/// safety net at all: a single call destroyed the material outright. Deleting a material
/// strips it from every compound structure and paint that referenced it, which is why the
/// preview probes the real cascade instead of only naming the material.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class DeleteMaterialTool : IRiveTTTool
{
    public string Name => "delete_material";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Deletes a material from the project by ID or name. Defaults to dryRun=true: the preview names the "
        + "material and reports the real deletion cascade. Set dryRun=false to execute.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var materialId   = input["materialId"]?.Value<long?>();
        var materialName = input["materialName"]?.Value<string>();

        if (materialId == null && string.IsNullOrWhiteSpace(materialName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Provide materialId or materialName",
                suggestion: "Use list_materials to find the material to delete");

        try
        {
            Material? material = null;

            if (materialId.HasValue)
            {
                material = doc.GetElement(new ElementId(materialId.Value)) as Material;
            }

            if (material == null && !string.IsNullOrWhiteSpace(materialName))
            {
                material = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
            }

            if (material == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Material not found (id={materialId}, name={materialName})",
                    suggestion: "Use list_materials to list available materials");

            var matName = material.Name;

            if (ToolHelpers.GetDryRun(input))
                return DeletionPreview.Build(doc, material.Id,
                    $"Material '{matName}'",
                    new { materialId = ToolHelpers.GetElementIdValue(material.Id), materialName = matName });

            using (var tx = new Transaction(doc, "RiveTT: Delete Material"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                doc.Delete(material.Id);
                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            return RiveTTResult<object>.Ok(new
            {
                deleted = true,
                materialName = matName,
                message = $"Material '{matName}' deleted"
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"delete_material could not delete material: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
