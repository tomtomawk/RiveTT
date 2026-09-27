using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Design;

namespace RiveTT.Server.Tools;

/// <summary>
/// Design before construction (docs/retex/2026-09-24/08, option B): the agent describes the
/// dwellings as JSON, validate_spec checks them against the agency's rules WITHOUT Revit, and
/// only a validated design is built. In the field session, three complete versions of the
/// plans were modelled before the charter's rules were applied; each correction cost a
/// rebuild. A JSON correction costs nothing.
///
/// Answered by the server alone: no pipe, no Revit, no write lock involved.
/// </summary>
[McpServerToolType]
public static class DesignTools
{
    [McpServerTool(Name = "validate_spec"), Description(
        "Check a dwelling DESIGN (JSON, before modelling anything) against the agency's dwelling rules: entrance open " +
        "onto the living room, 1.20 x 2.20 m entrance clearance, ETEL in the entrance, Ø 1.50 m free circles in the " +
        "shower room and living room (equipment footprints deducted), 3.20 x 3.10 m bedroom, corridor ≥ 0.90 m (aim " +
        "1.00), circulation ratio ≤ 12 %, separate WC from T3, surfaces, typology. Needs no Revit and works while RiveTT " +
        "is locked. spec: one dwelling {id, level, typology, targetAreaM2?, rooms:[{name, polygon:[[x,y],...]}], " +
        "partitions?:[{type, p0:[x,y], p1:[x,y], thicknessMm?}], doors?:[{family, type, at:[x,y], rooms?:[names]}], " +
        "equipment?:[{family, type, at:[x,y], footprint?:[[x,y],...]}]} in mm, or {dwellings:[...]}. Room names in the " +
        "charter vocabulary (Entrée, Séjour / Cuisine, Chambre 1, Sde, Sdb, WC, Dgt, Rgt) or an explicit kind. Each " +
        "check carries its source: Réglementaire rules are to be confirmed on the text in force, Pratique agence rules " +
        "by the agency. Run validate_dwelling on the model once it is built.")]
    public static string ValidateSpec(
        [Description("One dwelling object, or {\"dwellings\": [...]} — see the tool description for the schema")] System.Text.Json.JsonElement? spec = null,
        [Description("Rule overrides, e.g. {\"RATIO_CIRCULATION\": {\"max\": 0.10}, \"WC_SEPARE\": {\"enabled\": false}}")] System.Text.Json.JsonElement? rules = null,
        [Description("Return the rule catalogue (ids, labels, sources, severities, thresholds) instead of checking anything")] bool listRules = false)
    {
        var ruleSet = DwellingRuleSet.CreateDefault();
        if (listRules)
            return new JObject
            {
                ["success"] = true,
                ["ruleSet"] = ruleSet.Name,
                ["rules"] = ruleSet.Describe(),
                ["note"] = "Réglementaire: arrêté du 24 décembre 2015, to be checked on the text in force before any opposable use. " +
                           "Pratique agence: findings of the 2026-09-24 session, to be confirmed by the agency."
            }.ToString();

        if (!JsonOptionalParam.IsProvided(spec) || !JsonObjectParam.TryParse(spec, out var specObject))
            return JsonObjectParam.InvalidObjectResult("validate_spec", "spec", spec);

        var errors = new List<string>();
        JObject? overrides = null;
        if (JsonOptionalParam.IsProvided(rules))
        {
            if (!JsonObjectParam.TryParse(rules, out var rulesObject))
                return JsonObjectParam.InvalidObjectResult("validate_spec", "rules", rules);
            overrides = rulesObject;
        }
        ruleSet.ApplyOverrides(overrides, errors);

        var dwellingObjects = specObject["dwellings"] is JArray array
            ? array.OfType<JObject>().ToList()
            : new List<JObject> { specObject };
        var specs = dwellingObjects.Select(d => DwellingSpec.Parse(d, errors)).ToList();

        if (errors.Count > 0)
            return new JObject
            {
                ["success"] = false,
                ["error"] = new JObject
                {
                    ["code"] = "InvalidInput",
                    ["tool"] = "validate_spec",
                    ["message"] = $"{errors.Count} problem(s) in the spec or the rule overrides.",
                    ["problems"] = new JArray(errors),
                    ["suggestion"] = "Fix the listed items; coordinates are mm, polygons [[x,y],...] with at least 3 points.",
                    ["stage"] = "validation",
                    ["modelChanged"] = false
                }
            }.ToString();

        var reports = specs.Select(s =>
        {
            // One rule set per dwelling: overrides apply to each, results never leak between them.
            var perDwelling = DwellingRuleSet.CreateDefault();
            perDwelling.ApplyOverrides(overrides, new List<string>());
            return DwellingValidator.Validate(s, perDwelling);
        }).ToList();

        int Count(Func<JToken?, bool> predicate) => reports.Count(r => predicate(r["compliant"]));
        return new JObject
        {
            ["success"] = true,
            ["ruleSet"] = ruleSet.Name,
            ["summary"] = new JObject
            {
                ["dwellingCount"] = reports.Count,
                ["compliant"] = Count(c => c?.Type == JTokenType.Boolean && c.Value<bool>()),
                ["nonCompliant"] = Count(c => c?.Type == JTokenType.Boolean && !c.Value<bool>()),
                ["undecided"] = Count(c => c?.Type != JTokenType.Boolean)
            },
            ["dwellings"] = new JArray(reports),
            ["modelChanged"] = false,
            ["next"] = "Fix the failed checks in the JSON and validate again; build only a compliant design, then run " +
                       "capture_view and validate_dwelling on the model."
        }.ToString();
    }

