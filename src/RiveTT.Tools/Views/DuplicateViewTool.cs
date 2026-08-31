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
/// Duplicates one or more views with optional naming prefix/suffix.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class DuplicateViewTool : IRiveTTTool
{
    public string Name => "duplicate_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates one or more views with optional naming prefix/suffix.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var legacyViewId = input["viewId"]?.Value<long?>() ?? 0;
        if (viewIds.Count == 0 && legacyViewId > 0)
            viewIds.Add(legacyViewId);
        if (viewIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "viewIds array is required");

        var duplicateOption = input["duplicateOption"]?.Value<string>() ?? "Duplicate";
        var prefix = input["newNamePrefix"]?.Value<string>() ?? "";
        var suffix = input["newNameSuffix"]?.Value<string>() ?? "";

        var option = duplicateOption.ToLowerInvariant() switch
        {
            "withddetailing" or "withdetailing" or "duplicate_with_detailing" => ViewDuplicateOption.WithDetailing,
            "asdependent" or "dependent" or "duplicate_as_dependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.Duplicate
        };

        try
        {
            var results = new List<object>();
            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Duplicate Views");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var vid in viewIds)
            {
                var view = doc.GetElement(new ElementId(vid)) as View;
                if (view == null) continue;

                var newId = view.Duplicate(option);
                var newView = doc.GetElement(newId) as View;
                if (newView != null)
                {
                    if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
                    {
                        try { newView.Name = $"{prefix}{view.Name}{suffix}"; }
                        catch { /* name conflict, keep auto-generated */ }
                    }
                    results.Add(new
                    {
                        originalViewId = vid,
                        newViewId = ToolHelpers.GetElementIdValue(newId),
                        newViewName = newView.Name
                    });
                }
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new { duplicatedCount = results.Count, views = results };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    previewPayload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

return RiveTTResult<object>.Ok(previewPayload);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
