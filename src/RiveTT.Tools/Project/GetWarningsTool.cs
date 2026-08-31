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
/// Retrieves all warnings/errors in the model with optional severity and
/// description filtering. Useful for model health auditing.
/// </summary>
[ToolSafety(true, false)]
public class GetWarningsTool : IRiveTTTool
{
    public string Name => "list_warnings";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Retrieves all warnings/errors in the model with optional severity and description filtering. Useful for model health auditing.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var severityFilter = input["severityFilter"]?.Value<string>() ?? "All";
        var maxWarnings    = input["maxWarnings"]?.Value<int>() ?? 500;
        var categoryFilter = input["categoryFilter"]?.Value<string>() ?? "";

        try
        {
            var allWarnings = doc.GetWarnings();
            var warnings = new List<object>();
            var severityCounts = new Dictionary<string, int>
            {
                { "Error", 0 },
                { "Warning", 0 }
            };

            int count = 0;
            foreach (var warning in allWarnings)
            {
                if (count >= maxWarnings) break;

                var severity = warning.GetSeverity().ToString();

                if (severityFilter != "All" && severity != severityFilter)
                    continue;

                var description = warning.GetDescriptionText();

                if (!string.IsNullOrEmpty(categoryFilter) &&
                    description.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (severityCounts.ContainsKey(severity))
                    severityCounts[severity]++;

                var failingIds = warning.GetFailingElements().Select(id =>
                {
                    return id.Value;
                }).ToList();

                var additionalIds = warning.GetAdditionalElements().Select(id =>
                {
                    return id.Value;
                }).ToList();

                warnings.Add(new
                {
                    severity,
                    description,
                    failingElementIds = failingIds,
                    additionalElementIds = additionalIds
                });

                count++;
            }

            return RiveTTResult<object>.Ok(new
            {
                totalWarnings    = allWarnings.Count,
                returnedWarnings = warnings.Count,
                severityCounts,
                warnings
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"list_warnings could not get warnings: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
