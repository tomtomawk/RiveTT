using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// The one implementation behind duplicate_sheet_with_content and duplicate_sheet_with_views.
///
/// The two tools had drifted (field report ILO 13/10/2026): the first copied annotations and
/// exposed newNumber/newName but not the view duplication option, the second exposed the
/// option but no newNumber (so it always failed with "Sheet number is already in use"), and
/// NEITHER copied the secondary title blocks or the sheet's own parameters (Dossier,
/// Indice_manuel...). They now share this code and differ only by one default: whether loose
/// sheet annotations are copied (<c>copyAnnotationsDefault</c>).
/// </summary>
internal static class SheetDuplicator
{
    private static readonly BuiltInCategory[] SheetAnnotationCategories =
    {
        BuiltInCategory.OST_TextNotes,
        BuiltInCategory.OST_GenericAnnotation,
        BuiltInCategory.OST_DetailComponents,
        BuiltInCategory.OST_Lines
    };

    public static ViewDuplicateOption ParseViewOption(string? text)
        => (text ?? "DuplicateWithDetailing").Replace("_", "").Trim().ToLowerInvariant() switch
        {
            "duplicate" => ViewDuplicateOption.Duplicate,
            "duplicateasdependent" or "asdependent" or "dependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.WithDetailing
        };

