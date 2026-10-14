using System;
using System.Collections.Generic;
using System.Linq;

namespace RiveTT.Core.Results;

/// <summary>
/// The TEXT of the Revit warnings that a tool transaction deleted without showing them.
/// Until now only their count reached the caller (warningsSuppressed: 1), so a call that
/// silently did nothing — a viewport that would not move — could not be explained: the
/// one warning that said why had been deleted unread.
///
/// RiveTT.Tools records here from its failures preprocessor and the router, which cannot
/// reference that assembly, drains it into execution.suppressedWarnings. Tools run one
/// at a time on the Revit thread, so a process-wide list scoped by Clear/Drain around
/// each call is enough; the lock only guards against the pipe thread touching it.
/// </summary>
public static class SuppressedWarningLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Messages = new();

    public static void Record(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (Gate) Messages.Add(message!.Trim());
    }

    public static void Clear()
    {
        lock (Gate) Messages.Clear();
    }

    /// <summary>Distinct messages with their counts, in order of first appearance; empties the log.</summary>
    public static IReadOnlyList<(string Message, int Count)> Drain()
    {
        lock (Gate)
        {
            var grouped = Messages
                .GroupBy(m => m, StringComparer.Ordinal)
                .Select(g => (g.Key, g.Count()))
                .ToList();
            Messages.Clear();
            return grouped;
        }
    }
}
