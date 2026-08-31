using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Loads a .rfa family, lists loaded families, or duplicates a family type.
/// </summary>
[ToolSafety(false, false)]
public class LoadFamilyTool : IRiveTTTool
{
    public string Name => "load_family";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Loads a .rfa family, lists loaded families, or duplicates a family type.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "load" => LoadFamily(doc, input, session),
                "list" => ListFamilies(doc, input),
                "duplicate_type" => DuplicateType(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: load, list, duplicate_type")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"load_family could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> LoadFamily(Document doc, JObject input, RiveTTSession session)
    {
        var familyPath = input["familyPath"]?.Value<string>();
        if (string.IsNullOrEmpty(familyPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "familyPath is required for load");

        // The overload without IFamilyLoadOptions returns false (does nothing) the
        // moment a same-named family already exists in the project — the normal
        // case for reloading a family edited outside Revit, and get_server_capabilities'
        // documented way to do it. Default to overwriting: see P1.7 in
        // PLAN_CORRECTION.md.
        var overwriteExisting = input["overwriteExisting"]?.Value<bool>() ?? true;

        if (!PathSafety.TryResolveSafe(familyPath, out var safeFamilyPath, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                suggestion: "Give an absolute .rfa path outside the Windows system folders; "
                          + "the project drive and network shares are accepted.");

        if (!File.Exists(safeFamilyPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"familyPath does not exist: {safeFamilyPath}");

        if (!session.RequestConfirmation("load family", 1))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Load Family");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        if (doc.LoadFamily(safeFamilyPath, new Utilities.OverwritingFamilyLoadOptions(overwriteExisting), out var family))
        {
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            var types = family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(fs => fs != null)
                .Select(fs => new { id = ToolHelpers.GetElementIdValue(fs!.Id), name = fs.Name })
                .ToList();

            return RiveTTResult<object>.Ok(new
            {
                familyId = ToolHelpers.GetElementIdValue(family.Id),
                familyName = family.Name,
                categoryName = family.FamilyCategory?.Name,
                typeCount = types.Count,
                types
            });
        }

        tx.RollBack();
        // The path exists and was readable — a false return here means Revit
        // itself refused it (corrupt file, category not loadable in this
        // document, family already identical), not a bad path.
        return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
            "Revit refused to load the family from a valid path: the file may be corrupt, its category may " +
            "not be loadable into this document, or (with overwriteExisting=false) a family of the same name " +
            "already exists.",
            suggestion: overwriteExisting
                ? "Check the .rfa opens cleanly in Revit and its category matches the target document."
                : "Retry with overwriteExisting=true to update the family already in the project.");
    }

    private static RiveTTResult<object> ListFamilies(Document doc, JObject input)
    {
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();

        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryFilter!);
            if (catId != ElementId.InvalidElementId)
                families = families.Where(f => f.FamilyCategory?.Id == catId);
        }

        var result = families.Select(f => new
        {
            id = ToolHelpers.GetElementIdValue(f.Id),
            name = f.Name,
            category = f.FamilyCategory?.Name,
            isEditable = f.IsEditable,
            typeCount = f.GetFamilySymbolIds().Count
        }).ToList();

        return RiveTTResult<object>.Ok(new { familyCount = result.Count, families = result });
    }

    private static RiveTTResult<object> DuplicateType(Document doc, JObject input)
    {
        var sourceTypeId = input["sourceTypeId"]?.Value<long>() ?? 0;
        var newTypeName = input["newTypeName"]?.Value<string>();

        if (sourceTypeId <= 0 || string.IsNullOrEmpty(newTypeName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sourceTypeId and newTypeName required");

        var sourceType = doc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
        if (sourceType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Source family type not found");

        using var tx = new Transaction(doc, "RiveTT: Duplicate Family Type");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var newType = sourceType.Duplicate(newTypeName) as FamilySymbol;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            newTypeId = newType != null ? ToolHelpers.GetElementIdValue(newType.Id) : 0,
            newTypeName = newType?.Name,
            familyName = sourceType.FamilyName
        });
    }
}
