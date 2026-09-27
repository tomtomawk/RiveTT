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
/// Attaches (or detaches) the top or base of walls to a roof, floor, ceiling, toposolid or
/// wall, ONE WALL PER TRANSACTION, and names the elements Revit blames when one refuses.
///
/// Field report of 2026-09-24 (04 §2.4): attaching the walls of a block to its roof failed
/// with "cannot keep the join between the wall and the roof" for the west wall only. The
/// cause — the roof overhang cutting into the higher core walls — took seven calls to find,
/// by attaching wall by wall. This tool does exactly that, and reports for each refusal the
/// failure text and the other elements Revit named, with their category.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class AttachWallsTool : IRiveTTTool
{
    public string Name => "attach_walls";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Attaches or detaches the top or base of walls to a roof, floor, ceiling, toposolid or wall, one wall per " +
        "transaction, and names the elements Revit blames for each refusal.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var wallIds = input["wallIds"]?.ToObject<List<long>>() ?? new List<long>();
        var targetId = input["targetId"]?.Value<long>() ?? 0;
        var locationText = (input["location"]?.Value<string>() ?? "top").Trim().ToLowerInvariant();
        var action = (input["action"]?.Value<string>() ?? "attach").Trim().ToLowerInvariant();
        var mode = (input["mode"]?.Value<string>() ?? "bestEffort").Trim();
        var dryRun = ToolHelpers.GetDryRun(input);

        if (wallIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "wallIds is required (JSON array of wall ids).");
        if (targetId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "targetId is required: the roof, floor, ceiling, toposolid or wall to attach to.");
        if (locationText is not ("top" or "base"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"location '{locationText}' is not supported: top | base.");
        if (action is not ("attach" or "detach"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"action '{action}' is not supported: attach | detach.");
        var atomic = string.Equals(mode, "atomic", StringComparison.OrdinalIgnoreCase);
        if (!atomic && !string.Equals(mode, "bestEffort", StringComparison.OrdinalIgnoreCase))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"mode '{mode}' is not supported: bestEffort | atomic.");

        var location = locationText == "top" ? AttachmentLocation.Top : AttachmentLocation.Base;
        var target = doc.GetElement(ToolHelpers.ToElementId(targetId));
        if (target == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"targetId {targetId} does not exist.");
        if (action == "attach" && !Wall.IsValidTargetAttachment(doc, target.Id))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Element {targetId} ({target.Category?.Name}, {CategoryResolver.DescribeBuiltInCategory(target.Category)}) cannot be an attachment target.",
                suggestion: "Valid targets are roofs, floors, ceilings, toposolids and walls.");

        var done = new List<object>();
        var skipped = new List<object>();
        var failed = new List<object>();
        var failedWallIds = new List<long>();

        try
        {
            using var group = new TransactionGroup(doc, $"RiveTT: {action} walls");
            group.Start();

            foreach (var wallId in wallIds)
            {
                if (doc.GetElement(ToolHelpers.ToElementId(wallId)) is not Wall wall)
                {
                    skipped.Add(new { wallId, reason = "not a wall" });
                    continue;
                }

                var existing = SafeAttachmentIds(wall, location);
                if (action == "attach" && existing.Contains(targetId))
                {
                    skipped.Add(new { wallId, reason = $"already attached at its {locationText} to {targetId}" });
                    continue;
                }
                if (action == "detach" && !existing.Contains(targetId))
                {
                    skipped.Add(new { wallId, reason = $"not attached at its {locationText} to {targetId}" });
                    continue;
                }

                using var tx = new Transaction(doc, $"RiveTT: {action} wall {wallId}");
                tx.Start();
                var failures = TransactionFailureHandling.SuppressWarnings(tx);
                string? error = null;
                try
                {
                    if (action == "attach") wall.AddAttachment(target.Id, location);
                    else wall.RemoveAttachment(target.Id, location);
                    if (tx.Commit() != TransactionStatus.Committed)
                        error = TransactionFailureHandling.Describe(failures);
                }
                catch (Exception exception)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    error = exception.Message;
                }

                if (error == null)
                {
                    done.Add(new { wallId, attachedTo = SafeAttachmentIds(wall, location) });
                    continue;
                }

                // The other elements Revit named, with what they are: "the core wall 1064..."
                // is the answer the field session spent seven calls finding.
                var related = failures.FailedElementIds
                    .Where(id => id != wallId && id != targetId)
                    .Select(id => doc.GetElement(ToolHelpers.ToElementId(id)))
                    .Where(element => element != null)
                    .Select(element => new
                    {
                        id = ToolHelpers.GetElementIdValue(element!.Id),
                        category = element.Category?.Name,
                        categoryBic = CategoryResolver.DescribeBuiltInCategory(element.Category),
                        name = SafeName(element)
                    })
                    .ToList();
                failedWallIds.Add(wallId);
                failed.Add(new
                {
                    wallId,
                    error,
                    errorGroups = TransactionFailureHandling.ToContext(failures, null)["errorGroups"],
                    relatedElements = related
                });
            }

            var payload = new
            {
                action,
                location = locationText,
                target = new
                {
                    id = targetId,
                    category = target.Category?.Name,
                    categoryBic = CategoryResolver.DescribeBuiltInCategory(target.Category),
                    name = SafeName(target)
                },
                mode = atomic ? "atomic" : "bestEffort",
                doneCount = done.Count,
                failedCount = failed.Count,
                skippedCount = skipped.Count,
                done,
                failed,
                skipped,
                hint = failed.Count > 0
                    ? "A refusal usually comes from another element cutting the wall or the target (a roof overhang " +
                      "entering higher walls, a floor crossing the wall top). Fix the element in relatedElements, or " +
                      "attach the walls that do not touch it."
                    : null
            };

            if (dryRun)
            {
                if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                return ChangePreview.Probed(
                    $"DryRun: {done.Count} wall(s) would be {action}ed, {failed.Count} refused, {skipped.Count} skipped. " +
                    "Each wall was really tried in its own transaction, then everything was rolled back.",
                    payload);
            }

            if (atomic && failed.Count > 0)
            {
                if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"atomic: {failed.Count} wall(s) refused, so none was {action}ed.",
                    suggestion: "Read failed[].relatedElements, fix them, or use mode bestEffort to keep the walls that pass.",
                    context: new Dictionary<string, object>
                    {
                        ["rolledBack"] = true,
                        ["modelChanged"] = false,
                        ["failed"] = failed,
                        ["skipped"] = skipped,
                        ["failedElementIds"] = failedWallIds.ToArray()
                    });
            }

            if (group.Assimilate() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    "Revit rolled back the attachment group.",
                    suggestion: "Retry with dryRun to see which wall is refused.");

            return RiveTTResult<object>.Ok(payload);
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"attach_walls failed: {exception.Message}",
                suggestion: "Check the ids with a read tool and retry with dryRun.");
        }
    }

    private static List<long> SafeAttachmentIds(Wall wall, AttachmentLocation location)
    {
        try { return wall.GetAttachmentIds(location).Select(ToolHelpers.GetElementIdValue).ToList(); }
        catch { return new List<long>(); }
    }

    private static string? SafeName(Element element)
    {
        try { return element.Name; } catch { return null; }
    }
}
