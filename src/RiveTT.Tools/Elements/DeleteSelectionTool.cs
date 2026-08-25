using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Deletes a named saved selection filter. Defaults to dryRun=true — see DeletionPreview
/// for why the old RequestConfirmation call was not a safety net.
///
/// Only the FILTER is deleted, never the elements it lists; the preview says so explicitly,
/// because "delete_selection" reads like it removes the selected elements.
/// </summary>
[ToolSafety(false, true)]
public class DeleteSelectionTool : ICortexTool
{
    public string Name => "delete_selection";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Deletes a saved selection filter by name. Removes the SAVED LIST only — the elements it references "
        + "are untouched (use delete_element for those). Defaults to dryRun=true; set dryRun=false to execute.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrEmpty(name))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "name is required");

        try
        {
            var filter = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement))
                .Cast<SelectionFilterElement>()
                .FirstOrDefault(sf => sf.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Selection '{name}' not found",
                    suggestion: "Call load_selection without a name to list the saved selections.");

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
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                deletedSelection = name,
                success = true
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to delete selection: {ex.Message}");
        }
    }
}
