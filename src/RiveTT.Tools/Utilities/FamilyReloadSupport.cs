using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RiveTT.Tools.Utilities;

/// <summary>What load_from_open must do to release the source family once it is loaded.</summary>
internal enum SourceClosePlan
{
    /// <summary>The source is in the background: Document.Close(false) directly.</summary>
    CloseDirectly,
    /// <summary>The source is the active document: re-activate the target by path, then close.</summary>
    ReactivateTargetThenClose,
    /// <summary>The source is active and the target has no path: Revit cannot re-activate it.</summary>
    TargetNeverSaved
}

/// <summary>
/// Pure decisions behind load_from_open / load (source identity, close plan, id changes), kept free
/// of Revit types so they are unit-tested for real. The Revit-typed glue lives in LoadFamilyTool.
/// </summary>
internal static class FamilyReloadSupport
{
    /// <summary>
    /// Two Document handles denote the same document when they are the same object, or when they
    /// carry the same non-empty PathName, or — for two never-saved documents — the same Title.
    /// The old check was ReferenceEquals alone: it answered "not active" for the active family,
    /// so the close was attempted on the active document and Revit refused it.
    /// </summary>
    internal static bool SameDocument(bool sameReference, string? pathA, string? pathB, string? titleA, string? titleB)
    {
        if (sameReference) return true;
        var aHasPath = !string.IsNullOrWhiteSpace(pathA);
        var bHasPath = !string.IsNullOrWhiteSpace(pathB);
        if (aHasPath && bHasPath) return string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase);
        if (aHasPath != bHasPath) return false;
        return !string.IsNullOrWhiteSpace(titleA) && string.Equals(titleA, titleB, StringComparison.OrdinalIgnoreCase);
    }

    internal static SourceClosePlan DecideClose(bool sourceIsActive, string? targetPath)
    {
        if (!sourceIsActive) return SourceClosePlan.CloseDirectly;
        return string.IsNullOrWhiteSpace(targetPath)
            ? SourceClosePlan.TargetNeverSaved
            : SourceClosePlan.ReactivateTargetThenClose;
    }

    internal static bool IdChanged(long? previousId, long currentId)
        => previousId.HasValue && previousId.Value != currentId;

    internal static string TargetNeverSavedReason(string sourceTitle, string targetTitle)
        => $"'{sourceTitle}' is the active document and '{targetTitle}' was never saved: "
         + "Revit can only re-activate a document by path, and refuses to close the active one. "
         + $"Save '{targetTitle}' (save_as_document), activate it, then close_document on '{sourceTitle}'.";

    internal static bool SameDocument(Document? a, Document? b)
    {
        if (a == null || b == null) return false;
        string? pathA = null, pathB = null, titleA = null, titleB = null;
        try { pathA = a.PathName; pathB = b.PathName; titleA = a.Title; titleB = b.Title; }
        catch { /* an invalid handle is never "the same" document */ return ReferenceEquals(a, b); }
        return SameDocument(ReferenceEquals(a, b), pathA, pathB, titleA, titleB);
    }

    /// <summary>Instances of every type of the family, keyed by type id. One category scan, not one per type.</summary>
    internal static Dictionary<long, int> CountInstancesByType(Document doc, Family family)
    {
        var counts = new Dictionary<long, int>();
        var categoryId = family.FamilyCategory?.Id;
        if (categoryId == null) return counts;
        var symbolIds = new HashSet<long>(family.GetFamilySymbolIds().Select(id => ToolHelpers.GetElementIdValue(id)));
        foreach (var element in new FilteredElementCollector(doc).OfCategoryId(categoryId)
                     .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance)))
        {
            var typeId = ToolHelpers.GetElementIdValue(element.GetTypeId());
            if (!symbolIds.Contains(typeId)) continue;
            counts[typeId] = counts.TryGetValue(typeId, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    /// <summary>What the target held for a family BEFORE it is reloaded, to prove nothing moved.</summary>
    internal sealed record FamilySnapshot(long FamilyId, Dictionary<string, long> TypeIdsByName, int InstanceCount)
    {
        internal static FamilySnapshot? Take(Document doc, Family? family)
        {
            if (family == null) return null;
            var types = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in family.GetFamilySymbolIds())
                if (doc.GetElement(id) is FamilySymbol symbol) types[symbol.Name] = ToolHelpers.GetElementIdValue(id);
            return new FamilySnapshot(ToolHelpers.GetElementIdValue(family.Id), types,
                CountInstancesByType(doc, family).Values.Sum());
        }
    }

    /// <summary>
    /// The block read back from the target after a reload (verification), so the caller does not have to
    /// switch the active document to check the result. Ids and counts are reported as found.
    /// </summary>
    internal static object BuildVerification(Document target, Family family, FamilySnapshot? before)
    {
        var counts = CountInstancesByType(target, family);
        var types = family.GetFamilySymbolIds()
            .Select(id => target.GetElement(id) as FamilySymbol)
            .Where(symbol => symbol != null)
            .Select(symbol =>
            {
                var id = ToolHelpers.GetElementIdValue(symbol!.Id);
                long? previousId = null;
                if (before != null && before.TypeIdsByName.TryGetValue(symbol.Name, out var p)) previousId = p;
                return new
                {
                    id,
                    name = symbol.Name,
                    instanceCount = counts.TryGetValue(id, out var n) ? n : 0,
                    previousId,
                    idPreserved = before == null ? (bool?)null : previousId.HasValue && previousId.Value == id
                };
            })
            .ToList();
        var sameNameCount = new FilteredElementCollector(target).OfClass(typeof(Family)).Cast<Family>()
            .Count(f => string.Equals(f.Name, family.Name, StringComparison.OrdinalIgnoreCase));
        var total = types.Sum(t => t.instanceCount);
        return new
        {
            targetDocument = target.Title,
            familyId = ToolHelpers.GetElementIdValue(family.Id),
            familyCountWithName = sameNameCount,
            types,
            instanceCount = total,
            instanceCountBefore = before?.InstanceCount,
            instancesPreserved = before == null ? (bool?)null : total == before.InstanceCount,
            typeIdsPreserved = before == null ? (bool?)null : types.All(t => t.idPreserved == true)
        };
    }
}

/// <summary>
/// Closes the source family once Revit has actually switched away from it. OpenAndActivateDocument may
/// only take effect when the current API context ends; closing then, from the next Idling tick, is the
/// non-blocking way to finish the job. Gives up after a deadline rather than hooking Idling forever.
/// </summary>
internal sealed class DeferredSourceClose
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private readonly UIApplication _ui;
    private readonly Document _source;
    private readonly DateTime _expires = DateTime.UtcNow + Deadline;
    private bool _finished;

    private DeferredSourceClose(UIApplication ui, Document source) { _ui = ui; _source = source; }

    internal static void Schedule(UIApplication ui, Document source)
    {
        var closer = new DeferredSourceClose(ui, source);
        ui.Idling += closer.OnIdling;
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _ui.Idling -= OnIdling;
    }

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_finished) return;
        try
        {
            if (!_source.IsValidObject || DateTime.UtcNow > _expires) { Finish(); return; }
            if (FamilyReloadSupport.SameDocument(_ui.ActiveUIDocument?.Document, _source))
            {
                e.SetRaiseWithoutDelay(); // still active: look again on the next tick
                return;
            }
            Finish();
            _source.Close(false);
        }
        catch
        {
            Finish(); // nothing to report to: the caller was told the close was pending
        }
    }
}
