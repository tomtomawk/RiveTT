using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class StoreyAndGroupTools
{
    [McpServerTool(Name = "duplicate_storey"), Description("Preview or transactionally duplicate model elements from one level to a target elevation. Reports view-specific, grouped, and constrained dependencies before writing; can optionally shift upper levels.")]
    public static async Task<string> DuplicateStorey(
        RevitConnectionManager revit,
        [Description("Source level element ID")] long? sourceLevelId = null,
        [Description("Source level name when sourceLevelId is omitted")] string? sourceLevelName = null,
        [Description("Target level elevation in mm")] double? targetElevationMm = null,
        [Description("Target level name; default '<source> Copy'")] string? targetLevelName = null,
        [Description("Optional target top level ID for copied walls")] long? targetTopLevelId = null,
        [Description("Optional amount in mm to shift levels at/above the target elevation")] double? moveUpperLevelsByMm = null,
        [Description("Categories to copy, OST_* or localized display names; omit for all model categories. JSON array, e.g. [\"A\",\"B\"]")] string? categories = null,
        [Description("Copy each source model group as one group instance. Default: true")] bool copyGroups = true,
        [Description("Include element samples/IDs. Default: false")] bool includeDetails = false,
        [Description("Maximum detail rows. Default: 50")] int? sampleLimit = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        [Description("suppress_all (default) or allow_list; unapproved warnings roll back")] string? warningPolicy = null,
        [Description("FailureDefinition GUIDs allowed when warningPolicy=allow_list. JSON array, e.g. [\"A\",\"B\"]")] string? allowedWarningIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (sourceLevelId != null) p["sourceLevelId"] = sourceLevelId;
        if (sourceLevelName != null) p["sourceLevelName"] = sourceLevelName;
        if (targetElevationMm != null) p["targetElevationMm"] = targetElevationMm;
        if (targetLevelName != null) p["targetLevelName"] = targetLevelName;
        if (targetTopLevelId != null) p["targetTopLevelId"] = targetTopLevelId;
        if (moveUpperLevelsByMm != null) p["moveUpperLevelsByMm"] = moveUpperLevelsByMm;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("duplicate_storey", "categories", categories);
            p["categories"] = categoriesArray;
        }
        p["copyGroups"] = copyGroups;
        p["includeDetails"] = includeDetails;
        if (sampleLimit != null) p["sampleLimit"] = sampleLimit;
        if (warningPolicy != null) p["warningPolicy"] = warningPolicy;
        if (allowedWarningIds != null)
        {
            if (!JsonArrayParam.TryParse(allowedWarningIds, out var allowedWarningIdsArray))
                return JsonArrayParam.InvalidArrayResult("duplicate_storey", "allowedWarningIds", allowedWarningIds);
            p["allowedWarningIds"] = allowedWarningIdsArray;
        }
        return (await revit.ExecuteAsync("duplicate_storey", p, 600, ct)).ToString();
    }

    [McpServerTool(Name = "detach_wall_constraint"), Description("Preview or detach wall top-level constraints or Revit 2027 top/base attachments. Grouped walls are reported and skipped instead of rolling back unrelated walls.")]
    public static async Task<string> DetachWallConstraint(
        RevitConnectionManager revit,
        [Description("Wall element IDs")] long[] wallIds,
        [Description("level_top | attachment_top | attachment_base | all_attachments")] string mode = "level_top",
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        [Description("suppress_all (default) or allow_list; unapproved warnings roll back")] string? warningPolicy = null,
        [Description("FailureDefinition GUIDs allowed when warningPolicy=allow_list. JSON array, e.g. [\"A\",\"B\"]")] string? allowedWarningIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["wallIds"] = new JArray(wallIds.Cast<object>().ToArray()),
            ["mode"] = mode,
            ["dryRun"] = dryRun
        };
        if (warningPolicy != null) p["warningPolicy"] = warningPolicy;
        if (allowedWarningIds != null)
        {
            if (!JsonArrayParam.TryParse(allowedWarningIds, out var allowedWarningIdsArray))
                return JsonArrayParam.InvalidArrayResult("detach_wall_constraint", "allowedWarningIds", allowedWarningIds);
            p["allowedWarningIds"] = allowedWarningIdsArray;
        }
        return (await revit.ExecuteAsync("detach_wall_constraint", p, ct)).ToString();
    }

    [McpServerTool(Name = "manage_model_groups"), Description("Inventory model groups, duplicate a group type and optionally swap selected instances, or ungroup selected model groups. Write actions preview by default.")]
    public static async Task<string> ManageModelGroups(
        RevitConnectionManager revit,
        [Description("inventory | duplicate_type | ungroup")] string action = "inventory",
        [Description("Group type ID for duplicate_type")] long? groupTypeId = null,
        [Description("New group type name for duplicate_type")] string? newName = null,
        [Description("Group instance IDs to swap or ungroup. JSON array, e.g. [1,2]")] string? groupIds = null,
        [Description("Include member samples for inventory. Default: false")] bool includeMembers = false,
        [Description("Member sample limit. Default: 20")] int? sampleLimit = null,
        [Description("Preview write actions. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action, ["dryRun"] = dryRun };
        if (groupTypeId != null) p["groupTypeId"] = groupTypeId;
        if (newName != null) p["newName"] = newName;
        if (groupIds != null)
        {
            if (!JsonArrayParam.TryParse(groupIds, out var groupIdsArray))
                return JsonArrayParam.InvalidArrayResult("manage_model_groups", "groupIds", groupIds);
            p["groupIds"] = groupIdsArray;
        }
        p["includeMembers"] = includeMembers;
        if (sampleLimit != null) p["sampleLimit"] = sampleLimit;
        return (await revit.ExecuteAsync("manage_model_groups", p, ct)).ToString();
    }
}
