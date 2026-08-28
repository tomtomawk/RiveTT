using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Returns metadata about the currently active view (name, type, scale, detail level).
/// </summary>
[ToolSafety(true, false)]
public class GetCurrentViewInfoTool : IRiveTTTool
{
    public string Name => "get_current_view_info";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Returns metadata about the currently active view (name, type, scale, detail level).";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        try
        {
            var activeView = doc.ActiveView;

            return RiveTTResult<object>.Ok(new
            {
                id = activeView.Id.Value,
                uniqueId    = activeView.UniqueId,
                name        = activeView.Name,
                viewType    = activeView.ViewType.ToString(),
                isTemplate  = activeView.IsTemplate,
                scale       = activeView.Scale,
                detailLevel = activeView.DetailLevel.ToString()
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Failed to get current view info: {ex.Message}");
        }
    }
}
