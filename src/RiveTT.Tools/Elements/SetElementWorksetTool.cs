using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Moves one or more elements to a named user workset.
/// IsDynamic = true — only available when the document is workshared.
/// Mirrors the fork's SetElementWorksetEventHandler logic.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class SetElementWorksetTool : IRiveTTTool
{
    public string Name => "set_element_workset";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Moves one or more elements to a named user workset. IsDynamic = true — only available when the document is workshared. Mirrors the fork's SetElementWorksetEventHandler logic.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var requests = input["requests"]?.ToObject<List<SetWorksetRequest>>();
        if (requests == null || requests.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "requests array is required",
                suggestion: "Provide [{\"elementId\": 123, \"worksetName\": \"Structure\"}]");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        if (!doc.IsWorkshared)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Project is not workshared. Worksets are not available.");

        var results = new List<object>();
        var successCount = 0;
        var failCount = 0;

        if (!session.RequestConfirmation("change workset for", requests.Count))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        var dryRun = ToolHelpers.GetDryRun(input);
        using var tx = new Transaction(doc, "RiveTT: Set Element Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            foreach (var req in requests)
            {
                // Find the target workset by name (case-insensitive)
                var wsCollector = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset);

                Workset? targetWorkset = null;
                foreach (var ws in wsCollector)
                {
                    if (ws.Name.Equals(req.WorksetName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetWorkset = ws;
                        break;
                    }
                }

                if (targetWorkset == null)
                {
                    results.Add(new { elementId = req.ElementId, worksetName = req.WorksetName,
                        success = false, message = $"Workset '{req.WorksetName}' not found" });
                    failCount++;
                    continue;
                }

                var elementId = new ElementId(req.ElementId);
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    results.Add(new { elementId = req.ElementId, worksetName = req.WorksetName,
                        success = false, message = $"Element {req.ElementId} not found" });
                    failCount++;
                    continue;
                }

                var worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (worksetParam == null)
                {
                    results.Add(new { elementId = req.ElementId, worksetName = req.WorksetName,
                        success = false, message = $"Element {req.ElementId} does not support workset assignment" });
                    failCount++;
                    continue;
                }

                if (worksetParam.IsReadOnly)
                {
                    results.Add(new { elementId = req.ElementId, worksetName = req.WorksetName,
                        success = false, message = $"Workset parameter on element {req.ElementId} is read-only" });
                    failCount++;
                    continue;
                }

                worksetParam.Set(targetWorkset.Id.IntegerValue);
                results.Add(new { elementId = req.ElementId, worksetName = req.WorksetName,
                    success = true, message = $"Element moved to workset '{req.WorksetName}' successfully" });
                successCount++;
            }

            // dryRun keeps the transaction OPEN so the payload below can still read the
            // elements it describes; the rollback happens just before returning.
            if (!dryRun && tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started)
                tx.RollBack();
            throw;
        }

        if (dryRun)
        {
            ChangePreview.Rollback(tx);
            return ChangePreview.Probed(
                "DryRun: the operation ran inside a transaction and was rolled back. The "
                + "model is untouched; what follows is what Revit produced.",
                new
        {
            message = $"Moved {successCount}/{requests.Count} element(s) to target workset successfully",
            successCount,
            failCount,
            results
        });
        }

        return RiveTTResult<object>.Ok(new
        {
            message = $"Moved {successCount}/{requests.Count} element(s) to target workset successfully",
            successCount,
            failCount,
            results
        });
    }

    private class SetWorksetRequest
    {
        public long ElementId { get; set; }
        public string WorksetName { get; set; } = "";
    }
}
