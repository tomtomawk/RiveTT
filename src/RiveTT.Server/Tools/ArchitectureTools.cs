using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

/// <summary>
/// Small, explicit architectural surface for agents. These wrappers intentionally
/// avoid the ambiguous generic creation schemas inherited from RiveTT.
/// </summary>
[McpServerToolType]
public static class ArchitectureTools
{
    [McpServerTool(Name = "create_wall"), Description("Create one native Revit wall. wallTypeId and baseLevelId are required. Set topLevelId to constrain the wall to a level; topOffset is in mm and may be negative for partitions below a slab. ELEVATION: locationLine z values are IGNORED - baseLevelId plus baseOffset set the elevation. This differs from create_door/create_window, where locationPoint.z is an absolute project elevation unless zMode=relativeToLevel.")]
    public static async Task<string> CreateWall(
        RevitConnectionManager revit,
        [Description("Wall type element ID")] long wallTypeId,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Line JSON: {p0:{x,y,z},p1:{x,y,z},pMid?:{x,y,z}} in mm")] System.Text.Json.JsonElement locationLine,
        [Description("Top constraint level element ID. Omit for an unconnected wall")] long? topLevelId = null,
        [Description("Unconnected height in mm. Used only when topLevelId is omitted. Default: 3000")] double? height = null,
        [Description("Base offset in mm. Default: 0")] double? baseOffset = null,
        [Description("Top offset in mm. Default: 0")] double? topOffset = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!JsonObjectParam.TryParse(locationLine, out var locationLineObj))
            return JsonObjectParam.InvalidObjectResult("create_wall", "locationLine", locationLine);
        var wall = new JObject
        {
            ["category"] = "OST_Walls",
            ["typeId"] = wallTypeId,
            ["baseLevelId"] = baseLevelId,
            ["locationLine"] = locationLineObj,
            ["strictType"] = true
        };
        if (topLevelId != null) wall["topLevelId"] = topLevelId;
        if (height != null) wall["height"] = height;
        if (baseOffset != null) wall["baseOffset"] = baseOffset;
        if (topOffset != null) wall["topOffset"] = topOffset;
        return (await revit.ExecuteAsync("create_line_based_element", new JObject
        {
            ["data"] = new JArray(wall),
            ["dryRun"] = dryRun
        }, ct, publicToolName: "create_wall")).ToString();
    }

    [McpServerTool(Name = "create_door"), Description("Place a door family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z relative to levelId (z=0 = floor level), which is usually what you want. A point outside the host wall vertical range is refused with the valid range in mm, instead of Revit's 'instances do not cut anything'.")]
    public static Task<string> CreateDoor(
        RevitConnectionManager revit,
        [Description("Door family type element ID")] long typeId,
        [Description("Host wall element ID")] long hostWallId,
        [Description("Insertion point JSON {x,y,z} in mm")] System.Text.Json.JsonElement locationPoint,
        [Description("Level element ID")] long levelId,
        [Description("Flip the exterior/interior facing direction. Default false")] bool facingFlipped = false,
        [Description("Flip the door hand. Default false")] bool handFlipped = false,
        [Description("z semantics: absolute (default) | relativeToLevel")] string? zMode = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
        => CreateHostedOpening(revit, "OST_Doors", typeId, hostWallId, locationPoint, levelId,
            facingFlipped, handFlipped, zMode, dryRun, ct, publicToolName: "create_door");

    [McpServerTool(Name = "create_window"), Description("Place a window family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z as a sill height above levelId. A point outside the host wall vertical range, or a type wider than the wall, is refused with the numbers in mm.")]
    public static Task<string> CreateWindow(
        RevitConnectionManager revit,
        [Description("Window family type element ID")] long typeId,
        [Description("Host wall element ID")] long hostWallId,
        [Description("Insertion point JSON {x,y,z} in mm")] System.Text.Json.JsonElement locationPoint,
        [Description("Level element ID")] long levelId,
        [Description("Flip the exterior/interior facing direction. Default false")] bool facingFlipped = false,
        [Description("z semantics: absolute (default) | relativeToLevel")] string? zMode = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
        => CreateHostedOpening(revit, "OST_Windows", typeId, hostWallId, locationPoint, levelId,
            facingFlipped, false, zMode, dryRun, ct, publicToolName: "create_window");

    [McpServerTool(Name = "create_railing"), Description("Create a native Revit guardrail from a connected horizontal path. The path JSON is [{x,y,z}, ...] in mm.")]
    public static async Task<string> CreateRailing(
        RevitConnectionManager revit,
        [Description("Railing type element ID")] long railingTypeId,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Connected path JSON [{x,y,z}, ...] in mm")] string path,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        return (await revit.ExecuteAsync("create_railing", new JObject
        {
            ["railingTypeId"] = railingTypeId,
            ["baseLevelId"] = baseLevelId,
            ["path"] = JArray.Parse(path),
            ["dryRun"] = dryRun
        }, ct)).ToString();
    }

    [McpServerTool(Name = "set_wall_host"), Description("Revit 2027: associate a lining or façade wall with a host wall. Set hostWallId to 0 to detach it. offsetFromHost is in mm.")]
    public static async Task<string> SetWallHost(
        RevitConnectionManager revit,
        [Description("Wall to host")] long wallId,
        [Description("Host wall ID, or 0 to detach")] long hostWallId,
        [Description("Offset from host in mm. Default 0")] double? offsetFromHost = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var request = new JObject { ["wallId"] = wallId, ["hostWallId"] = hostWallId };
        if (offsetFromHost != null) request["offsetFromHost"] = offsetFromHost;
        request["dryRun"] = dryRun;
        return (await revit.ExecuteAsync("set_wall_host", request, ct)).ToString();
    }

    [McpServerTool(Name = "attach_walls"), Description("Attach (or detach) the top or base of walls to a roof, floor, ceiling, toposolid or wall, ONE WALL PER TRANSACTION. Each refusal comes back in failed[] with Revit's message and relatedElements: the other elements Revit blamed, with their category (typically a roof overhang cutting into higher walls). mode bestEffort (default) keeps the walls that pass; atomic keeps none if one fails. The dry run really tries every wall, then rolls everything back — use it to find the wall that blocks.")]
    public static async Task<string> AttachWalls(
        RevitConnectionManager revit,
        [Description("Wall ids, JSON array, e.g. [10639950, 10639951]")] System.Text.Json.JsonElement wallIds,
        [Description("Roof, floor, ceiling, toposolid or wall id to attach to")] long targetId,
        [Description("top (default) | base")] string location = "top",
        [Description("attach (default) | detach")] string action = "attach",
        [Description("bestEffort (default) | atomic")] string mode = "bestEffort",
        [Description("Preview without changing the model. Default: true — each wall is really tried, then rolled back")] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!JsonArrayParam.TryParse(wallIds, out var wallIdsArray))
            return JsonArrayParam.InvalidArrayResult("attach_walls", "wallIds", wallIds);
        var p = new JObject
        {
            ["wallIds"] = wallIdsArray,
            ["targetId"] = targetId,
            ["location"] = location,
            ["action"] = action,
            ["mode"] = mode,
            ["dryRun"] = dryRun
        };
        return (await revit.ExecuteAsync("attach_walls", p, ct)).ToString();
    }

    [McpServerTool(Name = "describe_family"), Description("Measure a loadable family type around its insertion point BEFORE placing it: placement type and the Z rule that goes with it (level-based: relative; hosted: absolute), facing direction, and extents in family coordinates (mm) measured from the INSERTION point (the point create_point_based_element takes) — visibleExtentMm (solids, the physical footprint), fullExtentMm (invisible clearances included), planViewExtentMm (with planViewId: what the plan draws) and boundingBoxMm (Revit's box, NOT a footprint). geometryOriginOffsetMm is where the family's own geometric origin sits from the insertion point (several agency families are offset by 45 mm or more). warnings say when the origin is off-centre or the box includes clearances. Pass instanceId, typeId, or familyName + typeName: a placed instance is measured when one exists, else a level-based type is placed temporarily and rolled back. Read-only.")]
    public static async Task<string> DescribeFamily(
        RevitConnectionManager revit,
        [Description("Placed family instance id to measure")] long? instanceId = null,
        [Description("Family type (FamilySymbol) id")] long? typeId = null,
        [Description("Family name, with typeName, when the ids are not known")] string? familyName = null,
        [Description("Type name within familyName")] string? typeName = null,
        [Description("Plan view id: also measure what that view draws (symbolic lines, clearance outlines)")] long? planViewId = null,
        [Description("Coarse | Medium | Fine (default)")] string? detailLevel = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (instanceId != null) p["instanceId"] = instanceId;
        if (typeId != null) p["typeId"] = typeId;
        if (familyName != null) p["familyName"] = familyName;
        if (typeName != null) p["typeName"] = typeName;
        if (planViewId != null) p["planViewId"] = planViewId;
        if (detailLevel != null) p["detailLevel"] = detailLevel;
        return (await revit.ExecuteAsync("describe_family", p, ct)).ToString();
    }

    [McpServerTool(Name = "place_in_room"), Description("Place a level-based family (bed, WC, shower, sink, sofa, bike...) IN A ROOM by its VISIBLE footprint, not its insertion point: it is placed on the room's level (elevation measured and corrected), rotated (rotationDeg, counter-clockwise; agency families face -Y at 0, +X at 90), centred on anchorMm (default: centre of the room), pushed against the walls named in against (N = +Y, S = -Y, E = +X, W = -X) — the first wall met in the footprint's own strip, so an L-shaped room works — at marginMm from the finish face (default 0: flush), and kept inside the room. footprintBasis picks what is placed: visible solids (default), what the plan draws (plan: use it when a warning says the solids cover less than half of the plan, e.g. a flush shower tray), or everything including clearances (full). The response gives the final footprint, insideRoom, tooLargeForRoom (for a clearance family: the room does not comply), the collisions with other equipment, and warns when the insertion point falls outside the room. Preview first: the dry run really places it, then rolls back.")]
    public static async Task<string> PlaceInRoom(
        RevitConnectionManager revit,
        [Description("Room element id")] long roomId,
        [Description("Family type id (or familyName + typeName)")] long? typeId = null,
        [Description("Family name, e.g. SAN_WC")] string? familyName = null,
        [Description("Type name, e.g. Suspendu PMR")] string? typeName = null,
        [Description("Rotation in degrees, counter-clockwise. Default: 0")] double rotationDeg = 0,
        [Description("Walls to push against, JSON array of N | S | E | W, e.g. [\"E\",\"S\"]")] System.Text.Json.JsonElement? against = null,
        [Description("Gap to the finish face in mm. Default: 0 (flush against the wall)")] double marginMm = 0,
        [Description("visible (default): solids visible in 3D | plan: what the level's floor plan draws | full: all geometry, clearances included")] string? footprintBasis = null,
        [Description("Target for the centre of the footprint, JSON [x, y] in mm. Default: centre of the room")] System.Text.Json.JsonElement? anchorMm = null,
        [Description("Height above the level in mm. Default: 0")] double offsetMm = 0,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["roomId"] = roomId,
            ["rotationDeg"] = rotationDeg,
            ["marginMm"] = marginMm,
            ["offsetMm"] = offsetMm,
            ["dryRun"] = dryRun
        };
        if (typeId != null) p["typeId"] = typeId;
        if (familyName != null) p["familyName"] = familyName;
        if (typeName != null) p["typeName"] = typeName;
        if (footprintBasis != null) p["footprintBasis"] = footprintBasis;
        if (JsonOptionalParam.IsProvided(against))
        {
            if (!JsonArrayParam.TryParse(against, out var againstArray))
                return JsonArrayParam.InvalidArrayResult("place_in_room", "against", against);
            p["against"] = againstArray;
        }
        if (JsonOptionalParam.IsProvided(anchorMm))
        {
            if (!JsonArrayParam.TryParse(anchorMm, out var anchorArray))
                return JsonArrayParam.InvalidArrayResult("place_in_room", "anchorMm", anchorMm);
            p["anchorMm"] = anchorArray;
        }
        return (await revit.ExecuteAsync("place_in_room", p, ct)).ToString();
    }

    private static async Task<string> CreateHostedOpening(
        RevitConnectionManager revit, string category, long typeId, long hostWallId,
        System.Text.Json.JsonElement locationPoint, long levelId, bool facingFlipped, bool handFlipped,
        string? zMode, bool dryRun, CancellationToken ct, string publicToolName)
    {
        if (!JsonObjectParam.TryParse(locationPoint, out var locationPointObj))
            return JsonObjectParam.InvalidObjectResult(publicToolName, "locationPoint", locationPoint);
        var spec = new JObject
        {
            ["category"] = category,
            ["typeId"] = typeId,
            ["hostWallId"] = hostWallId,
            ["levelId"] = levelId,
            ["locationPoint"] = locationPointObj,
            ["strictType"] = true
        };
        spec["facingFlipped"] = facingFlipped;
        spec["handFlipped"] = handFlipped;
        if (zMode != null) spec["zMode"] = zMode;
        return (await revit.ExecuteAsync("create_point_based_element", new JObject
        {
            ["data"] = new JArray(spec),
            ["dryRun"] = dryRun
        }, ct, publicToolName: publicToolName)).ToString();
    }
}
