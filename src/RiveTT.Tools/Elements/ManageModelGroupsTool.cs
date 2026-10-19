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

[ToolSafety(false, true, supportsDryRun: true)]
public sealed class ManageModelGroupsTool : IRiveTTTool
{
    public string Name => "manage_model_groups";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Inventory model groups, duplicate a group type for isolated changes, swap selected instances, ungroup selected instances, or regroup a list of elements (family documents included) under a named group type.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");
        var action = (input["action"]?.Value<string>() ?? "inventory").ToLowerInvariant();
        return action switch
        {
            "inventory" => Inventory(doc, input),
            "duplicate_type" => DuplicateType(doc, input),
            "ungroup" => Ungroup(doc, input),
            "regroup" => Regroup(doc, input),
            _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "action must be inventory, duplicate_type, ungroup, or regroup")
        };
    }

    private static RiveTTResult<object> Inventory(Document doc, JObject input)
    {
        var includeMembers = input["includeMembers"]?.Value<bool>() ?? false;
        var sampleLimit = Math.Clamp(input["sampleLimit"]?.Value<int>() ?? 20, 0, 200);
        // groupTypeId was published and ignored here: asking for one type returned
        // all twenty, and includeMembers then multiplied the response for nothing.
        var onlyTypeId = input["groupTypeId"]?.Value<long>() ?? 0;
        var types = new FilteredElementCollector(doc).OfClass(typeof(GroupType))
            .Cast<GroupType>()
            .Where(type => type.Category?.BuiltInCategory == BuiltInCategory.OST_IOSModelGroups)
            .Where(type => onlyTypeId <= 0 ||
                           ToolHelpers.GetElementIdValue(type.Id) == onlyTypeId)
            .Select(type =>
            {
                var instances = type.Groups.Cast<Group>().ToList();

                // Per-instance member counts, because instances of ONE type are
                // allowed to differ: excluding a member removes it from that instance
                // alone (Revit renames it "(membre exclu)"), and a grouped wall can be
                // taller in one instance through its own level constraints. Reading
                // only the first instance hid that, and could even report an
                // incomplete member list as if it were the definition.
                var perInstance = instances
                    .Select(instance => new
                    {
                        Instance = instance,
                        Members = instance.GetMemberIds()
                    })
                    .ToList();

                var fullest = perInstance.Count == 0
                    ? null
                    : perInstance.OrderByDescending(entry => entry.Members.Count).First();
                var fullCount = fullest?.Members.Count ?? 0;

                var instanceDetails = perInstance
                    .Select(entry => new
                    {
                        groupId = ToolHelpers.GetElementIdValue(entry.Instance.Id),
                        instanceName = entry.Instance.Name,
                        memberCount = entry.Members.Count,
                        excludedCount = Math.Max(0, fullCount - entry.Members.Count),
                        // Two independent signals: fewer members than the fullest
                        // instance, and the suffix Revit puts on the instance name.
                        hasExcludedMembers = entry.Members.Count < fullCount ||
                                             !string.Equals(entry.Instance.Name, type.Name,
                                                 StringComparison.Ordinal)
                    })
                    .ToList();

                var sample = fullest?.Instance;
                var memberIds = fullest?.Members ?? (IList<ElementId>)Array.Empty<ElementId>();
                return new
                {
                    groupTypeId = ToolHelpers.GetElementIdValue(type.Id),
                    name = type.Name,
                    instanceCount = instances.Count,
                    groupIds = instances.Select(g => ToolHelpers.GetElementIdValue(g.Id)).ToArray(),
                    // The full definition, read from the instance that has the most
                    // members — not from the first one, which may have exclusions.
                    memberCount = memberIds.Count,
                    instancesWithExclusions = instanceDetails.Count(detail => detail.hasExcludedMembers),
                    instances = instanceDetails,
                    members = includeMembers
                        ? memberIds.Take(sampleLimit).Select(id =>
                        {
                            var e = doc.GetElement(id);
                            return new { elementId = ToolHelpers.GetElementIdValue(id), category = e?.Category?.Name, name = e?.Name };
                        }).ToList()
                        : null
                };
            }).ToList();
        return RiveTTResult<object>.Ok(new
        {
            groupTypeCount = types.Count,
            groupInstanceCount = types.Sum(t => t.instanceCount),
            groupTypes = types
        });
    }

    private static RiveTTResult<object> DuplicateType(Document doc, JObject input)
    {
        var typeId = input["groupTypeId"]?.Value<long>() ?? 0;
        var name = input["newName"]?.Value<string>();
        var type = doc.GetElement(ToolHelpers.ToElementId(typeId)) as GroupType;
        if (type == null || string.IsNullOrWhiteSpace(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "A valid groupTypeId and newName are required");
        var groupIds = input["groupIds"]?.ToObject<List<long>>() ?? new List<long>();
        var dryRun = ToolHelpers.GetDryRun(input);
        if (dryRun)
            return RiveTTResult<object>.Ok(new
            {
                dryRun = true,
                sourceGroupTypeId = typeId,
                sourceName = type.Name,
                newName = name,
                groupsToSwap = groupIds
            });

        using var tx = new Transaction(doc, "RiveTT: Duplicate Model Group Type");
        tx.Start();
        var failures = TransactionFailureHandling.SuppressWarnings(tx);
        var duplicate = type.Duplicate(name!) as GroupType;
        if (duplicate == null)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                "Revit did not create the duplicated group type");
        }
        var swapped = new List<long>();
        foreach (var id in groupIds)
        {
            if (doc.GetElement(ToolHelpers.ToElementId(id)) is Group group && group.GroupType.Id == type.Id)
            {
                group.GroupType = duplicate;
                swapped.Add(id);
            }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return TransactionFailureHandling.ToFailure(failures,
                "Group type duplication was rolled back",
                "Check group consistency and retry with fewer instances.");
        return RiveTTResult<object>.Ok(new
        {
            sourceGroupTypeId = typeId,
            newGroupTypeId = ToolHelpers.GetElementIdValue(duplicate.Id),
            newName = duplicate.Name,
            swappedGroupIds = swapped
        });
    }

    private static RiveTTResult<object> Ungroup(Document doc, JObject input)
    {
        var groupIds = input["groupIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (groupIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "groupIds is required");
        var requested = groupIds.Distinct().ToList();
        var groups = requested.Select(id => doc.GetElement(ToolHelpers.ToElementId(id)) as Group)
            .Where(g => g != null).Cast<Group>().ToList();
        // A silent drop reads as "done": name what is not a group of this document.
        var groupIdSet = new HashSet<long>(groups.Select(g => ToolHelpers.GetElementIdValue(g.Id)));
        var notFoundIds = requested.Where(id => !groupIdSet.Contains(id)).ToList();
        var preview = groups.Select(group => new
        {
            groupId = ToolHelpers.GetElementIdValue(group.Id),
            groupType = group.GroupType.Name,
            attached = group.IsAttached,
            memberIds = group.GetMemberIds().Select(ToolHelpers.GetElementIdValue).ToArray()
        }).ToList();
        if (ToolHelpers.GetDryRun(input))
            return RiveTTResult<object>.Ok(new
            {
                dryRun = true,
                processed = groups.Count,
                notFoundIds,
                groups = preview.Select(g => new
                {
                    g.groupId, g.groupType, g.attached,
                    memberCount = g.memberIds.Length,
                    memberIds = g.memberIds.Take(MaxListedIds).ToArray(),
                    memberIdsTruncated = g.memberIds.Length > MaxListedIds
                }).ToList()
            });

        using var tx = new Transaction(doc, "RiveTT: Ungroup Model Groups");
        tx.Start();
        var failures = TransactionFailureHandling.SuppressWarnings(tx);
        var released = new HashSet<long>();
        var attachedCount = groups.Count(group => group.IsAttached);
        var groupsToUngroup = groups.Where(group => !group.IsAttached).ToList();
        foreach (var group in groupsToUngroup)
            foreach (var id in group.UngroupMembers()) released.Add(ToolHelpers.GetElementIdValue(id));
        if (tx.Commit() != TransactionStatus.Committed)
            return TransactionFailureHandling.ToFailure(failures,
                "Ungroup operation was rolled back",
                "Do not include attached detail groups and resolve inconsistent group types first.");
        var releasedOrdered = released.OrderBy(id => id).ToArray();
        return RiveTTResult<object>.Ok(new
        {
            processed = groups.Count,
            ungrouped = groupsToUngroup.Count,
            skippedAttached = attachedCount,
            notFoundIds,
            releasedCount = releasedOrdered.Length,
            // A 9,470-member group returned 9,470 ids: the count says it, the first ids are enough to find them.
            releasedElementIds = releasedOrdered.Take(MaxListedIds).ToArray(),
            releasedElementIdsTruncated = releasedOrdered.Length > MaxListedIds
        });
    }

    private const int MaxListedIds = 200;

    /// <summary>
    /// Groups a list of elements and gives the new group type the requested name (typically the name of the type the
    /// elements were ungrouped from). A family document goes through FamilyCreate.NewGroup: Document.Create throws
    /// there. dryRun makes the real call in a transaction that is rolled back.
    /// </summary>
    private static RiveTTResult<object> Regroup(Document doc, JObject input)
    {
        var ids = (input["elementIds"]?.ToObject<List<long>>() ?? new List<long>()).Distinct().ToList();
        var name = input["newName"]?.Value<string>();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "regroup needs elementIds (the elements to group) and newName (the group type name).",
                suggestion: "Pass elementIds as a JSON array, e.g. the releasedElementIds of a previous ungroup, and newName "
                          + "= the name of the group type they came from.",
                context: new Dictionary<string, object> { ["modelChanged"] = false });

        var notFound = ids.Where(id => doc.GetElement(ToolHelpers.ToElementId(id)) == null).ToList();
        if (notFound.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"{notFound.Count} element id(s) do not exist in '{doc.Title}'.",
                suggestion: "Ungrouping can give elements new ids: read them again before regrouping (releasedElementIds, or a "
                          + "filter_elements call).",
                context: new Dictionary<string, object>
                    { ["notFoundIds"] = notFound.Take(MaxListedIds).ToArray(), ["modelChanged"] = false });

        var dryRun = ToolHelpers.GetDryRun(input);
        var elementIds = ids.Select(ToolHelpers.ToElementId).ToList();
        using var tx = new Transaction(doc, "RiveTT: Regroup Elements");
        tx.Start();
        var failures = TransactionFailureHandling.SuppressWarnings(tx);
        try
        {
            // An empty type of that name left behind by the ungroup would refuse the rename.
            var stale = new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>()
                .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)
                                        && type.Groups.Size == 0);
            long? replacedTypeId = null;
            if (stale != null)
            {
                replacedTypeId = ToolHelpers.GetElementIdValue(stale.Id);
                doc.Delete(stale.Id);
            }

            var group = doc.IsFamilyDocument
                ? doc.FamilyCreate.NewGroup(elementIds)
                : doc.Create.NewGroup(elementIds);
            group.GroupType.Name = name!;

            var result = new
            {
                dryRun,
                previewMethod = dryRun ? "rolled_back_transaction" : null,
                regrouped = !dryRun,
                groupId = ToolHelpers.GetElementIdValue(group.Id),
                groupTypeId = ToolHelpers.GetElementIdValue(group.GroupType.Id),
                groupTypeName = group.GroupType.Name,
                memberCount = group.GetMemberIds().Count,
                requestedCount = ids.Count,
                replacedEmptyGroupTypeId = replacedTypeId,
                documentKind = doc.IsFamilyDocument ? "family" : "project"
            };
            if (dryRun)
            {
                tx.RollBack();
                return RiveTTResult<object>.Ok(result);
            }
            if (tx.Commit() != TransactionStatus.Committed)
                return TransactionFailureHandling.ToFailure(failures, "Regroup was rolled back",
                    "Check that the elements can be grouped together, then retry.");
            return RiveTTResult<object>.Ok(result);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException exception)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Revit refused to group these elements or to name the group type: {exception.Message}",
                suggestion: doc.IsFamilyDocument
                    ? "In a family document Revit's own rule decides what can be grouped (the call is FamilyCreate.NewGroup): "
                      + "the field report of 2026-10-15 got \"None of the input elements can be grouped\" for model lines, "
                      + "including freshly created ones. If so, this is a limit of the API: keep the elements ungrouped. "
                      + "If the message is about the name, a group type of that name still has instances: pick another newName."
                    : "Check that every element can be grouped (model elements, not types or views) and that no group type of "
                      + "that name still has instances.",
                context: new Dictionary<string, object>
                    { ["modelChanged"] = false, ["documentKind"] = doc.IsFamilyDocument ? "family" : "project" });
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException exception)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Revit cannot create a group here: {exception.Message}",
                suggestion: "Nothing was changed.",
                context: new Dictionary<string, object> { ["modelChanged"] = false });
        }
        finally
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
        }
    }
}
