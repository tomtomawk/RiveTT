using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Deletes one or more elements from the model.
/// Defaults to dryRun=true for safety — preview what would be deleted before committing.
/// Mirrors the fork's DeleteElementEventHandler logic.
/// </summary>
[ToolSafety(false, true)]
public class DeleteElementTool : ICortexTool
{
    public string Name => "delete_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Deletes one or more elements. Defaults to dryRun=true: the preview reports the real cascade and any group membership. Deleting a MEMBER of a group whose type has several instances is refused by default — the Revit API accepts it but does not propagate, leaving that instance divergent from its siblings; pass allowGroupMemberDeletion=true to accept that, or ungroup first.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Parse inputs
        var elementIdsToken = input["elementIds"];
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (elementIdsToken == null || elementIdsToken.Type == JTokenType.Null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required",
                suggestion: "Provide an array of element ID numbers: {\"elementIds\": [123, 456], \"dryRun\": true}");

        long[] rawIds;
        try
        {
            rawIds = elementIdsToken.ToObject<long[]>() ?? Array.Empty<long>();
        }
        catch
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds must be an array of numbers");
        }

        if (rawIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds array must not be empty");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // Validate IDs and separate valid from invalid
        var validElements = new List<(ElementId Id, Element Elem)>();
        var invalidIds = new List<long>();

        foreach (var rawId in rawIds)
        {
#if REVIT2024_OR_GREATER
            var elementId = new ElementId(rawId);
#else
            var elementId = new ElementId((int)rawId);
#endif
            var elem = doc.GetElement(elementId);
            if (elem != null)
                validElements.Add((elementId, elem));
            else
                invalidIds.Add(rawId);
        }

        // Group membership: deleting a MEMBER of a grouped instance is accepted by the
        // API and does NOT propagate to the other instances of its type. Measured on a
        // real model: after deleting one member, the edited instance reported 26
        // members while its 51 siblings still reported 27, with Revit still listing
        // them as one type. The UI forces a choice through a dialog in that situation;
        // the API silently leaves the instance divergent from its type, which Autodesk
        // reports as crash-prone on large models. So it is refused unless the caller
        // accepts it explicitly.
        var allowGroupMemberDeletion = input["allowGroupMemberDeletion"]?.Value<bool>() ?? false;
        var groupMembers = new List<object>();
        var divergingIds = new List<long>();

        foreach (var (_, element) in validElements)
        {
            if (element is Group) continue;   // deleting a whole group instance is fine
            var groupId = element.GroupId;
            if (groupId == ElementId.InvalidElementId) continue;

            var group = doc.GetElement(groupId) as Group;
            var groupType = group?.GroupType;
            var instanceCount = 0;
            try { instanceCount = groupType?.Groups?.Size ?? 0; } catch { }

            groupMembers.Add(new
            {
                elementId = GetElementIdLong(element),
                name = element.Name,
                groupId = GetElementIdLongFromId(groupId),
                groupTypeName = groupType?.Name,
                instancesOfThatType = instanceCount
            });

            if (instanceCount > 1) divergingIds.Add(GetElementIdLong(element));
        }

        if (divergingIds.Count > 0 && !allowGroupMemberDeletion && !dryRun)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"{divergingIds.Count} of the requested element(s) are members of a group whose type has " +
                "several instances. Deleting them through the API does NOT propagate: the edited instance " +
                "would end up with fewer members than its siblings while Revit still reports them as the " +
                "same type.",
                suggestion: "Delete the whole group instance instead, ungroup first " +
                            "(manage_model_groups action=ungroup), edit the group in the Revit UI where the " +
                            "propagation choice is offered, or pass allowGroupMemberDeletion=true to accept " +
                            "the divergence.",
                context: new Dictionary<string, object>
                {
                    ["groupMemberIds"] = divergingIds,
                    ["groupMembers"] = groupMembers
                });

        // Build preview info for each valid element
        var validInfo = validElements.Select(ve => new
        {
            elementId = GetElementIdLong(ve.Elem),
            name      = ve.Elem.Name,
            category  = ve.Elem.Category?.Name,
            // French Revit calls the viewport category "Fenêtres " — the same label
            // as windows. The OST_ code is the only unambiguous identification.
            categoryBic = CategoryResolver.DescribeBuiltInCategory(ve.Elem.Category),
            uniqueId  = ve.Elem.UniqueId
        }).ToList();

        // DryRun — return preview without touching the model
        if (dryRun)
        {
            // Probe the real cascade with the tx-sandbox pattern: doc.Delete returns
            // every element the deletion would drag along (dependent views, tags,
            // sketches...), RollBack discards the change. Without this, previewing a
            // Level deletion reported 1 element while the real delete removed ~100.
            var dependentCount = 0;
            List<object>? dependentSample = null;
            string? cascadePreviewError = null;

            if (validElements.Count > 0)
            {
                try
                {
                    List<ElementId> wouldDeleteIds;
                    using (var probeTx = new Transaction(doc, "MCPRVTT27: Delete Preview"))
                    {
                        TransactionFailureHandling.SuppressWarnings(probeTx);
                        probeTx.Start();
                        wouldDeleteIds = doc.Delete(validElements.Select(ve => ve.Id).ToList()).ToList();
                        probeTx.RollBack();
                    }

                    // Elements are restored after RollBack, so names resolve again.
                    var requested = new HashSet<ElementId>(validElements.Select(ve => ve.Id));
                    var dependents = wouldDeleteIds.Where(id => !requested.Contains(id)).ToList();
                    dependentCount = dependents.Count;
                    dependentSample = dependents.Take(20)
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null)
                        .Select(e => (object)new
                        {
                            elementId = GetElementIdLong(e!),
                            name = e!.Name,
                            category = e.Category?.Name
                        })
                        .ToList();
                }
                catch (Exception ex)
                {
                    cascadePreviewError = ex.Message;
                }
            }

            return CortexResult<object>.Ok(new
            {
                message = cascadePreviewError == null
                    ? $"DryRun: {validElements.Count} element(s) requested; deletion would cascade to {dependentCount} dependent element(s) ({validElements.Count + dependentCount} total). Set dryRun=false to execute."
                    : $"DryRun: {validElements.Count} element(s) would be deleted (cascade preview unavailable). Set dryRun=false to execute.",
                dryRun     = true,
                wouldDelete = validInfo,
                dependentCount,
                totalWouldDelete = validElements.Count + dependentCount,
                dependentSample,
                cascadePreviewError,
                invalidIds,
                validCount  = validElements.Count,
                invalidCount = invalidIds.Count,
                groupMembers,
                groupDivergenceIds = divergingIds,
                warnings = divergingIds.Count == 0
                    ? Array.Empty<string>()
                    : new[]
                    {
                        $"{divergingIds.Count} element(s) belong to a group type with several instances. " +
                        "Deleting them does not propagate — the edited instance would diverge from its " +
                        "siblings. The real call refuses this unless allowGroupMemberDeletion=true."
                    }
            });
        }

        // Actual deletion
        if (validElements.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No valid elements to delete",
                context: invalidIds.Count > 0
                    ? new Dictionary<string, object> { ["invalidIds"] = invalidIds }
                    : null);

        if (!session.RequestConfirmation("delete", validElements.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        try
        {
            ICollection<ElementId> deletedIds;
            var cascadeInfo = new List<object>();
            using var tx = new Transaction(doc, "MCPRVTT27: Delete Elements");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            try
            {
                // Probe the cascade first, inside a rolled-back sub-transaction, so the
                // implicitly removed elements (tags, sketches, dependent views) can be
                // named while they still exist. deletedCount used to report 2 while
                // deletedElements listed 1, with no way to know what the extra was.
                var requestedIds = validElements.Select(ve => ve.Id).ToList();
                try
                {
                    using var probe = new SubTransaction(doc);
                    probe.Start();
                    var probed = doc.Delete(requestedIds);
                    probe.RollBack();

                    var requestedSet = new HashSet<ElementId>(requestedIds);
                    cascadeInfo = probed
                        .Where(id => !requestedSet.Contains(id))
                        .Select(id => doc.GetElement(id))
                        .Where(element => element != null)
                        .Select(element => (object)new
                        {
                            elementId = GetElementIdLong(element!),
                            name = element!.Name,
                            category = element.Category?.Name,
                            categoryBic = CategoryResolver.DescribeBuiltInCategory(element.Category),
                            reason = "removed as a dependency of a requested element"
                        })
                        .ToList();
                }
                catch
                {
                    // A probe failure must not block the delete the caller asked for.
                }

                deletedIds = doc.Delete(requestedIds);
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the deletion: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            var deletedElementIds = deletedIds.Select(GetElementIdLongFromId).ToList();

            return CortexResult<object>.Ok(new
            {
                message      = cascadeInfo.Count == 0
                    ? $"Deleted {deletedIds.Count} element(s) successfully."
                    : $"Deleted {deletedIds.Count} element(s): {validInfo.Count} requested plus " +
                      $"{cascadeInfo.Count} dependent element(s).",
                dryRun       = false,
                deletedCount = deletedIds.Count,
                // deletedCount == requestedElements.Count + cascadedElements.Count,
                // and deletedElementIds is the full list Revit actually removed.
                deletedElementIds,
                requestedElements = validInfo,
                cascadedElements = cascadeInfo,
                cascadedCount = cascadeInfo.Count,
                invalidIds,
                invalidCount = invalidIds.Count,
                groupMembers,
                warnings = divergingIds.Count == 0
                    ? Array.Empty<string>()
                    : new[]
                    {
                        $"{divergingIds.Count} deleted element(s) were members of a multi-instance group " +
                        "type: those instances now have fewer members than their siblings while Revit still " +
                        "reports one type. Re-sync them by editing the group once in the Revit UI."
                    }
            });
        }
        catch (Exception ex)
        {
            // Document.Delete throws a bare ArgumentException ("One or more of the
            // elementIds cannot be deleted") with no indication of which element or
            // why. Name the likely cause so the caller is not left guessing.
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to delete elements: {ex.Message}",
                suggestion: ex is ArgumentException
                    ? "Revit refuses this deletion. Common causes: the element is the last sheet/view of its " +
                      "kind, it is pinned, it is referenced by another element (a viewport, a dimension, a " +
                      "group), or the document needs a regeneration after a previous delete. Delete the " +
                      "referencing elements first, or re-read the element to confirm it still exists."
                    : null,
                context: new Dictionary<string, object>
                {
                    ["requestedIds"] = validElements.Select(ve => GetElementIdLong(ve.Elem)).ToList(),
                    ["exceptionType"] = ex.GetType().Name
                });
        }
    }

    private static long GetElementIdLongFromId(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private static long GetElementIdLong(Element elem)
    {
#if REVIT2024_OR_GREATER
        return elem.Id.Value;
#else
        return elem.Id.IntegerValue;
#endif
    }
}
