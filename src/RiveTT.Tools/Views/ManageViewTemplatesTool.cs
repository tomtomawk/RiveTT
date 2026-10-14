using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Views;

/// <summary>
/// Lists, duplicates, deletes, or renames view templates.
/// </summary>
[ReadOnlyActions("list", "list")]
[ToolSafety(false, true, supportsDryRun: true)]
public class ManageViewTemplatesTool : IRiveTTTool
{
    public string Name => "manage_view_templates";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Lists, duplicates, deletes, renames, or creates (create_from_view) view templates. Revit refuses duplicate on "
        + "some templates (\"View cannot be duplicated\"): create_from_view builds one from a view's settings instead. "
        + "The write actions preview by default: deleting a "
        + "template silently unassigns it from every view that used it, which is the part the caller does not see "
        + "coming. Set dryRun=false to apply.";
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
                "list" => ListTemplates(doc, input),
                "duplicate" => DuplicateTemplate(doc, input),
                "delete" => DeleteTemplate(doc, input, session),
                "rename" => RenameTemplate(doc, input),
                "create_from_view" => CreateFromView(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, duplicate, delete, rename, create_from_view")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_view_templates could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> ListTemplates(Document doc, JObject input)
    {
        var filterViewType = input["filterViewType"]?.Value<string>();
        var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.IsTemplate);
        if (!string.IsNullOrEmpty(filterViewType))
            templates = templates.Where(v => v.ViewType.ToString().Equals(filterViewType, StringComparison.OrdinalIgnoreCase));

        var result = templates.Select(v => new
        {
            id = ToolHelpers.GetElementIdValue(v.Id), name = v.Name, viewType = v.ViewType.ToString()
        }).ToList();
        return RiveTTResult<object>.Ok(new { templateCount = result.Count, templates = result });
    }

    private static RiveTTResult<object> DuplicateTemplate(Document doc, JObject input)
    {
        var templateIds = input["templateIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (templateIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "templateIds required");

        var results = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);
        using var tx = new Transaction(doc, "RiveTT: Duplicate View Templates");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        foreach (var tid in templateIds)
        {
            var template = doc.GetElement(new ElementId(tid)) as View;
            if (template == null || !template.IsTemplate)
            {
                results.Add(new { originalId = tid, success = false,
                    message = template == null ? "Element not found" : "Not a view template" });
                continue;
            }

            ElementId newId;
            try
            {
                newId = template.Duplicate(ViewDuplicateOption.Duplicate);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.InvalidOperationException
                                          or Autodesk.Revit.Exceptions.ArgumentException)
            {
                results.Add(new { originalId = tid, success = false,
                    message = $"Cannot duplicate template '{template.Name}': {ex.Message}. "
                            + "Use action create_from_view with a view that carries these settings." });
                continue;
            }
            var newView = doc.GetElement(newId) as View;
            if (newView != null)
                results.Add(new { originalId = tid, newId = ToolHelpers.GetElementIdValue(newId), newName = newView.Name });
        }
        // Revit picks the copy's name itself ("Plan 1 copie 1"), and refuses some templates
        // outright. Duplicating for real and rolling back reports both.
        if (dryRun)
        {
            ChangePreview.Rollback(tx);
            return ChangePreview.Probed(
                $"DryRun: would duplicate {results.Count} view template(s).",
                new { duplicatedCount = results.Count, templates = results });
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { duplicatedCount = results.Count, templates = results });
    }

    /// <summary>
    /// View.CreateViewTemplate: a template carrying the view's current settings. Revit refuses
    /// Duplicate on some templates ("View cannot be duplicated"), and this is the supported way
    /// to get a template with the same settings (field report ILO 13/10/2026).
    /// </summary>
    private static RiveTTResult<object> CreateFromView(Document doc, JObject input)
    {
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var newName = input["newName"]?.Value<string>();
        if (viewId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "viewId is required for create_from_view",
                suggestion: "Pass the id of the view whose settings the new template must carry.");

        var view = doc.GetElement(new ElementId(viewId)) as View;
        if (view == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "View not found");
        if (view.IsTemplate)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{view.Name}' is already a view template.",
                suggestion: "Pass a regular view as viewId.");

        var taken = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Any(v => v.IsTemplate && !string.IsNullOrEmpty(newName)
                      && string.Equals(v.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (taken)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"A view template named '{newName}' already exists.",
                suggestion: "Pick another newName, or leave it out and Revit names the template itself.");

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would create a view template from '{view.Name}'"
                + (string.IsNullOrEmpty(newName) ? " (Revit chooses the name)." : $" named '{newName}'."),
                new { viewId, viewName = view.Name, viewType = view.ViewType.ToString(), newName });

        using var tx = new Transaction(doc, "RiveTT: Create View Template From View");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        var template = view.CreateViewTemplate();
        if (!string.IsNullOrEmpty(newName))
            template.Name = newName;
        var templateId = ToolHelpers.GetElementIdValue(template.Id);
        var templateName = template.Name;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new
        {
            templateId, templateName, createdFromViewId = viewId, viewType = view.ViewType.ToString()
        });
    }

    private static RiveTTResult<object> DeleteTemplate(Document doc, JObject input, RiveTTSession session)
    {
        var templateIds = input["templateIds"]?.ToObject<List<long>>() ?? new List<long>();

        // A deleted template is dropped from every view that used it, and those views keep
        // the graphics they had -- so nothing looks wrong afterwards, and the link is gone.
        // Naming the affected views is the whole value of this preview.
        if (ToolHelpers.GetDryRun(input))
        {
            var resolved = templateIds
                .Select(tid => doc.GetElement(new ElementId(tid)) as View)
                .Where(v => v != null && v!.IsTemplate)
                .ToList();
            var usedBy = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && resolved.Any(t => t!.Id == v.ViewTemplateId))
                .Select(v => v.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return DeletionPreview.Build(doc, resolved.Select(v => v!.Id).ToList(),
                $"{resolved.Count} view template(s)",
                new
                {
                    action = "delete",
                    templates = resolved.Select(v => new
                    {
                        id = ToolHelpers.GetElementIdValue(v!.Id), name = v.Name
                    }).ToList(),
                    viewsThatWouldLoseTheirTemplate = usedBy.Count,
                    viewNames = usedBy.Take(50).ToList()
                });
        }

        using var tx = new Transaction(doc, "RiveTT: Delete View Templates");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        int deleted = 0;
        foreach (var tid in templateIds)
        {
            var template = doc.GetElement(new ElementId(tid)) as View;
            if (template != null && template.IsTemplate) { doc.Delete(template.Id); deleted++; }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { deletedCount = deleted });
    }

    private static RiveTTResult<object> RenameTemplate(Document doc, JObject input)
    {
        var templateId = input["templateId"]?.Value<long>() ?? 0;
        var newName = input["newName"]?.Value<string>();
        if (templateId <= 0 || string.IsNullOrEmpty(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "templateId and newName required");

        var template = doc.GetElement(new ElementId(templateId)) as View;
        if (template == null || !template.IsTemplate)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "View template not found");

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would rename the view template '{template.Name}' to '{newName}'.",
                new { oldName = template.Name, newName, templateId });

        using var tx = new Transaction(doc, "RiveTT: Rename View Template");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        var oldName = template.Name;
        template.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { oldName, newName, templateId });
    }
}
