using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Views;

/// <summary>
/// Creates a 3D section box from selected elements' combined bounding box.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class SectionBoxFromSelectionTool : IRiveTTTool
{
    public string Name => "create_section_box_from_selection";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a 3D section box from selected elements' combined bounding box.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var offsetMm = input["offset"]?.Value<double>() ?? 1000;
        var duplicateView = input["duplicateView"]?.Value<bool>() ?? true;
        var viewName = input["viewName"]?.Value<string>();

        if (elementIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "elementIds array is required");

        try
        {
            // Compute combined bounding box
            XYZ? minPt = null, maxPt = null;
            foreach (var eid in elementIds)
            {
                var elem = doc.GetElement(new ElementId(eid));
                if (elem == null) continue;
                var bb = elem.get_BoundingBox(null);
                if (bb == null) continue;

                minPt = minPt == null ? bb.Min : new XYZ(
                    Math.Min(minPt.X, bb.Min.X), Math.Min(minPt.Y, bb.Min.Y), Math.Min(minPt.Z, bb.Min.Z));
                maxPt = maxPt == null ? bb.Max : new XYZ(
                    Math.Max(maxPt.X, bb.Max.X), Math.Max(maxPt.Y, bb.Max.Y), Math.Max(maxPt.Z, bb.Max.Z));
            }

            if (minPt == null || maxPt == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No valid bounding boxes found");

            var offset = offsetMm / MmPerFoot;
            var sectionBox = new BoundingBoxXYZ
            {
                Min = new XYZ(minPt.X - offset, minPt.Y - offset, minPt.Z - offset),
                Max = new XYZ(maxPt.X + offset, maxPt.Y + offset, maxPt.Z + offset)
            };

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Section Box From Selection");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            View3D? targetView;
            if (duplicateView)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (vft == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "No 3D view family type");

                targetView = View3D.CreateIsometric(doc, vft.Id);
                targetView.Name = viewName ?? $"SectionBox_{DateTime.Now:HHmmss}";
            }
            else
            {
                targetView = doc.ActiveView as View3D
                    ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate);
            }

            if (targetView == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "No 3D view available to apply a section box",
                    suggestion: "Open or create a 3D view, or pass duplicateView=true");

            targetView.SetSectionBox(sectionBox);
            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                viewId = ToolHelpers.GetElementIdValue(targetView.Id),
                viewName = targetView.Name,
                elementCount = elementIds.Count
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
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
