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
/// CRUD on named saved selections (SelectionFilterElement), replacing save_selection,
/// load_selection, and delete_selection: all three resolved the same element by the same
/// name lookup, the textbook case for an action-based merge. capture_selection stays a
/// separate tool — it manipulates a session-scoped token, not a document-persisted element,
/// and merging the two would mix an ephemeral TTL with document persistence.
/// Actions: save | load | list | delete. name is required for save/load/delete, ignored for
/// list. elementIds/overwrite apply to save only (elementIds absent = current UI selection).
/// selectInView applies to load only. dryRun applies to delete only.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class ManageSelectionTool : IRiveTTTool
{
    public string Name => "manage_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "CRUD on named saved selections (SelectionFilterElement). action=save|load|list|delete. "
        + "name is required for save/load/delete (ignored for list). save: elementIds (absent = "
        + "current UI selection) and overwrite (default false) apply only here. load: "
        + "selectInView (default true) applies only here. delete: dryRun (default true) applies "
        + "only here. Use capture_selection instead for a temporary session-scoped token — this "
        + "tool always persists into the document.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "").ToLowerInvariant();
        return action switch
        {
            "save" => Save(doc, input),
            "load" => Load(doc, input, requireName: true),
            "list" => Load(doc, input, requireName: false),
            "delete" => Delete(doc, input),
            _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Unsupported action: '{action}'", suggestion: "Use: save | load | list | delete")
        };
    }

    private static SelectionFilterElement? FindByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(SelectionFilterElement))
            .Cast<SelectionFilterElement>()
            .FirstOrDefault(sf => sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static RiveTTResult<object> Save(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required for action=save");

        var elementIds = input["elementIds"]?.ToObject<List<long>>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;

        try
        {
            ICollection<ElementId> ids;
            if (elementIds != null && elementIds.Count > 0)
            {
                ids = elementIds.Select(id => new ElementId(id)).ToList();
            }
            else
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                ids = uidoc.Selection.GetElementIds();
                if (ids.Count == 0)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        "No elements selected and no elementIds provided");
            }

            var existing = FindByName(doc, name);

            using var tx = new Transaction(doc, "RiveTT: Save Selection");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            if (existing != null)
            {
                if (!overwrite)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        $"Selection '{name}' already exists. Set overwrite=true to replace.");
                doc.Delete(existing.Id);
            }

            var filter = SelectionFilterElement.Create(doc, name);
            filter.SetElementIds(ids);

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return RiveTTResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                overwritten = existing != null
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_selection could not save selection: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> Load(Document doc, JObject input, bool requireName)
    {
        var name = input["name"]?.Value<string>();
        if (requireName && string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required for action=load");

        var selectInView = input["selectInView"]?.Value<bool>() ?? true;

        try
        {
            var allFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .ToList();

            if (string.IsNullOrEmpty(name))
            {
                var selections = allFilters.Select(sf => new
                {
                    name = sf.Name,
                    id = ToolHelpers.GetElementIdValue(sf.Id),
                    elementCount = sf.GetElementIds().Count
                }).ToList();

                return RiveTTResult<object>.Ok(new
                {
                    selectionCount = selections.Count,
                    selections
                });
            }

            var filter = allFilters.FirstOrDefault(sf =>
                sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call manage_selection(action: \"list\") to see the saved selections.");

            var elementIds = filter.GetElementIds();

            if (selectInView)
            {
                var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
                uidoc.Selection.SetElementIds(elementIds);
            }

            var ids = elementIds.Select(ToolHelpers.GetElementIdValue).ToList();

            return RiveTTResult<object>.Ok(new
            {
                selectionName = name,
                elementCount = ids.Count,
                elementIds = ids,
                selectedInView = selectInView
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_selection could not load selection: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> Delete(Document doc, JObject input)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required for action=delete");

        try
        {
            var filter = FindByName(doc, name);
            if (filter == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call manage_selection(action: \"list\") to see the saved selections.");

            if (ToolHelpers.GetDryRun(input))
            {
                var memberCount = 0;
                try { memberCount = filter.GetElementIds().Count; } catch { }

                return DeletionPreview.Build(doc, filter.Id,
                    $"Saved selection '{name}'",
                    new
                    {
                        selectionName = name,
                        selectionId = ToolHelpers.GetElementIdValue(filter.Id),
                        elementsInSelection = memberCount,
                        note = "Only the saved list is deleted; the " + memberCount
                             + " element(s) it references stay in the model."
                    });
            }

            using var tx = new Transaction(doc, "RiveTT: Delete Selection");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            doc.Delete(filter.Id);
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return RiveTTResult<object>.Ok(new
            {
                deletedSelection = name,
                success = true
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_selection could not delete selection: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
