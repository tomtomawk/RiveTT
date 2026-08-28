using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Caching;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists worksets with open/close status and ownership info.
/// Only available for workshared documents (IsDynamic = true).
/// </summary>
[ToolSafety(true, false)]
public class GetWorksetsTool : IRiveTTTool, ICacheableTool
{
    public string Name => "list_worksets";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => true;
    public string Description => "Lists worksets with open/close status and ownership info. Only available for workshared documents (IsDynamic = true).";
    // Transaction scope: ownership can change after a sync-with-central, so we
    // also drop on Save/Synchronized in addition to model-edit invalidation.
    public CacheScope CacheScope => CacheScope.Transaction;
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        if (!doc.IsWorkshared)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Project is not workshared — worksets are not available",
                suggestion: "Use get_project_info to check isWorkshared before calling this tool");

        var includeSystemWorksets = input["includeSystemWorksets"]?.Value<bool>() ?? false;

        try
        {
            FilteredWorksetCollector wsCollector;
            if (includeSystemWorksets)
                wsCollector = new FilteredWorksetCollector(doc);
            else
                wsCollector = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset);

            var worksets = wsCollector.Select(ws => new
            {
                id                 = ws.Id.IntegerValue,
                name               = ws.Name,
                kind               = ws.Kind.ToString(),
                isOpen             = ws.IsOpen,
                isEditable         = ws.IsEditable,
                owner              = ws.Owner,
                isDefaultWorkset   = ws.IsDefaultWorkset,
                isVisibleByDefault = ws.IsVisibleByDefault
            }).ToList();

            return RiveTTResult<object>.Ok(new
            {
                message = $"Retrieved {worksets.Count} workset(s)",
                worksetCount = worksets.Count,
                worksets
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Failed to get worksets: {ex.Message}");
        }
    }
}
