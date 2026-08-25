using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Workflows;

/// <summary>
/// Auto-creates sheets with title blocks from a sheet definition list.
/// </summary>
[ToolSafety(false, false)]
public class WorkflowSheetSetTool : ICortexTool
{
    public string Name => "workflow_sheet_set";
    public string Category => "Workflows";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Auto-creates sheets with title blocks from a sheet definition list, and places each definition's "
        + "viewIds on its sheet, centred in the title block's real frame. Previews by default: set "
        + "dryRun=false to create.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheets = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var titleBlockName = input["titleBlockName"]?.Value<string>();
        var dryRun = ToolHelpers.GetDryRun(input);

        if (sheets.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheets array required");

        try
        {
            // Find title block
            var tbs = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            var titleBlock = !string.IsNullOrEmpty(titleBlockName)
                ? tbs.FirstOrDefault(t => t.Name.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase)
                    || t.FamilyName.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase))
                  ?? tbs.FirstOrDefault()
                : tbs.FirstOrDefault();

            if (titleBlock == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No title block found");

            if (dryRun)
            {
                var planned = sheets.Select(sd => (object)new
                {
                    number = sd["number"]?.Value<string>(),
                    name = sd["name"]?.Value<string>(),
                    viewCount = (sd["viewIds"]?.ToObject<List<long>>() ?? new List<long>()).Count
                }).ToList();

                return CortexResult<object>.Ok(new
                {
                    dryRun = true,
                    message = $"DryRun: {sheets.Count} sheet(s) would be created on title block "
                            + $"'{titleBlock.Name}'. Set dryRun=false to execute.",
                    titleBlock = titleBlock.Name,
                    sheets = planned
                });
            }

            var results = new List<object>();
            // Counted here, not read back off the result objects: the failure shape has no
            // view fields at all, and a dynamic read of a missing member throws.
            var requestedViews = 0;
            var placedTotal = 0;

            using var tx = new Transaction(doc, "RiveTT: Workflow Sheet Set");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var sd in sheets)
            {
                var number = sd["number"]?.Value<string>();
                var name = sd["name"]?.Value<string>();
                // viewIds was published in the spec and never read: every sheet in the set
                // came out empty, with a success response and no warning. The whole point of
                // the workflow is that the views land on the sheets.
                var viewIds = sd["viewIds"]?.ToObject<List<long>>() ?? new List<long>();

                // Counted once, before the try, so a failure partway cannot count the same
                // sheet's views twice — the reconciliation below is the only signal that
                // views went missing.
                requestedViews += viewIds.Count;

                try
                {
                    var sheet = ViewSheet.Create(doc, titleBlock.Id);
                    if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
                    if (!string.IsNullOrEmpty(name)) sheet.Name = name;

                    var frame = SheetFrame.Measure(doc, sheet);
                    var cells = SheetFrame.Subdivide(frame, viewIds.Count);
                    var placedViews = new List<SheetFrame.Placement>();
                    var placedHere = 0;
                    for (var i = 0; i < viewIds.Count; i++)
                    {
                        var placement = SheetFrame.PlaceCentred(
                            doc, sheet, ToolHelpers.ToElementId(viewIds[i]), cells[i]);
                        placedViews.Add(placement);
                        if (SheetFrame.WasPlaced(placement)) placedHere++;
                    }

                    placedTotal += placedHere;

                    results.Add(new
                    {
                        sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                        number = sheet.SheetNumber,
                        name = sheet.Name,
                        success = true,
                        frameOutlineMm = SheetFrame.Describe(frame),
                        requestedViewCount = viewIds.Count,
                        placedCount = placedHere,
                        placedViews
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { number, name, success = false, reason = ex.Message });
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                createdCount = results.Count(r => ((dynamic)r).success),
                titleBlock = titleBlock.Name,
                requestedViewCount = requestedViews,
                placedViewCount = placedTotal,
                warnings = requestedViews == placedTotal
                    ? Array.Empty<string>()
                    : new[]
                    {
                        $"{requestedViews - placedTotal} of {requestedViews} requested view(s) were not "
                        + "placed: Revit refuses a view already placed on another sheet. See each sheet's "
                        + "placedViews[].reason."
                    },
                sheets = results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
