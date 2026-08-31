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

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a filled region from boundary points in the specified view.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class CreateFilledRegionTool : IRiveTTTool
{
    public string Name => "create_filled_region";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a filled region from boundary points in the specified view, optionally with holes (inner loops).";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var boundaryPoints = input["boundaryPoints"] as JArray;
        if (boundaryPoints == null || boundaryPoints.Count < 3)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "boundaryPoints array with minimum 3 points is required");

        var viewIdLong = input["viewId"]?.Value<long>() ?? -1;
        var typeName = input["filledRegionTypeName"]?.Value<string>();

        try
        {
            // Resolve view
            View? view;
            if (viewIdLong > 0)
            {
                view = doc.GetElement(new ElementId(viewIdLong)) as View;
            }
            else
            {
                view = doc.ActiveView;
            }

            if (view == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "Could not resolve view");

            // Resolve type
            var regionType = !string.IsNullOrEmpty(typeName)
                ? new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                : null;
            regionType ??= new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault();

            if (regionType == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    "No filled region types available");

            // Build boundary curve loop
            var points = boundaryPoints.Select(p => new XYZ(
                p["x"]!.Value<double>() / MmPerFoot,
                p["y"]!.Value<double>() / MmPerFoot,
                0)).ToList();

            var loop = new CurveLoop();
            for (int i = 0; i < points.Count; i++)
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));

            var loops = new List<CurveLoop> { loop };
            var warnings = new List<string>();
            var holes = input["holes"] as JArray;
            if (holes != null)
            {
                int holeIndex = 0;
                foreach (var hole in holes.OfType<JArray>())
                {
                    holeIndex++;
                    if (hole.Count < 3) { warnings.Add($"Hole {holeIndex} skipped: needs at least 3 points"); continue; }
                    try
                    {
                        var hpts = hole.Select(p => new XYZ(
                            p["x"]!.Value<double>() / MmPerFoot,
                            p["y"]!.Value<double>() / MmPerFoot, 0)).ToList();
                        var hloop = new CurveLoop();
                        for (int i = 0; i < hpts.Count; i++)
                            hloop.Append(Line.CreateBound(hpts[i], hpts[(i + 1) % hpts.Count]));
                        loops.Add(hloop);
                    }
                    catch (Exception ex) { warnings.Add($"Hole {holeIndex} skipped: {ex.Message}"); }
                }
            }

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Create Filled Region");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var region = FilledRegion.Create(doc, regionType.Id, view.Id, loops);
            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                filledRegionId = ToolHelpers.GetElementIdValue(region.Id),
                typeName = regionType.Name,
                viewName = view.Name,
                holeCount = loops.Count - 1,
                warnings
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
                $"create_filled_region could not create filled region: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
