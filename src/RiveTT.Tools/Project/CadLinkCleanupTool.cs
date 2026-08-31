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
/// Analyzes and cleans up imported/linked CAD files in the model.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class CadLinkCleanupTool : IRiveTTTool
{
    public string Name => "clean_cad_links";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Analyzes and cleans up imported/linked CAD files in the model. action=list|delete. Delete previews by "
        + "default: the dry run names every import and link it would remove, and probes the real deletion "
        + "cascade. Set dryRun=false to apply.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";
        var deleteImports = input["deleteImports"]?.Value<bool>() ?? false;
        var deleteLinks = input["deleteLinks"]?.Value<bool>() ?? false;
        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();

        try
        {
            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Select(i => new
                {
                    Id = i.Id,
                    Name = i.Name,
                    IsLinked = i.IsLinked,
                    ViewSpecific = i.OwnerViewId != ElementId.InvalidElementId,
                    ViewName = i.OwnerViewId != ElementId.InvalidElementId
                        ? (doc.GetElement(i.OwnerViewId) as View)?.Name
                        : null
                })
                .ToList();

            if (action == "list")
            {
                return RiveTTResult<object>.Ok(new
                {
                    totalCount = imports.Count,
                    importCount = imports.Count(i => !i.IsLinked),
                    linkCount = imports.Count(i => i.IsLinked),
                    items = imports.Select(i => new
                    {
                        id = ToolHelpers.GetElementIdValue(i.Id),
                        name = i.Name,
                        type = i.IsLinked ? "link" : "import",
                        viewSpecific = i.ViewSpecific,
                        viewName = i.ViewName
                    }).ToList()
                });
            }

            // Delete action — resolve the target set BEFORE opening the transaction so the
            // confirmation dialog reports an accurate count (ultrareview C7).
            var toDelete = imports.AsEnumerable();
            if (elementIds.Count > 0)
            {
                var idSet = elementIds.ToHashSet();
                toDelete = toDelete.Where(i => idSet.Contains(ToolHelpers.GetElementIdValue(i.Id)));
            }
            else
            {
                // H32: the old two-WHERE chain contradicted itself when both flags were
                // false (keep-links AND keep-imports → empty set, silent zero-delete).
                // Build an inclusive predicate instead: keep an item only if its kind was
                // explicitly requested for deletion.
                if (!deleteImports && !deleteLinks)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        "Nothing selected to delete: set deleteImports and/or deleteLinks to true, or pass explicit elementIds.",
                        suggestion: "deleteImports=true removes CAD imports; deleteLinks=true removes CAD links.");

                toDelete = toDelete.Where(i =>
                    (deleteLinks && i.IsLinked) || (deleteImports && !i.IsLinked));
            }

            var targets = toDelete.ToList();

            // A DWG import can carry hundreds of view-specific instances; naming them one by
            // one is what stops "deletedCount: 312" from being the first time the caller
            // learns the scope.
            if (ToolHelpers.GetDryRun(input))
                return DeletionPreview.Build(doc, targets.Select(i => i.Id).ToList(),
                    $"{targets.Count} CAD import(s)/link(s)",
                    new
                    {
                        action = "delete",
                        wouldDeleteCount = targets.Count,
                        items = targets.Take(100).Select(i => new
                        {
                            id = ToolHelpers.GetElementIdValue(i.Id),
                            name = i.Name,
                            type = i.IsLinked ? "link" : "import",
                            viewSpecific = i.ViewSpecific,
                            viewName = i.ViewName
                        }).ToList()
                    });

            using var tx = new Transaction(doc, "RiveTT: CAD Link Cleanup");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            int deleted = 0;
            foreach (var item in targets)
            {
                try { doc.Delete(item.Id); deleted++; } catch { }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return RiveTTResult<object>.Ok(new { action = "delete", deletedCount = deleted });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"clean_cad_links could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
