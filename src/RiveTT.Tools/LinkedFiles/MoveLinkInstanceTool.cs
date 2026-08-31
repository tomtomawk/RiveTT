using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.LinkedFiles;

/// <summary>
/// Moves a linked file instance by a delta offset or to an absolute position (in mm).
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class MoveLinkInstanceTool : IRiveTTTool
{
    public string Name => "move_link_instance";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Moves a linked file instance by a delta offset (mm) or to an absolute position (mm). Specify mode: 'delta' or 'absolute'.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long>() ?? 0;
        var x = input["x"]?.Value<double>() ?? 0;
        var y = input["y"]?.Value<double>() ?? 0;
        var z = input["z"]?.Value<double>() ?? 0;
        var mode = input["mode"]?.Value<string>() ?? "delta";

        if (instanceId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "instanceId is required");

        try
        {
            var element = doc.GetElement(new ElementId(instanceId));
            var linkInstance = element as RevitLinkInstance;
            if (linkInstance == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Element {instanceId} is not a RevitLinkInstance");

            if (linkInstance.Pinned)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.PermissionDenied,
                    "Link instance is pinned. Unpin it first using pin_unpin_link_instance.");

            if (!session.RequestConfirmation("move link instance", 1, $"Move '{linkInstance.Name}'"))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

            var currentTransform = linkInstance.GetTotalTransform();
            XYZ translation;

            if (mode.Equals("absolute", StringComparison.OrdinalIgnoreCase))
            {
                // Move to absolute position: calculate delta from current origin
                var targetFt = new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
                translation = targetFt - currentTransform.Origin;
            }
            else
            {
                // Delta mode: move by offset
                translation = new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
            }

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Move Link Instance");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            ElementTransformUtils.MoveElement(doc, linkInstance.Id, translation);
            // dryRun keeps the transaction OPEN so the payload below can still read the
            // elements it describes; the rollback happens just before returning.
            if (!dryRun && tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            // Read new position
            var newTransform = linkInstance.GetTotalTransform();
            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The "
                    + "model is untouched; what follows is what Revit produced.",
                    new
            {
                instanceId,
                name = linkInstance.Name,
                mode,
                newOrigin = new
                {
                    x = Math.Round(newTransform.Origin.X * MmPerFoot, 1),
                    y = Math.Round(newTransform.Origin.Y * MmPerFoot, 1),
                    z = Math.Round(newTransform.Origin.Z * MmPerFoot, 1)
                }
            });
            }

            return RiveTTResult<object>.Ok(new
            {
                instanceId,
                name = linkInstance.Name,
                mode,
                newOrigin = new
                {
                    x = Math.Round(newTransform.Origin.X * MmPerFoot, 1),
                    y = Math.Round(newTransform.Origin.Y * MmPerFoot, 1),
                    z = Math.Round(newTransform.Origin.Z * MmPerFoot, 1)
                }
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"move_link_instance could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
