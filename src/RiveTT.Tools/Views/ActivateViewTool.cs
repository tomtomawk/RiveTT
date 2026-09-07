using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Project;

namespace RiveTT.Tools.Views;

[ToolSafety(true, false, supportsDryRun: true)]
public sealed class ActivateViewTool : IRiveTTTool
{
    public string Name => "activate_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Make a view or sheet the active Revit view by ID. Available while RiveTT is locked. "
        + "Templates/internal views are refused. Executes synchronously outside any transaction; returns the actual active view.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var id = input["viewId"]?.Value<long>() ?? 0;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;
        if (id <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "viewId must be a positive Revit view ID.");
        var uiDoc = DocumentLifecycleSupport.ResolveUiApplication(session)?.ActiveUIDocument;
        if (uiDoc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active Revit UI document.");
        var doc = uiDoc.Document;
        if (doc.GetElement(new ElementId(id)) is not View view)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"View {id} does not exist in the active document.");
        if (view.IsTemplate || view.ViewType is ViewType.Internal or ViewType.Undefined or ViewType.ProjectBrowser or ViewType.SystemBrowser)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"View '{view.Name}' is a template or internal view and cannot be activated.");
        if (doc.IsModifiable || doc.IsReadOnly)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Revit cannot change views during a transaction or while the document API context is read-only.");
        var previousId = uiDoc.ActiveView?.Id.Value;
        if (dryRun)
            return RiveTTResult<object>.Ok(new { dryRun = true, mutated = false, viewId = id, viewName = view.Name, previousViewId = previousId });
        try
        {
            uiDoc.ActiveView = view;
            var active = uiDoc.ActiveView;
            if (active?.Id != view.Id)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "Revit did not activate the requested view.");
            uiDoc.RefreshActiveView();
            return RiveTTResult<object>.Ok(new
            {
                viewId = active.Id.Value, viewName = active.Name, viewType = active.ViewType.ToString(),
                previousViewId = previousId, alreadyActive = previousId == active.Id.Value,
                documentTitle = doc.Title
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"activate_view could not activate '{view.Name}': {ex.Message}",
                suggestion: "Finish the active Revit operation, then retry with a non-template view or sheet from the current document.");
        }
    }
}
