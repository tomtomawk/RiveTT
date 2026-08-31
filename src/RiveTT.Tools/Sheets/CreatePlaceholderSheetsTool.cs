using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// Creates, lists, converts, or deletes placeholder sheets.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class CreatePlaceholderSheetsTool : IRiveTTTool
{
    public string Name => "create_placeholder_sheets";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates, lists, converts, or deletes placeholder sheets. The write actions preview by default. convert "
        + "is the one to preview: it DELETES the placeholder and recreates a real sheet, so the sheet gets a new "
        + "element id and anything referencing the old one stops resolving. Set dryRun=false to apply.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "create";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "create" => CreatePlaceholders(doc, input),
                "list" => ListPlaceholders(doc),
                "convert" => ConvertPlaceholders(doc, input, session),
                "delete" => DeletePlaceholders(doc, input, session),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: create, list, convert, delete")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static RiveTTResult<object> CreatePlaceholders(Document doc, JObject input)
    {
        var sheetsArray = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        if (sheetsArray.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sheets array is required for create");

        var results = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);
        using var tx = new Transaction(doc, "RiveTT: Create Placeholder Sheets");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        foreach (var sd in sheetsArray)
        {
            var number = sd["number"]?.Value<string>();
            var name = sd["name"]?.Value<string>();

            var sheet = ViewSheet.CreatePlaceholder(doc);
            if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
            if (!string.IsNullOrEmpty(name)) sheet.Name = name;

            results.Add(dryRun
                ? (object)new { number = sheet.SheetNumber, name = sheet.Name }
                : new { sheetId = ToolHelpers.GetElementIdValue(sheet.Id), number = sheet.SheetNumber, name = sheet.Name });
        }

        if (dryRun)
        {
            ChangePreview.Rollback(tx);
            return ChangePreview.Probed(
                $"DryRun: would create {results.Count} placeholder sheet(s).",
                new { createdCount = results.Count, sheets = results });
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { createdCount = results.Count, sheets = results });
    }

    private static RiveTTResult<object> ListPlaceholders(Document doc)
    {
        var sheets = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(s => s.IsPlaceholder)
            .Select(s => new { id = ToolHelpers.GetElementIdValue(s.Id), number = s.SheetNumber, name = s.Name })
            .ToList();

        return RiveTTResult<object>.Ok(new { placeholderCount = sheets.Count, sheets });
    }

    private static RiveTTResult<object> ConvertPlaceholders(Document doc, JObject input, RiveTTSession session)
    {
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        var titleBlockId = input["titleBlockId"]?.Value<long>() ?? 0;

        if (sheetIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sheetIds required for convert");

        // H23: converting DELETES the placeholder and recreates a real sheet. The element id
        // changes, so anything holding the old one -- a saved selection, a sheet set, an
        // agent note -- stops resolving. Say it before, not after.
        if (ToolHelpers.GetDryRun(input))
        {
            var resolved = sheetIds
                .Select(sid => doc.GetElement(new ElementId(sid)) as ViewSheet)
                .Where(sheet => sheet != null)
                .ToList();
            return ChangePreview.Declared(
                $"DryRun: would convert {resolved.Count(s => s!.IsPlaceholder)} placeholder sheet(s) into real "
                + "sheets. Each is DELETED and recreated: the number and name are kept, the element id is NOT.",
                new
                {
                    action = "convert",
                    sheets = resolved.Select(s => new
                    {
                        id = ToolHelpers.GetElementIdValue(s!.Id),
                        number = s.SheetNumber,
                        name = s.Name,
                        isPlaceholder = s.IsPlaceholder
                    }).ToList(),
                    idsWillChange = true
                },
                blockers: resolved.Where(s => !s!.IsPlaceholder)
                    .Select(s => $"Sheet '{s!.SheetNumber}' is not a placeholder and would be skipped")
                    .ToList());
        }

        // Resolve title block
        ElementId tbId;
        if (titleBlockId > 0)
        {
            tbId = new ElementId(titleBlockId);
        }
        else
        {
            var firstTb = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .FirstOrDefault();
            tbId = firstTb?.Id ?? ElementId.InvalidElementId;
        }

        if (tbId == ElementId.InvalidElementId)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "No title block found");

        var results = new List<object>();
        using var tx = new Transaction(doc, "RiveTT: Convert Placeholder Sheets");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        foreach (var sid in sheetIds)
        {
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
            if (sheet == null || !sheet.IsPlaceholder)
            {
                results.Add(new { sheetId = sid, success = false, reason = "Not a placeholder sheet" });
                continue;
            }

            var savedNumber = sheet.SheetNumber;
            var savedName = sheet.Name;

            doc.Delete(sheet.Id);
            var newSheet = ViewSheet.Create(doc, tbId);
            newSheet.SheetNumber = savedNumber;
            newSheet.Name = savedName;

            results.Add(new
            {
                sheetId = ToolHelpers.GetElementIdValue(newSheet.Id),
                number = savedNumber,
                name = savedName,
                success = true
            });
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { convertedCount = results.Count(r => ((dynamic)r).success), sheets = results });
    }

    private static RiveTTResult<object> DeletePlaceholders(Document doc, JObject input, RiveTTSession session)
    {
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (sheetIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sheetIds required for delete");

        if (ToolHelpers.GetDryRun(input))
        {
            var resolved = sheetIds
                .Select(sid => doc.GetElement(new ElementId(sid)) as ViewSheet)
                .Where(sheet => sheet != null)
                .ToList();
            return DeletionPreview.Build(doc, resolved.Select(s => s!.Id).ToList(),
                $"{resolved.Count} placeholder sheet(s)",
                new
                {
                    action = "delete",
                    sheets = resolved.Select(s => new
                    {
                        id = ToolHelpers.GetElementIdValue(s!.Id),
                        number = s.SheetNumber,
                        name = s.Name
                    }).ToList()
                });
        }

        using var tx = new Transaction(doc, "RiveTT: Delete Placeholder Sheets");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        int deleted = 0;
        foreach (var sid in sheetIds)
        {
            var sheet = doc.GetElement(new ElementId(sid)) as ViewSheet;
            if (sheet != null) { doc.Delete(sheet.Id); deleted++; }
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(new { deletedCount = deleted });
    }
}
