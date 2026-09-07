using System;
using System.Collections.Generic;
using System.Linq;
namespace RiveTT.Tools.Utilities;

public static class ScheduleRowClassification
{
    public static bool IsColumnHeading(IReadOnlyList<string> headings,
        IReadOnlyList<string> cells, IReadOnlyList<bool> textCells)
        => headings.Count > 0 && cells.Count == headings.Count && textCells.Count == cells.Count
            && textCells.All(isText => isText)
            && headings.SequenceEqual(cells, StringComparer.Ordinal);
}
