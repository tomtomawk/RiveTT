using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists, creates, and deletes named view/sheet sets (ViewSheetSet) — the saved
/// selections batch_export lacks, so a print/export list does not need re-passing
/// every time. Built on Document.PrintManager.ViewSheetSetting, the API's own path
/// for saving a named set (PrintRange.Select + ViewSheetSetting.SaveAs).
/// </summary>
[ReadOnlyActions("list", "list", "list_collections")]
[ToolSafety(false, false)]
public class ManageSheetSetsTool : IRiveTTTool
{
    public string Name => "manage_sheet_sets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Lists, creates, or deletes named view/sheet sets (ViewSheetSet), so batch_export and printing can " +
        "reuse a saved list of views/sheets instead of one passed on every call. action=list|create|delete. " +
        "create needs name + viewIds/sheetIds (element IDs, either views or sheets). Revit has no rename API for " +
        "a saved set: recreate it under the new name instead. NOT the same thing as the sheet sets (jeux de feuilles) of " +
        "the Project Browser: those are sheet collections (SheetCollection, Revit 2025+), handled by action=" +
        "list_collections | create_collection (name) | assign_to_collection (collectionId or collectionName + " +
        "sheetIds). A sheet number must be unique within its collection.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();
        try
        {
            return action switch
            {
                "list" => ListSheetSets(doc),
                "create" => CreateSheetSet(doc, input),
                "delete" => DeleteSheetSet(doc, input),
                "list_collections" => ListCollections(doc),
                "create_collection" => CreateCollection(doc, input),
                "assign_to_collection" => AssignToCollection(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unsupported action: {action}",
                    suggestion: "Use: list | create | delete | list_collections | create_collection | assign_to_collection")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_sheet_sets could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> ListSheetSets(Document doc)
    {
        var sets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheetSet))
            .Cast<ViewSheetSet>()
            .Select(s => new
            {
                id = ToolHelpers.GetElementIdValue(s.Id),
                name = s.Name,
                viewCount = s.Views.Size
            })
            .ToList();

        return RiveTTResult<object>.Ok(new { count = sets.Count, sheetSets = sets });
    }

    private static RiveTTResult<object> CreateSheetSet(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        var ids = input["viewIds"]?.ToObject<List<long>>() ?? input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (string.IsNullOrWhiteSpace(name) || ids.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "name and a non-empty viewIds/sheetIds array are required");

        var viewSet = new ViewSet();
        var missing = new List<long>();
        foreach (var vid in ids)
        {
            var view = doc.GetElement(ToolHelpers.ToElementId(vid)) as View;
            if (view == null) { missing.Add(vid); continue; }
            viewSet.Insert(view);
        }

        if (viewSet.IsEmpty)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "None of the given IDs resolved to a view or sheet", suggestion: "Check the element IDs");

        var printManager = doc.PrintManager;

        using var tx = new Transaction(doc, "RiveTT: Create Sheet Set");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

        printManager.PrintRange = PrintRange.Select;
        var viewSheetSetting = printManager.ViewSheetSetting;
        viewSheetSetting.CurrentViewSheetSet.Views = viewSet;

        // A name already in use throws; surface that instead of a rolled-back transaction
        // with no explanation.
        try
        {
            viewSheetSetting.SaveAs(name!);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Could not save sheet set '{name}': {ex.Message}",
                suggestion: "A sheet set with this name may already exist; delete it first or pick another name.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        var created = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>()
            .FirstOrDefault(s => s.Name == name);

        return RiveTTResult<object>.Ok(new
        {
            action = "create",
            name,
            id = created != null ? ToolHelpers.GetElementIdValue(created.Id) : (long?)null,
            includedCount = viewSet.Size,
            missingIds = missing
        });
    }

    private static List<SheetCollection> AllCollections(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(SheetCollection)).Cast<SheetCollection>().ToList();

    private static RiveTTResult<object> ListCollections(Document doc)
    {
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
        var collections = AllCollections(doc).Select(c => new
        {
            id = ToolHelpers.GetElementIdValue(c.Id),
            name = c.Name,
            sheetCount = sheets.Count(s => s.SheetCollectionId == c.Id)
        }).ToList();
        var unassigned = sheets.Count(s => s.SheetCollectionId == ElementId.InvalidElementId);
        return RiveTTResult<object>.Ok(new
        {
            count = collections.Count,
            sheetCollections = collections,
            sheetsWithoutCollection = unassigned
        });
    }

    private static RiveTTResult<object> CreateCollection(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required for create_collection");
        if (AllCollections(doc).Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A sheet collection named '{name}' already exists.",
                suggestion: "Pick another name, or assign sheets to the existing one with assign_to_collection.");

        using var tx = new Transaction(doc, "RiveTT: Create Sheet Collection");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        var created = SheetCollection.Create(doc, name!);
        var id = ToolHelpers.GetElementIdValue(created.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");
        return RiveTTResult<object>.Ok(new { action = "create_collection", id, name });
    }

    private static RiveTTResult<object> AssignToCollection(Document doc, JObject input)
    {
        var collectionId = input["collectionId"]?.Value<long?>() ?? 0;
        var collectionName = input["collectionName"]?.Value<string>();
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? input["viewIds"]?.ToObject<List<long>>()
            ?? new List<long>();
        if (sheetIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "sheetIds (or viewIds carrying sheet ids) is required for assign_to_collection");

        var collection = collectionId > 0
            ? doc.GetElement(ToolHelpers.ToElementId(collectionId)) as SheetCollection
            : AllCollections(doc).FirstOrDefault(c => string.Equals(c.Name, collectionName, StringComparison.OrdinalIgnoreCase));
        if (collection == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                "No matching sheet collection found",
                suggestion: "Pass collectionId or an exact collectionName from action=list_collections, or create it with create_collection.");

        var assigned = new List<long>();
        var failed = new List<object>();
        using var tx = new Transaction(doc, "RiveTT: Assign Sheets To Collection");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        foreach (var sid in sheetIds)
        {
            if (doc.GetElement(ToolHelpers.ToElementId(sid)) is not ViewSheet sheet)
            {
                failed.Add(new { sheetId = sid, reason = "not a sheet" });
                continue;
            }
            try
            {
                sheet.SheetCollectionId = collection.Id;
                assigned.Add(sid);
            }
            catch (Exception ex)
            {
                failed.Add(new { sheetId = sid, sheetNumber = sheet.SheetNumber, reason = ex.Message });
            }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "A sheet number must be unique within its collection: two sheets with the same number cannot share one.");
        return RiveTTResult<object>.Ok(new
        {
            action = "assign_to_collection",
            collectionId = ToolHelpers.GetElementIdValue(collection.Id),
            collectionName = collection.Name,
            assignedCount = assigned.Count,
            assignedSheetIds = assigned,
            failed
        });
    }

    private static RiveTTResult<object> DeleteSheetSet(Document doc, JObject input)
    {
        var elementIdLong = input["elementId"]?.Value<long?>() ?? 0;
        var name = input["name"]?.Value<string>();

        ViewSheetSet? target = elementIdLong > 0
            ? doc.GetElement(ToolHelpers.ToElementId(elementIdLong)) as ViewSheetSet
            : new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>()
                .FirstOrDefault(s => s.Name == name);

        if (target == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                "No matching sheet set found", suggestion: "Provide elementId or an exact name from action=list");

        using var tx = new Transaction(doc, "RiveTT: Delete Sheet Set");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        var deletedName = target.Name;
        doc.Delete(target.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return RiveTTResult<object>.Ok(new { action = "delete", name = deletedName });
    }
}
