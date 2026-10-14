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
/// Duplicates a loadable family type (FamilySymbol) with a new name,
/// optionally setting parameter values on the new type in the same transaction.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class DuplicateFamilyTypeTool : IRiveTTTool
{
    public string Name => "duplicate_family_type";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a loadable family type (door, window, furniture, etc.) with a new name. Optionally sets type parameters on the new type in the same operation. "
        + "parameterOverrides units: a bare number on a LENGTH parameter is millimetres (area m², volume m³, angle degrees), "
        + "NOT Revit's internal feet; a string carries its own unit (\"900 mm\"). Any other numeric parameter keeps internal units. "
        + "Each applied parameter reports value + unit + display + internalValue, so a wrong unit is visible in the answer. "
        + "dryRun=true runs the duplication in a transaction and rolls it back.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var sourceTypeId   = input["sourceTypeId"]?.Value<long?>();
        var sourceTypeName = input["sourceTypeName"]?.Value<string>();
        var familyName     = input["familyName"]?.Value<string>();
        var newName        = input["newName"]?.Value<string>();
        var parameterOverrides = input["parameterOverrides"] as JObject;
        // Applies immediately when omitted: that is what this tool did before it could preview.
        var dryRun = ToolHelpers.GetDryRun(input, defaultValue: false);

        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide the name for the duplicated type");

        try
        {
            FamilySymbol? source = null;

            if (sourceTypeId.HasValue)
            {
                source = doc!.GetElement(ToolHelpers.ToElementId(sourceTypeId.Value)) as FamilySymbol;
                if (source == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                        $"Element {sourceTypeId} not found or is not a FamilySymbol",
                        suggestion: "Provide a valid loadable family type ID. Use list_family_types to list them.");
            }
            else if (!string.IsNullOrWhiteSpace(sourceTypeName))
            {
                source = FindFamilySymbol(doc!, sourceTypeName!, familyName);
                if (source == null)
                {
                    var hint = string.IsNullOrWhiteSpace(familyName)
                        ? "If the type name exists in multiple families, add familyName to disambiguate."
                        : $"No type '{sourceTypeName}' found in family '{familyName}'.";
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                        $"Family type '{sourceTypeName}' not found",
                        suggestion: $"{hint} Use list_family_types to list available types.");
                }
            }
            else
            {
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "Provide sourceTypeId or sourceTypeName to identify the source type");
            }

            // Check if target name already exists in the same family
            var existing = FindFamilySymbol(doc!, newName!, source.FamilyName);
            if (existing != null)
            {
                return RiveTTResult<object>.Ok(new
                {
                    typeId = ToolHelpers.GetElementIdValue(existing),
                    typeName = existing.Name,
                    familyName = existing.FamilyName,
                    categoryName = existing.Category?.Name ?? "",
                    alreadyExisted = true
                });
            }

            using (var tx = new Transaction(doc!, "RiveTT: Duplicate Family Type"))
            {
                tx.Start();
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

                var newType = source.Duplicate(newName) as FamilySymbol;
                if (newType == null)
                {
                    tx.RollBack();
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                        "Duplicate returned null — the type could not be created",
                        suggestion: "Revit refuses the duplicate when the new name is already taken in the family, or when the source type is the only one left and the family forbids it. List the existing types with get_available_family_types and pick a free name.");
                }

                // Apply parameter overrides if provided
                var appliedParams = new List<object>();
                var failedParams = new List<object>();
                var warnings = new List<string>();

                if (parameterOverrides != null)
                {
                    foreach (var prop in parameterOverrides.Properties())
                    {
                        var param = newType.LookupParameter(prop.Name);
                        if (param == null || param.IsReadOnly)
                        {
                            failedParams.Add(new { name = prop.Name, reason = param == null ? "not found" : "read-only" });
                            continue;
                        }

                        try
                        {
                            if (TrySetParameterValue(param, prop.Value, out var interpretedAs, out var reason))
                            {
                                // Reports what the parameter NOW holds, in display units, next to the
                                // raw internal number — the answer used to echo the request, which
                                // hid a 1500 applied as 1500 feet (457 m) until the model was opened.
                                var applied = ParameterValueFormatter.Format(param);
                                appliedParams.Add(new
                                {
                                    name = prop.Name,
                                    requested = prop.Value.ToString(),
                                    interpretedAs,
                                    value = applied.Value,
                                    unit = applied.Unit,
                                    display = applied.DisplayValue,
                                    internalValue = applied.InternalValue
                                });
                                if (IsLength(param) && applied.InternalValue is double feet
                                    && Math.Abs(feet) * LengthUnits.MmPerFoot > 100_000)
                                    warnings.Add($"'{prop.Name}' now holds {applied.DisplayValue} — over 100 m for a family dimension. Check the unit of the value passed.");
                            }
                            else
                                failedParams.Add(new { name = prop.Name, reason });
                        }
                        catch (Exception ex)
                        {
                            failedParams.Add(new { name = prop.Name, reason = ex.Message });
                        }
                    }
                }

                if (dryRun)
                {
                    var previewPayload = new
                    {
                        typeName = newType.Name,
                        familyName = newType.FamilyName,
                        categoryName = newType.Category?.Name ?? "",
                        sourceTypeName = source.Name,
                        alreadyExisted = false,
                        appliedParameters = appliedParams,
                        failedParameters = failedParams,
                        warnings
                    };
                    ChangePreview.Rollback(tx);
                    return ChangePreview.Probed(
                        $"DryRun: would create the type '{previewPayload.typeName}' in '{previewPayload.familyName}' from '{source.Name}'.",
                        previewPayload);
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");

                var result = new Dictionary<string, object?>
                {
                    ["typeId"] = ToolHelpers.GetElementIdValue(newType),
                    ["typeName"] = newType.Name,
                    ["familyName"] = newType.FamilyName,
                    ["categoryName"] = newType.Category?.Name ?? "",
                    ["sourceTypeName"] = source.Name,
                    ["alreadyExisted"] = false
                };

                if (appliedParams.Count > 0)
                    result["appliedParameters"] = appliedParams;
                if (failedParams.Count > 0)
                    result["failedParameters"] = failedParams;
                if (warnings.Count > 0)
                    result["warnings"] = warnings;

                return RiveTTResult<object>.Ok(result);
            }
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"duplicate_family_type could not duplicate family type: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static FamilySymbol? FindFamilySymbol(Document doc, string typeName, string? familyName)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>();

        if (!string.IsNullOrWhiteSpace(familyName))
            collector = collector.Where(fs => fs.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        return collector.FirstOrDefault(fs => fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static ForgeTypeId? SpecOf(Parameter param)
    {
        try { return param.Definition.GetDataType(); } catch { return null; }
    }

    private static bool IsLength(Parameter param)
        => SpecOf(param)?.Equals(SpecTypeId.Length) == true;

    /// <summary>
    /// The unit a BARE number is read in, per parameter spec — the agency's: mm, m², m³,
    /// degrees. Revit stores feet, and a bare 1500 used to be applied as 1500 feet.
    /// Null for every other spec, which keeps internal units and says so.
    /// </summary>
    private static ForgeTypeId? AgencyUnit(Parameter param, out string label)
    {
        label = "";
        var spec = SpecOf(param);
        if (spec == null) return null;
        if (spec.Equals(SpecTypeId.Length)) { label = "mm"; return UnitTypeId.Millimeters; }
        if (spec.Equals(SpecTypeId.Area)) { label = "m²"; return UnitTypeId.SquareMeters; }
        if (spec.Equals(SpecTypeId.Volume)) { label = "m³"; return UnitTypeId.CubicMeters; }
        if (spec.Equals(SpecTypeId.Angle)) { label = "°"; return UnitTypeId.Degrees; }
        return null;
    }

    private static bool TrySetParameterValue(Parameter param, JToken value, out string interpretedAs, out string reason)
    {
        interpretedAs = "";
        reason = "type mismatch";
        switch (param.StorageType)
        {
            case StorageType.Double:
                if (value.Type == JTokenType.String)
                {
                    // A string carries its own unit ("900 mm"), parsed by Revit.
                    var text = (value.Value<string>() ?? "").Trim();
                    if (param.SetValueString(text))
                    {
                        interpretedAs = $"\"{text}\", unit read from the text";
                        return true;
                    }
                    reason = $"'{text}' is not a valid value with a unit for this parameter (e.g. \"900 mm\")";
                    return false;
                }
                if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                {
                    var number = value.Value<double>();
                    var unit = AgencyUnit(param, out var label);
                    if (unit != null)
                    {
                        param.Set(UnitUtils.ConvertToInternalUnits(number, unit));
                        interpretedAs = $"{number} {label}";
                    }
                    else
                    {
                        param.Set(number);
                        interpretedAs = $"{number} in Revit internal units (no agency unit for this parameter type)";
                    }
                    return true;
                }
                return false;

            case StorageType.Integer:
                if (value.Type == JTokenType.Integer)
                {
                    param.Set(value.Value<int>());
                    interpretedAs = value.ToString();
                    return true;
                }
                return false;

            case StorageType.String:
                param.Set(value.ToString());
                interpretedAs = "text";
                return true;

            case StorageType.ElementId:
                if (value.Type == JTokenType.Integer)
                {
                    param.Set(ToolHelpers.ToElementId(value.Value<long>()));
                    interpretedAs = "element id";
                    return true;
                }
                return false;

            default:
                reason = $"unsupported storage type {param.StorageType}";
                return false;
        }
    }
}
