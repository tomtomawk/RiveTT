using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Reads, searches, replaces and deletes the text of text notes, keeping their formatting.
///
/// A text note's content is not a parameter ("Texte" matched nothing and export_elements_data
/// answered "0 of 466" with only a warning in the message), and no tool could edit it: correcting
/// the notices of four sheets went through send_code_to_revit. The edit works on the note's
/// FormattedText — Find to locate, SetPlainText on the found range, SetFormattedText back — so
/// bold, underline and all-caps formatting survive, which a plain Text assignment destroys.
///
/// The search is case-insensitive by default and is made on the STORED text: a note stored as
/// "Volumétrie" and displayed "VOLUMÉTRIE" through the all-caps format is found by either spelling.
/// </summary>
[ReadOnlyActions("read", "read", "find")]
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class ManageTextNotesTool : IRiveTTTool
{
    public string Name => "manage_text_notes";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Reads, searches, replaces or deletes the text of text notes, keeping their formatting (bold, underline, " +
        "all caps). action=read (text of each note) | find (search, with counts) | replace (search -> replacement) | " +
        "delete (removes the searched text). Scope: elementIds, or viewIds / sheetIds, or scope=\"project\" (every note); " +
        "default the active view. The search ignores case by default (caseSensitive=true to match it) and runs on the " +
        "STORED text: a note stored \"Volumétrie\" and shown \"VOLUMÉTRIE\" in all caps is found either way. replace and " +
        "delete preview by default (dryRun) with the resulting text of each note.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "read").ToLowerInvariant();
        if (action is not ("read" or "find" or "replace" or "delete"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"Unknown action '{action}'",
                suggestion: "Use: read, find, replace, delete");

        var search = input["search"]?.Value<string>();
        var replacement = input["replacement"]?.Value<string>() ?? "";
        var caseSensitive = input["caseSensitive"]?.Value<bool>() ?? false;
        var wholeWord = input["wholeWord"]?.Value<bool>() ?? false;
        var limit = Math.Clamp(input["limit"]?.Value<int>() ?? 200, 1, 2000);

        if (action != "read" && string.IsNullOrEmpty(search))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "search is required for " + action,
                suggestion: "The text to look for, as stored in the note (case is ignored unless caseSensitive=true).");
        if (action == "replace" && input["replacement"] == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "replacement is required for replace",
                suggestion: "Use action=delete to remove the searched text, or replacement \"\" explicitly.");
        if (action == "delete") replacement = "";

        try
        {
            var notes = ResolveNotes(doc, input, out var scopeDescription, out var scopeError);
            if (scopeError != null) return scopeError;

            if (action == "read")
                return Read(doc, notes, limit, scopeDescription, search, caseSensitive);

            // Everything is computed on the notes' FormattedText COPIES: nothing is written until the
            // transaction below, so the preview and the real call report the same text.
            var plans = new List<NotePlan>();
            foreach (var note in notes)
            {
                var formatted = note.GetFormattedText();
                var before = formatted.GetPlainText();
                var count = CountOccurrences(formatted, search!, caseSensitive, wholeWord);
                if (count == 0) continue;

                string? after = null;
                if (action != "find")
                {
                    Substitute(formatted, search!, replacement, caseSensitive, wholeWord);
                    after = formatted.GetPlainText();
                }
                plans.Add(new NotePlan(note, formatted, before, after, count));
            }

            var totalOccurrences = plans.Sum(p => p.Count);
            var report = plans.Take(limit).Select(p => Describe(doc, p, action)).ToList();

            if (action == "find")
                return RiveTTResult<object>.Ok(new
                {
                    action, search, caseSensitive, scope = scopeDescription,
                    notesScanned = notes.Count, matchedNotes = plans.Count, totalOccurrences,
                    returned = report.Count, notes = report
                });

            var dryRun = ToolHelpers.GetDryRun(input);
            if (dryRun || plans.Count == 0)
                return RiveTTResult<object>.Ok(new
                {
                    action, search, replacement, caseSensitive, scope = scopeDescription,
                    dryRun = true, mutated = false, previewMethod = "declared",
                    message = plans.Count == 0
                        ? $"No note of the scope ({notes.Count}) contains '{search}': nothing to change."
                        : $"DryRun: {totalOccurrences} occurrence(s) in {plans.Count} note(s) would change; formatting is kept. Set dryRun=false to apply.",
                    notesScanned = notes.Count, matchedNotes = plans.Count, totalOccurrences,
                    returned = report.Count, notes = report
                });

            using var tx = new Transaction(doc, $"RiveTT: Text notes {action}");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            foreach (var plan in plans)
                plan.Note.SetFormattedText(plan.Formatted);
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return RiveTTResult<object>.Ok(new
            {
                action, search, replacement, caseSensitive, scope = scopeDescription,
                notesScanned = notes.Count, changedNotes = plans.Count, totalOccurrences,
                returned = report.Count, notes = report
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"manage_text_notes could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the scope with a read tool before retrying. The full call, its "
                    + "duration and this error are in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private sealed record NotePlan(TextNote Note, FormattedText Formatted, string Before, string? After, int Count);

    private static List<TextNote> ResolveNotes(Document doc, JObject input, out string scope, out RiveTTResult<object>? error)
    {
        error = null;
        var ids = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        var scopeName = input["scope"]?.Value<string>()?.ToLowerInvariant();

        if (ids.Count > 0)
        {
            scope = $"{ids.Count} element id(s)";
            var found = ids.Select(id => doc.GetElement(new ElementId(id)) as TextNote).ToList();
            var missing = ids.Where((id, i) => found[i] == null).ToList();
            if (missing.Count > 0)
            {
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"Not text notes (or not found): {string.Join(", ", missing.Take(10))}",
                    suggestion: "Text note ids come from this tool (action read) or filter_elements (OST_TextNotes).");
                return new List<TextNote>();
            }
            return found.Select(n => n!).ToList();
        }

        if (scopeName == "project")
        {
            scope = "the whole project";
            return new FilteredElementCollector(doc).OfClass(typeof(TextNote)).Cast<TextNote>().ToList();
        }

        var views = viewIds.Concat(sheetIds).ToList();
        if (views.Count == 0)
        {
            if (doc.ActiveView == null)
            {
                scope = "";
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No scope given and no active view",
                    suggestion: "Pass elementIds, viewIds, sheetIds or scope=\"project\".");
                return new List<TextNote>();
            }
            views.Add(doc.ActiveView.Id.Value);
        }
        scope = views.Count == 1 ? $"view/sheet {views[0]}" : $"{views.Count} views/sheets";
        return views
            .SelectMany(v => new FilteredElementCollector(doc, new ElementId(v)).OfClass(typeof(TextNote)).Cast<TextNote>())
            .ToList();
    }

    private static RiveTTResult<object> Read(Document doc, List<TextNote> notes, int limit, string scope,
        string? search, bool caseSensitive)
    {
        IEnumerable<TextNote> selected = notes;
        if (!string.IsNullOrEmpty(search))
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            selected = notes.Where(n => n.GetFormattedText().GetPlainText().Contains(search!, comparison));
        }
        var list = selected.ToList();
        var returned = list.Take(limit).Select(note => (object)new
        {
            id = ToolHelpers.GetElementIdValue(note.Id),
            text = note.GetFormattedText().GetPlainText(),
            viewId = ToolHelpers.GetElementIdValue(note.OwnerViewId),
            viewName = doc.GetElement(note.OwnerViewId)?.Name,
            sheetNumber = (doc.GetElement(note.OwnerViewId) as ViewSheet)?.SheetNumber,
            typeName = note.TextNoteType?.Name,
            positionMm = new { x = Math.Round(note.Coord.X * MmPerFoot, 1), y = Math.Round(note.Coord.Y * MmPerFoot, 1) }
        }).ToList();
        return RiveTTResult<object>.Ok(new
        {
            action = "read", scope, notesInScope = notes.Count, matched = list.Count, returned = returned.Count,
            truncated = list.Count > returned.Count, notes = returned
        });
    }

    private static object Describe(Document doc, NotePlan plan, string action) => new
    {
        id = ToolHelpers.GetElementIdValue(plan.Note.Id),
        viewId = ToolHelpers.GetElementIdValue(plan.Note.OwnerViewId),
        sheetNumber = (doc.GetElement(plan.Note.OwnerViewId) as ViewSheet)?.SheetNumber,
        occurrences = plan.Count,
        before = plan.Before,
        after = plan.After
    };

    /// <summary>Occurrences of the text, found the way the substitution will find them.</summary>
    private static int CountOccurrences(FormattedText formatted, string search, bool caseSensitive, bool wholeWord)
    {
        var count = 0;
        var start = 0;
        var length = formatted.GetPlainText().Length;
        while (start <= length)
        {
            var range = formatted.Find(search, start, caseSensitive, wholeWord);
            if (range.Length == 0) break;
            count++;
            start = range.Start + Math.Max(range.Length, 1);
        }
        return count;
    }

    /// <summary>
    /// Replaces every occurrence on the FormattedText copy, range by range, so each keeps the character
    /// formatting of the text it replaces. Resumes after the inserted text: a replacement that contains the
    /// searched text cannot loop.
    /// </summary>
    private static void Substitute(FormattedText formatted, string search, string replacement, bool caseSensitive, bool wholeWord)
    {
        var start = 0;
        while (true)
        {
            var range = formatted.Find(search, start, caseSensitive, wholeWord);
            if (range.Length == 0) break;
            formatted.SetPlainText(range, replacement);
            start = range.Start + replacement.Length;
            if (start > formatted.GetPlainText().Length) break;
        }
    }
}
