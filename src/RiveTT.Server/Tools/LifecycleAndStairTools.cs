using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

/// <summary>
/// Document lifecycle, stairs and group members — the operations previously
/// declared impossible. Two of those declarations were wrong: an ExternalEvent
/// handler MAY switch the active document (it is the API event handlers that may
/// not), and a component stair is built through the non-modal StairsEditScope.
/// </summary>
[McpServerToolType]
public static class LifecycleAndStairTools
{
    [McpServerTool(Name = "create_document"), Description(
        "Create a NEW EMPTY project from a Revit template (.rte) and save it to targetPath. This is the real " +
        "'new project': save_as_document duplicates the open model with all its history, this does not. Omit " +
        "templatePath to use Revit's default project template. The file is created and saved but NOT opened " +
        "unless activate=true. Preview with dryRun (default true) to check the template, the target and the " +
        "blockers first.")]
    public static async Task<string> CreateDocument(
        RevitConnectionManager revit,
        [Description("Absolute output .rvt path")] string targetPath,
        [Description("Absolute path of the .rte template. Omit for Revit's default project template")] string? templatePath = null,
        [Description("Replace an existing target file. Default false")] bool overwrite = false,
        [Description("Open and activate the new project in Revit once saved. Default false")] bool activate = false,
        [Description("Preview without creating. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["targetPath"] = targetPath, ["dryRun"] = dryRun };
        if (templatePath != null) p["templatePath"] = templatePath;
        p["overwrite"] = overwrite;
        p["activate"] = activate;
        return (await revit.ExecuteAsync("create_document", p, ct)).ToString();
    }

    [McpServerTool(Name = "open_document"), Description(
        "Open a .rvt file and make it the ACTIVE document in Revit. Every later tool call targets that " +
        "document and all caches are flushed. Save the current document first if it has unsaved changes — " +
        "switching does not save it. Use detachFromCentral=true for a workshared central model.")]
    public static async Task<string> OpenDocument(
        RevitConnectionManager revit,
        [Description("Absolute .rvt path to open")] string filePath,
        [Description("Detach from central and preserve worksets (workshared models). Default false")] bool detachFromCentral = false,
        [Description("Preview without opening. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath, ["dryRun"] = dryRun };
        p["detachFromCentral"] = detachFromCentral;
        return (await revit.ExecuteAsync("open_document", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_stair"), Description(
        "Create a native component stair between two levels. runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] " +
        "in mm plan coordinates — the levels drive the elevation, not z. Consecutive runs get an automatic " +
        "landing. The response reports actualRiserCount against desiredRiserCount and reachesTopLevel: a run " +
        "too short produces a stair that stops below the top level. Get stairsTypeId and railingTypeId from " +
        "list_system_types (OST_Stairs, OST_StairsRailing).")]
    public static async Task<string> CreateStair(
        RevitConnectionManager revit,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Top level element ID (must be above the base level)")] long topLevelId,
        [Description("Runs JSON: [{p0:{x,y}, p1:{x,y}}, ...] in mm")] string runs,
        [Description("StairsType element ID. Omit for the document default")] long? stairsTypeId = null,
        [Description("Run width in mm. Omit for the type default")] double? widthMm = null,
        [Description("Railing type ID to place on the treads (creates one railing per side)")] long? railingTypeId = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["baseLevelId"] = baseLevelId,
            ["topLevelId"] = topLevelId,
            ["runs"] = JArray.Parse(runs),
            ["dryRun"] = dryRun
        };
        if (stairsTypeId != null) p["stairsTypeId"] = stairsTypeId;
        if (widthMm != null) p["widthMm"] = widthMm;
        if (railingTypeId != null) p["railingTypeId"] = railingTypeId;
        return (await revit.ExecuteAsync("create_stair", p, ct)).ToString();
    }

    [McpServerTool(Name = "edit_group_members"), Description(
        "Add or remove members of a model group. The Revit API cannot edit group members in place, so this " +
        "ungroups the instance, changes the member set and creates a NEW group type: the type id changes and " +
        "other instances of the original type keep the old definition. A type with several instances is " +
        "refused unless allowMultiInstance=true. Preview with dryRun.")]
    public static async Task<string> EditGroupMembers(
        RevitConnectionManager revit,
        [Description("Group INSTANCE element ID")] long groupId,
        [Description("Element IDs to add to the group. JSON array, e.g. [1,2]")] string? addElementIds = null,
        [Description("Element IDs to remove from the group. JSON array, e.g. [1,2]")] string? removeElementIds = null,
        [Description("Name for the resulting group type. Defaults to the original name")] string? newTypeName = null,
        [Description("Accept that other instances of the type keep the old definition. Default false")] bool allowMultiInstance = false,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["groupId"] = groupId, ["dryRun"] = dryRun };
        if (addElementIds != null)
        {
            if (!JsonArrayParam.TryParse(addElementIds, out var addElementIdsArray))
                return JsonArrayParam.InvalidArrayResult("edit_group_members", "addElementIds", addElementIds);
            p["addElementIds"] = addElementIdsArray;
        }
        if (removeElementIds != null)
        {
            if (!JsonArrayParam.TryParse(removeElementIds, out var removeElementIdsArray))
                return JsonArrayParam.InvalidArrayResult("edit_group_members", "removeElementIds", removeElementIds);
            p["removeElementIds"] = removeElementIdsArray;
        }
        if (newTypeName != null) p["newTypeName"] = newTypeName;
        p["allowMultiInstance"] = allowMultiInstance;
        return (await revit.ExecuteAsync("edit_group_members", p, ct)).ToString();
    }
}
