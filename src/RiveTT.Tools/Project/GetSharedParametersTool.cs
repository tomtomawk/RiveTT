using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists all project parameters (shared and project-specific) with their
/// bindings, parameter types, and applicable categories.
/// </summary>
[ToolSafety(true, false)]
public class GetSharedParametersTool : IRiveTTTool
{
    public string Name => "list_shared_parameters";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all project parameters (shared and project-specific) with their bindings, parameter types, and applicable categories.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var categoryFilter = input["categoryFilter"]?.Value<string>() ?? "";

        try
        {
            var parameters = new List<object>();
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key;
                var binding = iterator.Current as ElementBinding;
                if (binding == null) continue;

                var categories = binding.Categories.Cast<Category>()
                    .Select(c => c.Name)
                    .ToList();

                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !categories.Any(c => c.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                var isShared = definition is ExternalDefinition;
                var guid = isShared ? ((ExternalDefinition)definition).GUID.ToString() : "";
                var isInstance = binding is InstanceBinding;

                var paramType = definition.GetDataType()?.TypeId ?? "";
                var paramGroup = definition.GetGroupTypeId()?.TypeId ?? "";

                parameters.Add(new
                {
                    name        = definition.Name,
                    isShared,
                    guid,
                    isInstance,
                    parameterType  = paramType,
                    parameterGroup = paramGroup,
                    categories
                });
            }

            return RiveTTResult<object>.Ok(new
            {
                parameterCount = parameters.Count,
                parameters
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"list_shared_parameters could not get shared parameters: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
