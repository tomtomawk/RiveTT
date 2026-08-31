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
/// Creates, renames, deletes, opens, closes, or sets the active workset.
/// Write counterpart of <c>list_worksets</c>; only available for workshared documents.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class ManageWorksetsTool : IRiveTTTool
{
    public string Name => "manage_worksets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description =>
        "Creates, renames, deletes, or sets the active workset (workshared models only). Actions: create, rename, "
        + "delete, set_active. Previews by default: delete in particular reports WHICH workset the elements would "
        + "be moved to, which is the part that cannot be undone by renaming afterwards. Set dryRun=false to apply. "
        + "(Opening/closing worksets on a live document is a Revit UI operation with no API — not exposed.)";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        if (!doc.IsWorkshared)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Project is not workshared — worksets are not available",
                suggestion: "Worksets require a workshared model. Use get_project_info to check isWorkshared.");

        var action = (input["action"]?.Value<string>() ?? "").ToLowerInvariant();

        try
        {
            return action switch
            {
                "create"     => CreateWorkset(doc, input, session),
                "rename"     => RenameWorkset(doc, input, session),
                "delete"     => DeleteWorkset(doc, input, session),
                "set_active" => SetActiveWorkset(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: create, rename, delete, set_active")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_worksets could not manage worksets: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> CreateWorkset(Document doc, JObject input, RiveTTSession session)
    {
        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "name is required for create");

        if (!WorksetTable.IsWorksetNameUnique(doc, name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A workset named '{name}' already exists");

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would create the workset '{name}'.",
                new { action = "create", name });

        using var tx = new Transaction(doc, "RiveTT: Create Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var workset = Workset.Create(doc, name);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            action = "create",
            worksetId = workset.Id.IntegerValue,
            name = workset.Name
        });
    }

    private static RiveTTResult<object> RenameWorkset(Document doc, JObject input, RiveTTSession session)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "newName is required for rename");

        if (!WorksetTable.IsWorksetNameUnique(doc, newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A workset named '{newName}' already exists");

        var oldName = workset!.Name;

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would rename the workset '{oldName}' to '{newName}'.",
                new { action = "rename", worksetId = workset.Id.IntegerValue, oldName, newName });
        using var tx = new Transaction(doc, "RiveTT: Rename Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        WorksetTable.RenameWorkset(doc, workset.Id, newName);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "rename", worksetId = workset.Id.IntegerValue, oldName, newName });
    }

    private static RiveTTResult<object> DeleteWorkset(Document doc, JObject input, RiveTTSession session)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        if (workset!.Kind != WorksetKind.UserWorkset)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Only user worksets can be deleted");

        // Elements on a deleted workset must move somewhere; default to another user workset.
        var fallback = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
            .FirstOrDefault(w => w.Id != workset.Id);
        if (fallback == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Cannot delete the only user workset");

        var name = workset.Name;

        // The destination workset is chosen HERE, not by the caller, and the move is not
        // reversible by recreating the workset afterwards. Naming it before the fact is
        // the whole point of previewing this action.
        if (ToolHelpers.GetDryRun(input))
        {
            var affected = new FilteredElementCollector(doc)
                .WherePasses(new ElementWorksetFilter(workset.Id, inverted: false))
                .GetElementCount();
            return ChangePreview.Declared(
                $"DryRun: would delete the workset '{name}' and move its {affected} element(s) "
                + $"to '{fallback.Name}'.",
                new
                {
                    action = "delete",
                    deletedWorkset = name,
                    elementsMovedTo = fallback.Name,
                    elementsAffected = affected
                });
        }
        using var tx = new Transaction(doc, "RiveTT: Delete Workset");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var settings = new DeleteWorksetSettings(DeleteWorksetOption.MoveElementsToWorkset, fallback.Id);
        WorksetTable.DeleteWorkset(doc, workset.Id, settings);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            action = "delete",
            deletedWorkset = name,
            elementsMovedTo = fallback.Name
        });
    }

    private static RiveTTResult<object> SetActiveWorkset(Document doc, JObject input)
    {
        var (workset, error) = ResolveWorkset(doc, input);
        if (error != null) return error;

        // Not a model change, but it decides the workset of everything created next, so a
        // caller previewing a sequence must be able to preview this step as well. And the
        // tool declares supportsDryRun: honouring it on some actions only is the defect
        // the router gate exists to prevent.
        if (ToolHelpers.GetDryRun(input))
        {
            var currentId = doc.GetWorksetTable().GetActiveWorksetId();
            var current = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Id == currentId);
            return ChangePreview.Declared(
                $"DryRun: would make '{workset!.Name}' the active workset"
                + (current != null ? $" instead of '{current.Name}'." : "."),
                new
                {
                    action = "set_active",
                    worksetId = workset.Id.IntegerValue,
                    name = workset.Name,
                    currentActiveWorkset = current?.Name
                });
        }

        doc.GetWorksetTable().SetActiveWorksetId(workset!.Id);
        return RiveTTResult<object>.Ok(new { action = "set_active", worksetId = workset.Id.IntegerValue, name = workset.Name });
    }

    /// <summary>Resolves a workset by worksetId (int) or name from the input.</summary>
    private static (Workset?, RiveTTResult<object>?) ResolveWorkset(Document doc, JObject input)
    {
        var worksetIdInt = input["worksetId"]?.Value<int?>();
        var name = input["name"]?.Value<string>();

        var all = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToList();

        Workset? workset = null;
        if (worksetIdInt.HasValue)
            workset = all.FirstOrDefault(w => w.Id.IntegerValue == worksetIdInt.Value);
        if (workset == null && !string.IsNullOrEmpty(name))
            workset = all.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (workset == null)
            return (null, RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                "Workset not found", suggestion: "Provide a valid worksetId or name (list them with list_worksets)"));

        return (workset, null);
    }
}
