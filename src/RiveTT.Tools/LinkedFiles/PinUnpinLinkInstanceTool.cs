using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.LinkedFiles;

/// <summary>
/// Pins or unpins one or more link instances.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class PinUnpinLinkInstanceTool : IRiveTTTool
{
    public string Name => "pin_unpin_link_instance";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Pins or unpins one or more linked file instances to prevent or allow accidental movement.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var instanceIds = input["instanceIds"]?.ToObject<List<long>>() ?? new List<long>();
        var pin = input["pin"]?.Value<bool>() ?? true;

        if (instanceIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "instanceIds array is required");

        var action = pin ? "pin" : "unpin";
        if (!session.RequestConfirmation($"{action} link instance(s)", instanceIds.Count))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            var results = new List<object>();
            int successCount = 0;

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, $"RiveTT: {(pin ? "Pin" : "Unpin")} Link Instance");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var id in instanceIds)
            {
                var element = doc.GetElement(new ElementId(id));
                var linkInstance = element as RevitLinkInstance;
                if (linkInstance == null)
                {
                    results.Add(new { instanceId = id, success = false, message = "Not a RevitLinkInstance" });
                    continue;
                }

                linkInstance.Pinned = pin;
                results.Add(new { instanceId = id, success = true, name = linkInstance.Name, pinned = pin });
                successCount++;
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                message = $"{(pin ? "Pinned" : "Unpinned")} {successCount}/{instanceIds.Count} instance(s)",
                action,
                results
            };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    previewPayload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

return RiveTTResult<object>.Ok(previewPayload);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"pin_unpin_link_instance could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
