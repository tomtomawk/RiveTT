using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Duplicates a system family type (wall, floor, roof, ceiling) with a new name.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class DuplicateSystemTypeTool : IRiveTTTool
{
    public string Name => "duplicate_system_type";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Duplicates a system family type (wall, floor, roof, ceiling), or renames/deletes an existing type. "
        + "Actions: duplicate (default), rename, delete. Previews by default: delete reports how many ELEMENTS "
        + "of that type Revit would take with it, which is what makes it destructive. Set dryRun=false to apply.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "duplicate").ToLowerInvariant();
        if (action == "rename") return RenameType(doc, input, session);
        if (action == "delete") return DeleteType(doc, input, session);
        if (action != "duplicate")
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Unknown action: {action}", suggestion: "Use: duplicate, rename, delete");

        var sourceTypeId   = input["sourceTypeId"]?.Value<long?>();
        var sourceTypeName = input["sourceTypeName"]?.Value<string>();
        var category       = input["category"]?.Value<string>();
        var newName        = input["newName"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide the name for the duplicated type");

        try
        {
            // Resolve source type
            ElementType? sourceType = null;

            if (sourceTypeId.HasValue)
            {
                sourceType = doc.GetElement(new ElementId(sourceTypeId.Value)) as ElementType;
                if (sourceType == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                        $"Type {sourceTypeId} not found or is not an ElementType");
            }
            else if (!string.IsNullOrWhiteSpace(sourceTypeName))
            {
                sourceType = FindTypeByName(doc, sourceTypeName!, category);
                if (sourceType == null)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                        $"Type '{sourceTypeName}' not found" + (category != null ? $" in category {category}" : ""),
                        // Pointing at list_family_types was actively
                        // misleading for system types: it used to enumerate only
                        // loadable families, so the suggested remedy returned nothing
                        // for exactly the categories this tool serves.
                        suggestion: "Use list_system_types(category) to list the system types of that " +
                                    "category with their exact names and ids.");
            }
            else
            {
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "Provide sourceTypeId or sourceTypeName to identify the source type");
            }

            // Check if target name already exists
            var existing = FindTypeByName(doc, newName!, category);
            if (existing != null)
            {
                long existingIdValue;
                existingIdValue = existing.Id.Value;
                return RiveTTResult<object>.Ok(new
                {
                    typeId = existingIdValue,
                    typeName = existing.Name,
                    typeCategory = existing.Category?.Name ?? "",
                    alreadyExisted = true
                });
            }

            if (ToolHelpers.GetDryRun(input))
                return ChangePreview.Declared(
                    $"DryRun: would duplicate the type '{sourceType.Name}' as '{newName}'.",
                    new
                    {
                        action = "duplicate",
                        sourceTypeName = sourceType.Name,
                        typeCategory = sourceType.Category?.Name ?? "",
                        newName,
                        alreadyExisted = false
                    });

            // Duplicate
            using (var tx = new Transaction(doc, "RiveTT: Duplicate System Type"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                var newType = sourceType.Duplicate(newName);
                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");

                long newIdValue;
                newIdValue = newType.Id.Value;

                return RiveTTResult<object>.Ok(new
                {
                    typeId = newIdValue,
                    typeName = newType.Name,
                    typeCategory = newType.Category?.Name ?? "",
                    sourceTypeName = sourceType.Name,
                    alreadyExisted = false
                });
            }
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"duplicate_system_type could not duplicate type: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> RenameType(Document doc, JObject input, RiveTTSession session)
    {
        var (type, error) = ResolveType(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "newName is required for rename");

        if (FindTypeByName(doc, newName!, type!.Category?.Name) != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"A type named '{newName}' already exists");

        var oldName = type.Name;

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would rename the type '{oldName}' to '{newName}'. Every element of this type "
                + "follows the rename; none is modified otherwise.",
                new { action = "rename", typeId = ToolHelpers.GetElementIdValue(type.Id), oldName, newName });
        using var tx = new Transaction(doc, "RiveTT: Rename Type");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        type.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "rename", typeId = ToolHelpers.GetElementIdValue(type.Id), oldName, newName });
    }

    private static RiveTTResult<object> DeleteType(Document doc, JObject input, RiveTTSession session)
    {
        var (type, error) = ResolveType(doc, input);
        if (error != null) return error;

        var name = type!.Name;

        // Deleting a type deletes every ELEMENT using it. DeletionPreview probes the real
        // cascade, so the count is Revit own answer rather than an estimate.
        if (ToolHelpers.GetDryRun(input))
            return DeletionPreview.Build(doc, type.Id, $"Type '{name}'",
                new
                {
                    action = "delete",
                    typeId = ToolHelpers.GetElementIdValue(type.Id),
                    deletedType = name,
                    typeCategory = type.Category?.Name ?? ""
                });

        using var tx = new Transaction(doc, "RiveTT: Delete Type");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var deleted = doc.Delete(type.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            action = "delete",
            deletedType = name,
            deletedElementCount = deleted?.Count ?? 0
        });
    }

    /// <summary>Resolves a type by sourceTypeId, or by sourceTypeName/typeName (+optional category).</summary>
    private static (ElementType?, RiveTTResult<object>?) ResolveType(Document doc, JObject input)
    {
        var sourceTypeId = input["sourceTypeId"]?.Value<long?>() ?? input["typeId"]?.Value<long?>();
        var typeName = input["sourceTypeName"]?.Value<string>() ?? input["typeName"]?.Value<string>();
        var category = input["category"]?.Value<string>();

        ElementType? type = null;
        if (sourceTypeId.HasValue && sourceTypeId.Value > 0)
            type = doc.GetElement(ToolHelpers.ToElementId(sourceTypeId.Value)) as ElementType;
        if (type == null && !string.IsNullOrWhiteSpace(typeName))
            type = FindTypeByName(doc, typeName!, category);

        if (type == null)
            return (null, RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                "Type not found",
                suggestion: "Provide sourceTypeId or sourceTypeName; list the candidates with " +
                            "list_system_types(category)."));

        return (type, null);
    }

    private static ElementType? FindTypeByName(Document doc, string typeName, string? category)
    {
        var collector = new FilteredElementCollector(doc)
            .WhereElementIsElementType();

        if (!string.IsNullOrEmpty(category))
        {
            var catId = CategoryResolver.ResolveToId(doc, category!);
            if (catId != null && catId != ElementId.InvalidElementId)
                collector = collector.OfCategoryId(catId);
        }

        return collector
            .OfType<ElementType>()
            .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}
