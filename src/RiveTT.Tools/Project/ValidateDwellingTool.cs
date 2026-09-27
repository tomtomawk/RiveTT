using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Design;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Project;

/// <summary>
/// Checks the dwellings of a level, as BUILT in the model, against the agency's dwelling rules
/// (RiveTT.Core.Design). A dwelling is the set of rooms of one level sharing a value of the
/// dwelling parameter (ARC_PAR_NUMERO_LOGEMENT by default).
///
/// Field report of 2026-09-24: the charter's rules (open entrance, ETEL, Ø 1.50 m circles,
/// corridor widths) were in a document the agent read once. They are now a check, the same
/// one validate_spec runs on a design before anything is built. includeSpec returns the
/// dwelling as JSON in the design schema: the way to turn a delivered, validated dwelling into
/// a reference plan (docs/retex/2026-09-24/08, extract_spec).
/// </summary>
[ToolSafety(true, false)]
public sealed class ValidateDwellingTool : IRiveTTTool, ICommandTimeoutTool
{
    private static readonly BuiltInCategory[] EquipmentCategories =
    {
        BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems, BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Casework, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_DataDevices
    };

    public string Name => "validate_dwelling";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public int CommandTimeoutSeconds => 180;
    public string Description =>
        "Checks the dwellings of a level, as built, against the agency's dwelling rules (open entrance, ETEL, " +
        "Ø 1.50 m circles, corridor widths, surfaces...) and reports rule by rule; includeSpec returns each dwelling " +
        "as a design JSON.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var dwellingParameter = input["dwellingParameter"]?.Value<string>() ?? "ARC_PAR_NUMERO_LOGEMENT";
        var typologyParameter = input["typologyParameter"]?.Value<string>() ?? "ARC_PAR_TYPOLOGIE";
        var wanted = input["dwelling"]?.Value<string>();
        var includeSpec = input["includeSpec"]?.Value<bool>() ?? false;
        var targetAreaM2 = input["targetAreaM2"]?.Value<double?>();

        var level = ResolveLevel(doc, input, out var levelError);
        if (level == null) return levelError!;
        if (targetAreaM2 != null && wanted == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "targetAreaM2 applies to one dwelling: pass dwelling with it.",
                suggestion: "A programme area for a whole level would compare every dwelling to the same figure.");

        var ruleErrors = new List<string>();
        var ruleOverrides = input["rules"] as JObject;
        if (input["rules"] != null && input["rules"]!.Type != JTokenType.Null && ruleOverrides == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "rules must be a JSON object of rule overrides.");
        // Validated once up front: an unknown rule id must fail the call, not every dwelling.
        DwellingRuleSet.CreateDefault().ApplyOverrides(ruleOverrides, ruleErrors);
        if (ruleErrors.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, string.Join(" ", ruleErrors),
                suggestion: "Rule ids and values: call validate_spec with listRules=true.");

