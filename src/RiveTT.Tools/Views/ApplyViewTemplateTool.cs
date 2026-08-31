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
/// Lists, applies, or removes view templates from views.
/// </summary>
[ToolSafety(false, false)]
public class ApplyViewTemplateTool : IRiveTTTool
{
    public string Name => "apply_view_template";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists, applies, or removes view templates from views.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "apply";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListTemplates(doc),
                "apply" => ApplyTemplate(doc, input, session),
                "remove" => RemoveTemplate(doc, input, session),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, apply, remove")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"apply_view_template could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> ListTemplates(Document doc)
    {
        var templates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .Select(v => new { id = ToolHelpers.GetElementIdValue(v.Id), name = v.Name, viewType = v.ViewType.ToString() })
            .ToList();
        return RiveTTResult<object>.Ok(new { templateCount = templates.Count, templates });
    }

    private static RiveTTResult<object> ApplyTemplate(Document doc, JObject input, RiveTTSession session)
    {
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var templateId = input["templateId"]?.Value<long>() ?? 0;
        var templateName = input["templateName"]?.Value<string>();

        // Resolve template
        View? template = null;
        if (templateId > 0)
        {
            template = doc.GetElement(new ElementId(templateId)) as View;
        }
        if (template == null && !string.IsNullOrEmpty(templateName))
        {
            template = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        }
        if (template == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "View template not found");

        // H14: confirm before changing view templates across a set of views.
        if (!session.RequestConfirmation("apply view template to", viewIds.Count))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Apply View Template");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        int applied = 0;
        foreach (var vid in viewIds)
        {
            var view = doc.GetElement(new ElementId(vid)) as View;
            if (view != null && !view.IsTemplate) { view.ViewTemplateId = template.Id; applied++; }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { appliedCount = applied, templateName = template.Name });
    }

    private static RiveTTResult<object> RemoveTemplate(Document doc, JObject input, RiveTTSession session)
    {
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();

        // H14: confirm before clearing view templates across a set of views.
        if (!session.RequestConfirmation("remove view template from", viewIds.Count))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Remove View Template");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        int removed = 0;
        foreach (var vid in viewIds)
        {
            var view = doc.GetElement(new ElementId(vid)) as View;
            if (view != null && !view.IsTemplate) { view.ViewTemplateId = ElementId.InvalidElementId; removed++; }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { removedCount = removed });
    }
}
