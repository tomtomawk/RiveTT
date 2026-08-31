using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Tags all or specified rooms in the current view.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class TagRoomsTool : IRiveTTTool
{
    public string Name => "tag_rooms";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tags all or specified rooms in the current view.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var useLeader = input["useLeader"]?.Value<bool>() ?? false;
        var roomIds = input["roomIds"]?.ToObject<List<long>>();

        try
        {
            // viewId when given, active view otherwise: nothing in the MCP surface can
            // activate a view, so tagging used to require a human to switch tabs first.
            var view = ToolHelpers.ResolveTargetView(doc, input, out var viewError);
            if (view == null) return viewError!;

            // Get rooms
            IEnumerable<Room> rooms;
            if (roomIds != null && roomIds.Count > 0)
            {
                rooms = roomIds.Select(id =>
                {
                    return doc.GetElement(new ElementId(id)) as Room;
                }).Where(r => r != null)!;
            }
            else
            {
                rooms = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0);
            }

            // Find room tag type
            var tagType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            // Get existing tagged rooms to avoid duplicates
            var alreadyTagged = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<RoomTag>()
                .Select(rt => ToolHelpers.GetElementIdValue(rt.Room?.Id ?? ElementId.InvalidElementId))
                .ToHashSet();

            int taggedCount = 0;
            int skippedCount = 0;
            var warnings = new List<string>();

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Tag Rooms");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var room in rooms)
            {
                if (alreadyTagged.Contains(ToolHelpers.GetElementIdValue(room.Id)))
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    var loc = room.Location as LocationPoint;
                    if (loc == null) continue;

                    var point = loc.Point;
                    var uv = new UV(point.X, point.Y);
                    var tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), uv, view.Id);
                    if (tag != null)
                    {
                        tag.HasLeader = useLeader;
                        taggedCount++;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to tag room {room.Name}: {ex.Message}");
                }
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                taggedCount,
                skippedCount,
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
                $"tag_rooms could not tag rooms: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
