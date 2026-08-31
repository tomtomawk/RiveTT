using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Parameters;

/// <summary>
/// Lists, creates, reads, updates, or deletes global parameters in the project.
/// Global parameters are project-level named values that can drive dimensions and constraints.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class ManageGlobalParametersTool : IRiveTTTool
{
    public string Name => "manage_global_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Lists, creates, reads, updates, or deletes global parameters. Actions: list, get, create, set, delete, "
        + "rename, set_formula, move_up, move_down, sort. The write actions preview by default: delete in "
        + "particular breaks every formula and dimension driven by the parameter. Set dryRun=false to apply.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        if (!GlobalParametersManager.AreGlobalParametersAllowed(doc))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Global parameters are not supported in this document type (families not supported)");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();

        // Gated centrally rather than in each of the eight write branches. Honouring dryRun
        // on some actions only is precisely the defect the router gate was added for, and a
        // tool with eight branches is where that omission would happen.
        if (WriteActions.Contains(action) && ToolHelpers.GetDryRun(input))
        {
            var preview = PreviewAction(doc, action, input);
            if (preview != null) return preview;
        }

        try
        {
            return action switch
            {
                "list"        => ListGlobalParameters(doc),
                "get"         => GetGlobalParameter(doc, input),
                "create"      => CreateGlobalParameter(doc, input),
                "set"         => SetGlobalParameterValue(doc, input),
                "delete"      => DeleteGlobalParameter(doc, input, session),
                "rename"      => RenameGlobalParameter(doc, input),
                "set_formula" => SetGlobalParameterFormula(doc, input),
                "move_up"     => ReorderGlobalParameter(doc, input, up: true),
                "move_down"   => ReorderGlobalParameter(doc, input, up: false),
                "sort"        => SortGlobalParameters(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: list, get, create, set, delete, rename, set_formula, move_up, move_down, sort")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_global_parameters could not manage global parameters: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    /// <summary>Actions that modify the document; everything else is a read.</summary>
    private static readonly HashSet<string> WriteActions = new(StringComparer.Ordinal)
    {
        "create", "set", "delete", "rename", "set_formula", "move_up", "move_down", "sort"
    };

    /// <summary>
    /// The dryRun answer for a write action: what would change, and — for delete — what
    /// else depends on the parameter. Returns null when the action cannot be resolved, so
    /// the real path runs and produces its own precise error instead of a vague preview.
    /// </summary>
    private static RiveTTResult<object>? PreviewAction(Document doc, string action, JObject input)
    {
        var name = input["name"]?.Value<string>() ?? input["parameterName"]?.Value<string>();
        var existingId = string.IsNullOrWhiteSpace(name)
            ? ElementId.InvalidElementId
            : GlobalParametersManager.FindByName(doc, name);
        var existing = existingId == ElementId.InvalidElementId
            ? null
            : doc.GetElement(existingId) as GlobalParameter;

        switch (action)
        {
            case "create":
                if (string.IsNullOrWhiteSpace(name)) return null;
                return ChangePreview.Declared(
                    $"DryRun: would create the global parameter '{name}'.",
                    new { action, name, alreadyExists = existing != null },
                    blockers: existing != null
                        ? new[] { $"A global parameter named '{name}' already exists" }
                        : null);

            case "delete":
                if (existing == null) return null;
                // A global parameter drives dimensions and other parameters through formulas;
                // deleting it breaks them, and DeletionPreview reports what Revit would take.
                return DeletionPreview.Build(doc, existing.Id, $"Global parameter '{existing.Name}'",
                    new { action, name = existing.Name });

            case "rename":
                if (existing == null) return null;
                return ChangePreview.Declared(
                    $"DryRun: would rename the global parameter '{existing.Name}' to "
                    + $"'{input["newName"]?.Value<string>()}'. Formulas referencing it by name follow the rename.",
                    new { action, oldName = existing.Name, newName = input["newName"]?.Value<string>() });

            case "set":
            case "set_formula":
                if (existing == null) return null;
                return ChangePreview.Declared(
                    $"DryRun: would change the global parameter '{existing.Name}'. Everything it drives "
                    + "— dimensions, other parameters through formulas — changes with it.",
                    new
                    {
                        action,
                        name = existing.Name,
                        newValue = input["value"]?.ToString(),
                        newFormula = input["formula"]?.Value<string>()
                    });

            case "move_up":
            case "move_down":
            case "sort":
                return ChangePreview.Declared(
                    $"DryRun: would reorder the global parameters ({action}). Order affects evaluation "
                    + "of formulas, not the values themselves.",
                    new { action, name });
        }

        return null;
    }

    private static RiveTTResult<object> ListGlobalParameters(Document doc)
    {
        var paramIds = GlobalParametersManager.GetAllGlobalParameters(doc);
        var parameters = paramIds
            .Select(id => doc.GetElement(id) as GlobalParameter)
            .Where(gp => gp != null)
            .Select(gp => BuildParameterInfo(gp!))
            .ToList();

        return RiveTTResult<object>.Ok(new
        {
            parameterCount = parameters.Count,
            parameters
        });
    }

    private static RiveTTResult<object> GetGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        return RiveTTResult<object>.Ok(BuildParameterInfo(gp));
    }

    private static RiveTTResult<object> CreateGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        if (FindByName(doc, name!) != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A global parameter named '{name}' already exists");

        var dataType    = input["dataType"]?.Value<string>() ?? "text";
        var initialValue = input["value"]?.Value<string>();

        using var tx = new Transaction(doc, "RiveTT: Create Global Parameter");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

#if REVIT2023_OR_GREATER
        var gp = GlobalParameter.Create(doc, name, ResolveSpecTypeId(dataType));
#else
        var gp = GlobalParameter.Create(doc, name, ResolveParameterType(dataType));
#endif

        if (!string.IsNullOrEmpty(initialValue))
            ApplyStringValue(gp, initialValue!);

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            action = "create",
            name = gp.Name,
            elementId = ToolHelpers.GetElementIdValue(gp.Id),
            dataType
        });
    }

    private static RiveTTResult<object> SetGlobalParameterValue(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        var value = input["value"]?.Value<string>();
        if (value == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "value is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        if (!string.IsNullOrEmpty(GetFormula(gp)))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Parameter '{name}' is driven by a formula and cannot be set directly");

        using var tx = new Transaction(doc, "RiveTT: Set Global Parameter Value");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        ApplyStringValue(gp, value);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "set", name, value });
    }

    private static RiveTTResult<object> DeleteGlobalParameter(Document doc, JObject input, RiveTTSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        using var tx = new Transaction(doc, "RiveTT: Delete Global Parameter");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        doc.Delete(gp.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "delete", name });
    }

    /// <summary>
    /// Renames a global parameter. Unlike shared/project parameters, the
    /// GlobalParameter.Name setter is writable, so this is supported.
    /// </summary>
    private static RiveTTResult<object> RenameGlobalParameter(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Both name and newName are required for rename.");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        if (!GlobalParametersManager.IsUniqueName(doc, newName!))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A global parameter named '{newName}' already exists.");

        using var tx = new Transaction(doc, "RiveTT: Rename Global Parameter");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        gp.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "rename", oldName = name, newName });
    }

    /// <summary>
    /// Sets (or clears, with an empty string) the formula driving a global
    /// parameter. A formula makes the parameter's value read-only.
    /// </summary>
    private static RiveTTResult<object> SetGlobalParameterFormula(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        // formula may be empty string to clear; treat missing (null) as an error.
        if (input["formula"] == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "formula is required for set_formula (pass an empty string to clear the formula).");
        var formula = input["formula"]!.Value<string>() ?? "";

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        using var tx = new Transaction(doc, "RiveTT: Set Global Parameter Formula");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        try
        {
            gp.SetFormula(formula);
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
        }
        catch (Exception ex)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Revit rejected the formula: {ex.Message}",
                suggestion: "Check the formula syntax and that referenced parameters exist and don't create a circular reference.");
        }

        return RiveTTResult<object>.Ok(new
        {
            action = "set_formula",
            name,
            formula = string.IsNullOrEmpty(formula) ? null : formula,
            cleared = string.IsNullOrEmpty(formula)
        });
    }

    /// <summary>
    /// Moves a global parameter up or down in evaluation/display order. Ordering
    /// only shifts within the parameter's group.
    /// </summary>
    private static RiveTTResult<object> ReorderGlobalParameter(Document doc, JObject input, bool up)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required");

        var gp = FindByName(doc, name!);
        if (gp == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"Global parameter '{name}' not found");

        bool moved;
        using (var tx = new Transaction(doc, "RiveTT: Reorder Global Parameter"))
        {
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            moved = up
                ? GlobalParametersManager.MoveParameterUpOrder(doc, gp.Id)
                : GlobalParametersManager.MoveParameterDownOrder(doc, gp.Id);
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
        }

        return RiveTTResult<object>.Ok(new
        {
            action = up ? "move_up" : "move_down",
            name,
            moved,
            message = moved ? null : "Already at the boundary of its group; no move performed."
        });
    }

    /// <summary>
    /// Sorts all global parameters ascending or descending (within each group).
    /// </summary>
    private static RiveTTResult<object> SortGlobalParameters(Document doc, JObject input)
    {
        var order = (input["order"]?.Value<string>() ?? "ascending").ToLowerInvariant();
        var sortOrder = order == "descending" || order == "desc"
            ? ParametersOrder.Descending
            : ParametersOrder.Ascending;

        using (var tx = new Transaction(doc, "RiveTT: Sort Global Parameters"))
        {
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            GlobalParametersManager.SortParameters(doc, sortOrder);
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
        }

        return RiveTTResult<object>.Ok(new { action = "sort", order = sortOrder.ToString() });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static GlobalParameter? FindByName(Document doc, string name)
    {
        return GlobalParametersManager.GetAllGlobalParameters(doc)
            .Select(id => doc.GetElement(id) as GlobalParameter)
            .FirstOrDefault(gp => gp?.Name == name);
    }

    private static object BuildParameterInfo(GlobalParameter gp)
    {
        string valueStr  = "";
        string valueType = "unknown";

        try
        {
            var val = gp.GetValue();
            switch (val)
            {
                case DoubleParameterValue  dpv: valueStr = dpv.Value.ToString("F6");  valueType = "double";    break;
                case StringParameterValue  spv: valueStr = spv.Value ?? "";            valueType = "string";    break;
                case IntegerParameterValue ipv: valueStr = ipv.Value.ToString();       valueType = "integer";   break;
                case ElementIdParameterValue epv:
                    valueStr = epv.Value.Value.ToString();
                    valueType = "elementId";
                    break;
            }
        }
        catch { /* parameter may not have a value */ }

        return new
        {
            elementId  = ToolHelpers.GetElementIdValue(gp.Id),
            name       = gp.Name,
            formula    = GetFormula(gp),
            valueType,
            value = valueStr
        };
    }

    private static void ApplyStringValue(GlobalParameter gp, string value)
    {
        try
        {
            var current = gp.GetValue();
            switch (current)
            {
                case DoubleParameterValue:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dv))
                        gp.SetValue(new DoubleParameterValue(dv));
                    break;
                case StringParameterValue:
                    gp.SetValue(new StringParameterValue(value));
                    break;
                case IntegerParameterValue:
                    if (int.TryParse(value, out var iv))
                        gp.SetValue(new IntegerParameterValue(iv));
                    break;
            }
        }
        catch
        {
            // If GetValue fails (e.g. brand-new param with no type info yet), try string
            gp.SetValue(new StringParameterValue(value));
        }
    }

    /// Safely retrieve formula (API surface changed across versions; use reflection as fallback).
    private static string GetFormula(GlobalParameter gp)
    {
        try
        {
            // Revit 2016-2024: GetFormula() method
            var method = typeof(GlobalParameter).GetMethod("GetFormula",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (method != null)
                return (string?)method.Invoke(gp, null) ?? "";

            // Fallback: Formula property
            var prop = typeof(GlobalParameter).GetProperty("Formula");
            return (string?)prop?.GetValue(gp) ?? "";
        }
        catch { return ""; }
    }

#if REVIT2023_OR_GREATER
    private static ForgeTypeId ResolveSpecTypeId(string dataType) =>
        dataType.ToLowerInvariant() switch
        {
            "text"   or "string"          => SpecTypeId.String.Text,
            "integer" or "int"            => SpecTypeId.Int.Integer,
            "number"  or "double" or "real" => SpecTypeId.Number,
            "length"                      => SpecTypeId.Length,
            "area"                        => SpecTypeId.Area,
            "volume"                      => SpecTypeId.Volume,
            "angle"                       => SpecTypeId.Angle,
            "yesno"   or "boolean" or "bool" => SpecTypeId.Boolean.YesNo,
            _                             => SpecTypeId.String.Text
        };
#else
    private static ParameterType ResolveParameterType(string dataType) =>
        dataType.ToLowerInvariant() switch
        {
            "text"    or "string"            => ParameterType.Text,
            "integer" or "int"               => ParameterType.Integer,
            "number"  or "double" or "real"  => ParameterType.Number,
            "length"                         => ParameterType.Length,
            "area"                           => ParameterType.Area,
            "volume"                         => ParameterType.Volume,
            "angle"                          => ParameterType.Angle,
            "yesno"   or "boolean" or "bool" => ParameterType.YesNo,
            _                                => ParameterType.Text
        };
#endif
}
