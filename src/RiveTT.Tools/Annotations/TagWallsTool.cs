using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Tags all walls in the current view at their midpoint.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class TagWallsTool : IRiveTTTool
{
    public string Name => "tag_walls";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tags walls in the current view. Tags all walls by default, or specific ones via wallIds. Supports useLeader, tagTypeId, and orientation (horizontal/vertical).";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var useLeader = input["useLeader"]?.Value<bool>() ?? false;
        var orientationStr = input["orientation"]?.Value<string>() ?? "horizontal";
        var tagOrientation = orientationStr.Equals("vertical", StringComparison.OrdinalIgnoreCase)
            ? TagOrientation.Vertical : TagOrientation.Horizontal;
        var requestedTagTypeId = input["tagTypeId"]?.Value<long?>() ?? 0;
        var wallIds = input["wallIds"]?.ToObject<List<long>>();

        try
        {
            var view = doc.ActiveView;

            var wallsQuery = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>();

            var walls = (wallIds != null && wallIds.Count > 0)
                ? wallsQuery.Where(w => wallIds.Contains(ToolHelpers.GetElementIdValue(w.Id))).ToList()
                : wallsQuery.ToList();

            // Find wall tag type — honor requested tagTypeId first
            FamilySymbol? tagType = null;
            if (requestedTagTypeId > 0)
                tagType = doc.GetElement(ToolHelpers.ToElementId(requestedTagTypeId)) as FamilySymbol;

            tagType ??= new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_WallTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            if (tagType == null)
            {
                // Fallback to multi-category tags
                tagType = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();
            }

            if (tagType == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    "No wall tag or multi-category tag types found in project");

            int taggedCount = 0;
            var warnings = new List<string>();

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Tag Walls");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (!tagType.IsActive)
            {
                tagType.Activate();
                doc.Regenerate();
            }

            foreach (var wall in walls)
            {
                try
                {
                    var locCurve = wall.Location as LocationCurve;
                    if (locCurve == null) continue;

                    var midPoint = locCurve.Curve.Evaluate(0.5, true);
                    var reference = new Reference(wall);

                    IndependentTag.Create(doc, view.Id, reference, useLeader, TagMode.TM_ADDBY_CATEGORY,
                        tagOrientation, midPoint);
                    taggedCount++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to tag wall {ToolHelpers.GetElementIdValue(wall.Id)}: {ex.Message}");
                }
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                taggedCount,
                totalWalls = walls.Count,
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
                $"tag_walls could not tag walls: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
