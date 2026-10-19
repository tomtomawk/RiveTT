using System;
using System.Collections.Generic;
using System.Linq;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// What a script's <c>changes.deletedCount</c> is made of. 1,527 lines and one ungroup read as "deletedCount: 6487"
/// with no detail, which forced a second check on the model (field report EQS_IRM, 2026-10-15). Revit-free so the
/// arithmetic is tested; the tracker feeds it the classes it recorded BEFORE the script ran, because a deleted
/// element can no longer be read afterwards.
/// </summary>
internal static class DeletedBreakdown
{
    internal const int MaxClasses = 15;

    internal sealed record Result(
        int TopLevelCount, int AuxiliaryCount, int UnclassifiedCount, IReadOnlyDictionary<string, int> ByClass);

    /// <param name="deleted">One entry per deleted id: its class name and whether it is scaffolding
    /// (sketch, sketch plane, model/detail curve, element without category); a null class is an id that was
    /// not known before the run.</param>
    internal static Result Summarize(IEnumerable<(string? ClassName, bool Auxiliary)> deleted)
    {
        int top = 0, aux = 0, unknown = 0;
        var byClass = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (className, auxiliary) in deleted)
        {
            if (className == null) { unknown++; continue; }
            if (auxiliary) aux++; else top++;
            byClass[className] = byClass.TryGetValue(className, out var n) ? n + 1 : 1;
        }

        var ordered = byClass.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        var capped = new Dictionary<string, int>();
        foreach (var pair in ordered.Take(MaxClasses)) capped[pair.Key] = pair.Value;
        var rest = ordered.Skip(MaxClasses).Sum(p => p.Value);
        if (rest > 0) capped["(other classes)"] = rest;
        return new Result(top, aux, unknown, capped);
    }
}
