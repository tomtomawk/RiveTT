using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.IFC;

/// <summary>
/// Tags IFC-imported elements that cannot be rebuilt as native Revit elements.
/// Sets a value in the Comments parameter to mark them for manual review.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class IfcTagUnreconstructableElementsTool : IRiveTTTool
{
    public string Name => "ifc_tag_unreconstructable_elements";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Tag IFC elements that cannot be rebuilt, marking them for manual review. Previews by default: the dry "
        + "run names the elements it would tag and the Comments value it would overwrite on each. Set "
        + "dryRun=false to apply.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var tagValue = input["tagValue"]?.Value<string>() ?? "IFC_UNRECONSTRUCTABLE";

        List<DirectShape> targets;
        if (elementIds != null && elementIds.Length > 0)
        {
            targets = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            // Tag all DirectShapes with unknown/mesh geometry or low rebuild confidence
            targets = IfcGeometryHelper.GetDirectShapes(doc!)
                .Where(ds =>
                {
                    var geomType = IfcGeometryHelper.DetectGeometryType(ds);
                    return geomType == "mesh" || geomType == "unknown";
                })
                .ToList();
        }

        if (targets.Count == 0)
            return RiveTTResult<object>.Ok(new
            {
                tagged = 0,
                message = "No elements to tag",
            });

        // Comments is a user-visible field an architect may already be using for something
        // else, and this overwrites it. Report what is there now, per element, before doing it.
        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would set Comments to '{tagValue}' on {targets.Count} element(s).",
                new
                {
                    tagValue,
                    wouldTagCount = targets.Count,
                    elements = targets.Take(100).Select(ds => new
                    {
                        id = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        currentComments = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()
                    }).ToList()
                });

        int tagged = 0;
        var results = new List<object>();

        using var tx = new Transaction(doc!, "RiveTT: Tag Unreconstructable");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        foreach (var ds in targets)
        {
            try
            {
                var commentsParam = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentsParam != null && !commentsParam.IsReadOnly)
                {
                    commentsParam.Set(tagValue);
                    tagged++;
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "tagged",
                    });
                }
                else
                {
                    results.Add(new
                    {
                        elementId = ToolHelpers.GetElementIdValue(ds.Id),
                        name = ds.Name,
                        status = "skipped",
                        reason = "Comments parameter not writable",
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    elementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            tagged,
            tagValue,
            total = targets.Count,
            results,
        });
    }
}