    [McpServerTool(Name = "validate_dwelling"), Description(
        "Check the dwellings of a level AS BUILT in Revit against the same rules as validate_spec. A dwelling is the set " +
        "of rooms of the level sharing a value of dwellingParameter (ARC_PAR_NUMERO_LOGEMENT by default); its typology " +
        "comes from typologyParameter (ARC_PAR_TYPOLOGIE). Room outlines, doors (with the rooms they join) and equipment " +
        "footprints (visible solids, never the bounding box) are read from the model. Omit dwelling to check every " +
        "dwelling of the level. includeSpec=true also returns each dwelling as design JSON — reusable as a reference plan " +
        "in validate_spec. Read-only; works while RiveTT is locked.")]
    public static async Task<string> ValidateDwelling(
        RiveTT.Server.Connection.RevitConnectionManager revit,
        [Description("Level name, e.g. \"R+1\" (or pass levelId)")] string? level = null,
        [Description("Level element id")] long? levelId = null,
        [Description("Dwelling id (value of dwellingParameter), e.g. \"A2\". Omit to check all dwellings of the level")] string? dwelling = null,
        [Description("Room parameter holding the dwelling id. Default: ARC_PAR_NUMERO_LOGEMENT")] string? dwellingParameter = null,
        [Description("Room parameter holding the typology (T1..T5). Default: ARC_PAR_TYPOLOGIE")] string? typologyParameter = null,
        [Description("Programme area in m² for the dwelling (only with dwelling)")] double? targetAreaM2 = null,
        [Description("Rule overrides, same format as validate_spec")] System.Text.Json.JsonElement? rules = null,
        [Description("Also return each dwelling as design JSON. Default: false")] bool includeSpec = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["includeSpec"] = includeSpec };
        if (level != null) p["level"] = level;
        if (levelId != null) p["levelId"] = levelId;
        if (dwelling != null) p["dwelling"] = dwelling;
        if (dwellingParameter != null) p["dwellingParameter"] = dwellingParameter;
        if (typologyParameter != null) p["typologyParameter"] = typologyParameter;
        if (targetAreaM2 != null) p["targetAreaM2"] = targetAreaM2;
        if (JsonOptionalParam.IsProvided(rules))
        {
            if (!JsonObjectParam.TryParse(rules, out var rulesObject))
                return JsonObjectParam.InvalidObjectResult("validate_dwelling", "rules", rules);
            p["rules"] = rulesObject;
        }
        return (await revit.ExecuteAsync("validate_dwelling", p, 240, ct)).ToString();
    }
}
