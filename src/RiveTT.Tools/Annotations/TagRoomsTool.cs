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
/// Tags all or specified rooms in a view, with a chosen tag type.
///
/// The tool used to look up a room tag type, never use it, and let NewRoomTag place the
/// default one; the dwelling-typology tags of the 2026-09-24 session (one ETQ_Pièce typo tag,
/// on the living room of each dwelling) had to be scripted. tagTypeId/tagTypeName now choose
/// the type, roomNameContains and onePerParameter choose the rooms, and every room left
/// untagged comes back with its reason.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class TagRoomsTool : IRiveTTTool
{
    public string Name => "tag_rooms";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Tags all or specified rooms in a view with the chosen room tag type; can keep one room per value of a parameter (one tag per dwelling).";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var useLeader = input["useLeader"]?.Value<bool>() ?? false;
        var roomIds = input["roomIds"]?.ToObject<List<long>>();
        var tagTypeId = input["tagTypeId"]?.Value<long>() ?? 0;
        var tagTypeName = input["tagTypeName"]?.Value<string>();
        var roomNameContains = input["roomNameContains"]?.Value<string>();
        var onePerParameter = input["onePerParameter"]?.Value<string>();

        try
        {
            // Explicit viewId or the current view selected with activate_view.
            var view = ToolHelpers.ResolveTargetView(doc, input, out var viewError);
            if (view == null) return viewError!;

            var tagTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            FamilySymbol? tagType = null;
            if (tagTypeId > 0)
            {
                tagType = tagTypes.FirstOrDefault(t => ToolHelpers.GetElementIdValue(t.Id) == tagTypeId);
                if (tagType == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        $"tagTypeId {tagTypeId} is not a room tag type.",
                        suggestion: "Room tag types: " + DescribeTypes(tagTypes));
            }
            else if (!string.IsNullOrWhiteSpace(tagTypeName))
            {
                tagType = ResolveTagType(tagTypes, tagTypeName!);
                if (tagType == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        $"No room tag type matches '{tagTypeName}'.",
                        suggestion: "Pass \"Family : Type\" or the type name. Room tag types: " + DescribeTypes(tagTypes));
            }

            // Candidate rooms
            var notFoundIds = new List<long>();
            List<Room> rooms;
            if (roomIds != null && roomIds.Count > 0)
            {
                rooms = new List<Room>();
                foreach (var id in roomIds)
                {
                    if (doc.GetElement(ToolHelpers.ToElementId(id)) is Room room) rooms.Add(room);
                    else notFoundIds.Add(id);
                }
            }
            else
            {
                rooms = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
            }

            var skipped = new List<object>();
            if (!string.IsNullOrWhiteSpace(roomNameContains))
            {
                var wanted = NameMatching.Normalize(roomNameContains);
                var kept = new List<Room>();
                foreach (var room in rooms)
                {
                    var roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    if (NameMatching.Normalize(roomName).Contains(wanted)) kept.Add(room);
                }
                rooms = kept;
            }

            var unresolvedParameterNames = new List<object>();
            if (!string.IsNullOrWhiteSpace(onePerParameter))
            {
                rooms = KeepOnePerValue(doc, rooms, onePerParameter!, skipped, unresolvedParameterNames);
            }

            // Rooms already carrying a tag in this view. With an explicit type, only a tag of
            // THAT type counts: a typology tag goes next to the name tag, not instead of it.
            var alreadyTagged = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .OfType<RoomTag>()
                .Where(rt => tagType == null || rt.GetTypeId() == tagType.Id)
                .Select(rt => ToolHelpers.GetElementIdValue(rt.Room?.Id ?? ElementId.InvalidElementId))
                .ToHashSet();

            int taggedCount = 0;
            var createdTagIds = new List<long>();
            var warnings = new List<string>();

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Tag Rooms");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

            foreach (var room in rooms)
            {
                var roomId = ToolHelpers.GetElementIdValue(room.Id);
                if (alreadyTagged.Contains(roomId))
                {
                    skipped.Add(new { roomId, reason = tagType == null ? "already tagged in this view" : "already has a tag of this type in this view" });
                    continue;
                }

                try
                {
                    if (room.Location is not LocationPoint loc)
                    {
                        skipped.Add(new { roomId, reason = "room is not placed (no location point)" });
                        continue;
                    }

                    var point = loc.Point;
                    var uv = new UV(point.X, point.Y);
                    var tag = doc.Create.NewRoomTag(new LinkElementId(room.Id), uv, view.Id);
                    if (tag != null)
                    {
                        // NewRoomTag always places the default type; the chosen one is applied after.
                        if (tagType != null && tag.GetTypeId() != tagType.Id)
                            tag.ChangeTypeId(tagType.Id);
                        tag.HasLeader = useLeader;
                        doc.Regenerate();
                        warnings.AddRange(ViewCropDiagnostics.Inspect(view, tag));
                        createdTagIds.Add(ToolHelpers.GetElementIdValue(tag.Id));
                        taggedCount++;
                    }
                    else
                    {
                        skipped.Add(new { roomId, reason = "Revit returned no tag" });
                    }
                }
                catch (Exception ex)
                {
                    skipped.Add(new { roomId, reason = ex.Message });
                    warnings.Add($"Failed to tag room {room.Name}: {ex.Message}");
                }
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new
            {
                taggedCount,
                skippedCount = skipped.Count,
                createdTagIds,
                tagType = tagType == null
                    ? null
                    : new { id = ToolHelpers.GetElementIdValue(tagType.Id), familyName = tagType.FamilyName, typeName = tagType.Name },
                tagTypeApplied = tagType == null ? "view default (NewRoomTag)" : "requested type",
                skipped,
                notFoundIds,
                unresolvedParameterNames,
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
                    suggestion: "Fix the reported model errors and retry.",
                    context: TransactionFailureHandling.ToContext(txFailures, null));

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

    /// <summary>
    /// One room per distinct value of <paramref name="parameterName"/> — the largest one — so
    /// a dwelling gets one typology tag, not one per room.
    /// </summary>
    private static List<Room> KeepOnePerValue(Document doc, List<Room> rooms, string parameterName,
        List<object> skipped, List<object> unresolved)
    {
        var byValue = new Dictionary<string, List<Room>>(StringComparer.Ordinal);
        var resolvedOnAny = false;
        foreach (var room in rooms)
        {
            var parameter = ParameterNameResolver.Resolve(room, parameterName, doc);
            if (parameter == null)
            {
                skipped.Add(new { roomId = ToolHelpers.GetElementIdValue(room.Id), reason = $"no parameter '{parameterName}'" });
                continue;
            }
            resolvedOnAny = true;
            var value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
            if (string.IsNullOrWhiteSpace(value))
            {
                skipped.Add(new { roomId = ToolHelpers.GetElementIdValue(room.Id), reason = $"'{parameterName}' is empty" });
                continue;
            }
            if (!byValue.TryGetValue(value!, out var list)) byValue[value!] = list = new List<Room>();
            list.Add(room);
        }

        if (!resolvedOnAny && rooms.Count > 0)
        {
            unresolved.Add(new
            {
                name = parameterName,
                suggestions = ParameterNameResolver.Suggest(parameterName, ParameterNameResolver.AvailableNames(rooms[0], doc))
            });
        }

        var kept = new List<Room>();
        foreach (var group in byValue.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = group.Value.OrderByDescending(r => r.Area).ToList();
            kept.Add(ordered[0]);
            foreach (var other in ordered.Skip(1))
                skipped.Add(new
                {
                    roomId = ToolHelpers.GetElementIdValue(other.Id),
                    reason = $"'{parameterName}' = '{group.Key}' is already tagged on its largest room {ToolHelpers.GetElementIdValue(ordered[0].Id)}"
                });
        }
        return kept;
    }

    private static FamilySymbol? ResolveTagType(List<FamilySymbol> types, string requested)
    {
        var parts = requested.Split(new[] { ':' }, 2);
        if (parts.Length == 2)
        {
            var family = parts[0].Trim();
            var type = parts[1].Trim();
            var exact = types.FirstOrDefault(t => string.Equals(t.FamilyName, family, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        var byType = types.Where(t => string.Equals(t.Name, requested.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (byType.Count == 1) return byType[0];
        var byFamily = types.Where(t => string.Equals(t.FamilyName, requested.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return byFamily.Count == 1 ? byFamily[0] : null;
    }

    private static string DescribeTypes(IEnumerable<FamilySymbol> types) =>
        string.Join(", ", types.Take(30).Select(t => $"{ToolHelpers.GetElementIdValue(t.Id)} = '{t.FamilyName} : {t.Name}'"));
}