        try
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.LevelId == level.Id)
                .ToList();

            var unplaced = rooms.Where(r => r.Area <= 0).Select(r => ToolHelpers.GetElementIdValue(r.Id)).ToList();
            var placed = rooms.Where(r => r.Area > 0).ToList();

            var byDwelling = new Dictionary<string, List<Room>>(StringComparer.Ordinal);
            var withoutDwelling = new List<long>();
            var parameterFound = false;
            foreach (var room in placed)
            {
                var parameter = ParameterNameResolver.Resolve(room, dwellingParameter, doc);
                if (parameter != null) parameterFound = true;
                var value = parameter == null ? null
                    : parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
                if (string.IsNullOrWhiteSpace(value)) { withoutDwelling.Add(ToolHelpers.GetElementIdValue(room.Id)); continue; }
                if (!byDwelling.TryGetValue(value!, out var list)) byDwelling[value!] = list = new List<Room>();
                list.Add(room);
            }

            if (!parameterFound && placed.Count > 0)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"No room of '{level.Name}' has a parameter '{dwellingParameter}'.",
                    suggestion: "Pass dwellingParameter with the name of the parameter that numbers the dwellings.",
                    context: new Dictionary<string, object>
                    {
                        ["unresolvedParameterNames"] = new[]
                        {
                            new
                            {
                                name = dwellingParameter,
                                suggestions = ParameterNameResolver.Suggest(dwellingParameter,
                                    ParameterNameResolver.AvailableNames(placed[0], doc))
                            }
                        }
                    });

            if (wanted != null && !byDwelling.ContainsKey(wanted))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No dwelling '{wanted}' on '{level.Name}'.",
                    suggestion: byDwelling.Count == 0
                        ? $"No room of this level has a value for {dwellingParameter}."
                        : "Dwellings on this level: " + string.Join(", ", byDwelling.Keys.OrderBy(k => k)));

            var doors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            var equipment = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(EquipmentCategories.ToList()))
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Where(fi => fi.LevelId == level.Id || (fi.Host as Element)?.LevelId == level.Id)
                .ToList();

            var reports = new JArray();
            var specs = new JArray();
            foreach (var (dwellingId, dwellingRooms) in byDwelling.OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                if (wanted != null && dwellingId != wanted) continue;
                var spec = Extract(doc, level, dwellingId, dwellingRooms, typologyParameter, doors, equipment);
                if (targetAreaM2 != null && wanted != null) spec.TargetAreaM2 = targetAreaM2;

                var rules = DwellingRuleSet.CreateDefault();
                rules.ApplyOverrides(ruleOverrides, new List<string>());
                var report = DwellingValidator.Validate(spec, rules);
                reports.Add(report);
                if (includeSpec) specs.Add(ToJson(spec));
            }

            var compliant = reports.Count(r => r["compliant"]?.Type == JTokenType.Boolean && r["compliant"]!.Value<bool>());
            var nonCompliant = reports.Count(r => r["compliant"]?.Type == JTokenType.Boolean && !r["compliant"]!.Value<bool>());
            return RiveTTResult<object>.Ok(new
            {
                message = $"{reports.Count} dwelling(s) of '{level.Name}' checked: {compliant} compliant, " +
                          $"{nonCompliant} not, {reports.Count - compliant - nonCompliant} undecided.",
                level = level.Name,
                levelId = ToolHelpers.GetElementIdValue(level.Id),
                dwellingParameter,
                typologyParameter,
                summary = new { dwellingCount = reports.Count, compliant, nonCompliant, undecided = reports.Count - compliant - nonCompliant },
                dwellings = reports,
                specs = includeSpec ? specs : null,
                roomsWithoutDwelling = withoutDwelling,
                unplacedOrUnenclosedRoomIds = unplaced,
                notes = new[]
                {
                    "Room outlines are read at the finish face; rooms separated by a wall do not touch, rooms joined " +
                    "by a room separation line do — which is how an open entrance is recognised.",
                    "Equipment footprints are the visible solids of each family (never its bounding box).",
                    "Level-wide rules (lift, common corridors, landing shafts, balconies) are not part of this check."
                }
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"validate_dwelling could not read the dwellings: {exception.Message}",
                suggestion: "Check that the rooms of the level are placed and enclosed (export_room_data).");
        }
    }

    private static Level? ResolveLevel(Document doc, JObject input, out RiveTTResult<object>? error)
    {
        error = null;
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        var levelId = input["levelId"]?.Value<long>() ?? 0;
        if (levelId > 0)
        {
            var byId = levels.FirstOrDefault(l => ToolHelpers.GetElementIdValue(l.Id) == levelId);
            if (byId != null) return byId;
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"levelId {levelId} is not a level.");
            return null;
        }
        var levelName = input["level"]?.Value<string>() ?? input["levelName"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(levelName))
        {
            var byName = levels.FirstOrDefault(l => l.Name == levelName)
                ?? levels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
            string.IsNullOrWhiteSpace(levelName) ? "level (name) or levelId is required." : $"No level named '{levelName}'.",
            suggestion: "Levels: " + string.Join(", ", levels.OrderBy(l => l.Elevation).Select(l => $"'{l.Name}'")));
        return null;
    }

    /// <summary>The dwelling as the design schema describes it, read from the model.</summary>
    private static DwellingSpec Extract(Document doc, Level level, string dwellingId, List<Room> rooms,
        string typologyParameter, List<FamilyInstance> doors, List<FamilyInstance> equipment)
    {
        var spec = new DwellingSpec { Id = dwellingId, Level = level.Name, Source = "revit" };
        foreach (var room in rooms)
        {
            var typology = ParameterNameResolver.Resolve(room, typologyParameter, doc);
            var value = typology?.StorageType == StorageType.String ? typology.AsString() : typology?.AsValueString();
            if (spec.Typology == null && !string.IsNullOrWhiteSpace(value)) spec.Typology = value;

            var (polygon, holes) = RoomOutline.LoopsMm(room);
            if (polygon.Count < 3) continue;
            var name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name;
            spec.Rooms.Add(new SpecRoom
            {
                Name = name,
                Kind = DwellingSpec.Classify(name),
                Polygon = polygon,
                Holes = holes,
                ElementId = ToolHelpers.GetElementIdValue(room.Id)
            });
        }

        var roomIds = rooms.Select(r => r.Id).ToHashSet();
        bool Near(Pt p) => spec.Rooms.Any(r => PlanGeometry.DistanceTo(r.Polygon, p) <= 500);

        foreach (var door in doors)
        {
            if (door.Location is not LocationPoint location) continue;
            var from = SafeRoom(() => door.FromRoom);
            var to = SafeRoom(() => door.ToRoom);
            var at = new Pt(location.Point.X * MmPerFoot, location.Point.Y * MmPerFoot);
            var touches = (from != null && roomIds.Contains(from.Id)) || (to != null && roomIds.Contains(to.Id));
            if (!touches && !(door.LevelId == level.Id && Near(at))) continue;
            spec.Doors.Add(new SpecDoor
            {
                Family = door.Symbol?.FamilyName ?? "",
                Type = door.Symbol?.Name ?? "",
                At = at,
                Rooms = new[] { from, to }.Where(r => r != null)
                    .Select(r => r!.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? r.Name).ToList(),
                ElementId = ToolHelpers.GetElementIdValue(door.Id)
            });
        }

        foreach (var item in equipment)
        {
            if (item.Location is not LocationPoint location) continue;
            var at = new Pt(location.Point.X * MmPerFoot, location.Point.Y * MmPerFoot);
            if (!Near(at)) continue;
            var footprint = FamilyExtents.PlanFootprintMm(item)?.Select(p => new Pt(p.X, p.Y)).ToList();
            spec.Equipment.Add(new SpecEquipment
            {
                Family = item.Symbol?.FamilyName ?? "",
                Type = item.Symbol?.Name ?? "",
                At = at,
                Footprint = footprint,
                ElementId = ToolHelpers.GetElementIdValue(item.Id)
            });
        }
        return spec;
    }

    private static Room? SafeRoom(Func<Room?> read)
    {
        try { return read(); } catch { return null; }
    }

    private static JObject ToJson(DwellingSpec spec)
    {
        static JArray Point(Pt p) => new(Math.Round(p.X, 1), Math.Round(p.Y, 1));
        return new JObject
        {
            ["id"] = spec.Id,
            ["level"] = spec.Level,
            ["typology"] = spec.Typology,
            ["rooms"] = new JArray(spec.Rooms.Select(r => new JObject
            {
                ["name"] = r.Name,
                ["kind"] = r.Kind.ToString(),
                ["elementId"] = r.ElementId,
                ["polygon"] = new JArray(r.Polygon.Select(Point)),
                ["holes"] = r.Holes.Count == 0 ? null : new JArray(r.Holes.Select(h => new JArray(h.Select(Point))))
            })),
            ["doors"] = new JArray(spec.Doors.Select(d => new JObject
            {
                ["family"] = d.Family, ["type"] = d.Type, ["at"] = Point(d.At),
                ["rooms"] = new JArray(d.Rooms), ["elementId"] = d.ElementId
            })),
            ["equipment"] = new JArray(spec.Equipment.Select(e => new JObject
            {
                ["family"] = e.Family, ["type"] = e.Type, ["at"] = Point(e.At), ["elementId"] = e.ElementId,
                ["footprint"] = e.Footprint == null ? null : new JArray(e.Footprint.Select(Point))
            }))
        };
    }
}