    public static RiveTTResult<object> Run(JObject input, Document doc, string toolName, bool copyAnnotationsDefault, bool dryRun)
    {
        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var copies = input["copies"]?.Value<int>() ?? 1;
        var duplicateViews = input["duplicateViews"]?.Value<bool>() ?? true;
        var keepLegends = input["keepLegends"]?.Value<bool>() ?? true;
        var keepSchedules = input["keepSchedules"]?.Value<bool>() ?? true;
        var copyRevisions = input["copyRevisions"]?.Value<bool>() ?? false;
        var copyAnnotations = input["copyAnnotations"]?.Value<bool>() ?? copyAnnotationsDefault;
        var sheetNumberPrefix = input["sheetNumberPrefix"]?.Value<string>()
            ?? input["newSheetNumberPrefix"]?.Value<string>() ?? "";
        var sheetNumberSuffix = input["sheetNumberSuffix"]?.Value<string>() ?? "";
        var newNumber = input["newNumber"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();
        var viewDuplicateOptionText = input["viewDuplicateOption"]?.Value<string>();
        var viewDuplicateOption = ParseViewOption(viewDuplicateOptionText);
        // null = the historical " - <sheet number>" suffix; "" = keep the name Revit gives the copy.
        var viewNameSuffixInput = input["viewNameSuffix"]?.Value<string>();

        if (sheetId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sheetId is required");
        if (copies < 1)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "copies must be at least 1");

        try
        {
            var sourceSheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
            if (sourceSheet == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Sheet not found");

            var sourceTitleBlocks = TitleBlocksOf(doc, sourceSheet);
            var titleBlockTypeId = sourceTitleBlocks.FirstOrDefault()?.GetTypeId()
                ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .FirstOrDefault()?.Id
                ?? ElementId.InvalidElementId;

            var viewportData = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Select(vp => new { vp.ViewId, Center = vp.GetBoxCenter(), View = doc.GetElement(vp.ViewId) as View })
                .Where(vp => vp.View != null)
                .ToList();

            var scheduleInstances = new FilteredElementCollector(doc, sourceSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .Select(si => new { si.ScheduleId, si.Point })
                .ToList();

            var results = new List<object>();
            using var tx = new Transaction(doc, $"RiveTT: {toolName}");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

            for (var i = 0; i < copies; i++)
            {
                var newSheet = ViewSheet.Create(doc, titleBlockTypeId);
                var suffix = copies > 1 ? $"-{i + 1:D2}" : "";
                newSheet.SheetNumber = !string.IsNullOrWhiteSpace(newNumber)
                    ? $"{newNumber}{suffix}"
                    : $"{sheetNumberPrefix}{sourceSheet.SheetNumber}{sheetNumberSuffix}{suffix}";
                try { newSheet.Name = !string.IsNullOrWhiteSpace(newName) ? newName! : sourceSheet.Name; }
                catch { /* duplicate name: Revit keeps its default */ }

                var sheetParametersCopied = CopySheetParameters(sourceSheet, newSheet);
                try
                {
                    if (sourceSheet.SheetCollectionId != ElementId.InvalidElementId)
                        newSheet.SheetCollectionId = sourceSheet.SheetCollectionId;
                }
                catch { /* assembly sheets and some templates refuse a collection */ }

                var titleBlocksCopied = CopyTitleBlocks(doc, sourceSheet, newSheet, sourceTitleBlocks);

                if (copyRevisions)
                {
                    var revIds = sourceSheet.GetAllRevisionIds();
                    if (revIds.Count > 0) newSheet.SetAdditionalRevisionIds(revIds);
                }

                var viewportCount = 0;
                var newViewportIds = new List<long>();
                foreach (var vpData in viewportData)
                {
                    var view = vpData.View!;
                    var isLegend = view.ViewType == ViewType.Legend;
                    var isSchedule = view.ViewType == ViewType.Schedule;

                    if (isLegend && !keepLegends) continue;
                    if (isSchedule && !keepSchedules) continue;

                    if (isLegend || !duplicateViews)
                    {
                        if (Viewport.CanAddViewToSheet(doc, newSheet.Id, vpData.ViewId))
                        {
                            var vp = Viewport.Create(doc, newSheet.Id, vpData.ViewId, vpData.Center);
                            newViewportIds.Add(ToolHelpers.GetElementIdValue(vp.Id));
                            viewportCount++;
                        }
                    }
                    else
                    {
                        var newView = doc.GetElement(view.Duplicate(viewDuplicateOption)) as View;
                        if (newView == null) continue;
                        var wanted = viewNameSuffixInput == null
                            ? $"{view.Name} - {newSheet.SheetNumber}"
                            : $"{view.Name}{viewNameSuffixInput}";
                        if (wanted != newView.Name)
                        {
                            try { newView.Name = wanted; } catch { /* keep Revit's own copy name */ }
                        }
                        var vp = Viewport.Create(doc, newSheet.Id, newView.Id, vpData.Center);
                        newViewportIds.Add(ToolHelpers.GetElementIdValue(vp.Id));
                        viewportCount++;
                    }
                }

                var skippedSchedules = new List<object>();
                if (keepSchedules)
                {
                    foreach (var si in scheduleInstances)
                    {
                        try { ScheduleSheetInstance.Create(doc, newSheet.Id, si.ScheduleId, si.Point); }
                        catch (Exception ex)
                        {
                            skippedSchedules.Add(new
                            {
                                scheduleId = ToolHelpers.GetElementIdValue(si.ScheduleId),
                                reason = ex.Message
                            });
                        }
                    }
                }

                var annotationCount = copyAnnotations ? CopySheetAnnotations(doc, sourceSheet, newSheet) : 0;

                results.Add(new
                {
                    sheetId = ToolHelpers.GetElementIdValue(newSheet.Id),
                    number = newSheet.SheetNumber,
                    name = newSheet.Name,
                    viewportCount,
                    viewportIds = newViewportIds,
                    titleBlocksCopied,
                    sheetParametersCopied,
                    annotationCount,
                    skippedScheduleCount = skippedSchedules.Count,
                    skippedSchedules
                });
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws.
            var payload = new
            {
                duplicatedCount = results.Count,
                viewDuplicateOption = viewDuplicateOption.ToString(),
                sourceTitleBlockCount = sourceTitleBlocks.Count,
                sheets = results
            };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    payload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry. \"Sheet number is already in use\": "
                              + "pass newNumber (or sheetNumberPrefix/sheetNumberSuffix).");

            return RiveTTResult<object>.Ok(payload);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"{toolName} could not complete: {ex.Message}",
                suggestion: ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase)
                    ? "The new sheet number is taken: pass newNumber, or sheetNumberPrefix/sheetNumberSuffix."
                    : "Unexpected failure, not a rejected input: the wording above is Revit own. "
                      + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                      + "call if it covered many elements. The full call, its duration and this error are "
                      + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static List<Element> TitleBlocksOf(Document doc, ViewSheet sheet)
        => new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .ToList();

    /// <summary>
    /// The first title block was already created with the sheet (it carries the type): it only
    /// needs its position and parameters. EVERY OTHER title block — the small cartouche next
    /// to the frame — is copied across, which neither tool did.
    /// </summary>
    private static int CopyTitleBlocks(Document doc, ViewSheet source, ViewSheet dest, List<Element> sourceTitleBlocks)
    {
        if (sourceTitleBlocks.Count == 0) return 0;
        var copied = 0;

        var created = TitleBlocksOf(doc, dest).FirstOrDefault();
        if (created != null)
        {
            CopyParameters(sourceTitleBlocks[0], created, excluded: null);
            try
            {
                if (sourceTitleBlocks[0].Location is LocationPoint from && created.Location is LocationPoint to)
                {
                    var delta = from.Point - to.Point;
                    if (delta.GetLength() > 1e-6)
                        ElementTransformUtils.MoveElement(doc, created.Id, delta);
                }
            }
            catch { /* position is cosmetic: the title block itself is there */ }
            copied++;
        }

        if (sourceTitleBlocks.Count > 1)
        {
            try
            {
                var extra = sourceTitleBlocks.Skip(1).Select(e => e.Id).ToList();
                var done = ElementTransformUtils.CopyElements(source, extra, dest, Transform.Identity, new CopyPasteOptions());
                copied += done?.Count ?? 0;
            }
            catch { /* reported through titleBlocksCopied being lower than sourceTitleBlockCount */ }
        }
        return copied;
    }

    /// <summary>
    /// Every writable sheet parameter by name, EXCEPT the number and the name the caller
    /// controls — that is what carries Dossier, Indice_manuel and the like.
    /// </summary>
    private static int CopySheetParameters(ViewSheet source, ViewSheet dest)
    {
        var excluded = new HashSet<BuiltInParameter> { BuiltInParameter.SHEET_NUMBER, BuiltInParameter.SHEET_NAME };
        return CopyParameters(source, dest, excluded);
    }

    private static int CopyParameters(Element source, Element target, HashSet<BuiltInParameter>? excluded)
    {
        var count = 0;
        foreach (Parameter srcParam in source.Parameters)
        {
            if (srcParam.IsReadOnly || !srcParam.HasValue) continue;
            if (excluded != null && srcParam.Definition is InternalDefinition internalDefinition
                && excluded.Contains(internalDefinition.BuiltInParameter))
                continue;
            var tgtParam = target.LookupParameter(srcParam.Definition.Name);
            if (tgtParam == null || tgtParam.IsReadOnly || tgtParam.StorageType != srcParam.StorageType) continue;

            try
            {
                switch (srcParam.StorageType)
                {
                    case StorageType.String:
                        var text = srcParam.AsString();
                        if (text != null) { tgtParam.Set(text); count++; }
                        break;
                    case StorageType.Integer: tgtParam.Set(srcParam.AsInteger()); count++; break;
                    case StorageType.Double: tgtParam.Set(srcParam.AsDouble()); count++; break;
                    case StorageType.ElementId: tgtParam.Set(srcParam.AsElementId()); count++; break;
                }
            }
            catch { /* skip unwritable params */ }
        }
        return count;
    }

    /// <summary>
    /// Loose annotation elements owned directly by the source sheet. Excludes viewports,
    /// schedule instances and title blocks, which are handled separately.
    /// </summary>
    private static int CopySheetAnnotations(Document doc, ViewSheet sourceSheet, ViewSheet destSheet)
    {
        var annoIds = new FilteredElementCollector(doc, sourceSheet.Id)
            .WhereElementIsNotElementType()
            .Where(e => e.Category != null
                && SheetAnnotationCategories.Contains((BuiltInCategory)ToolHelpers.GetElementIdValue(e.Category.Id)))
            .Select(e => e.Id)
            .ToList();
        if (annoIds.Count == 0) return 0;

        try
        {
            var copied = ElementTransformUtils.CopyElements(
                sourceSheet, annoIds, destSheet, Transform.Identity, new CopyPasteOptions());
            return copied?.Count ?? 0;
        }
        catch
        {
            // Some annotation kinds resist cross-view copy; skip rather than fail the duplicate.
            return 0;
        }
    }
}
