using System;
using System.Collections.Generic;
using System.Linq;
namespace RiveTT.Tools.Utilities;

public static class DimensionMeasurementValidation
{
    public static bool IsValid(IEnumerable<double?> values)
    {
        var segments = values.ToArray();
        return segments.Length > 0 && segments.All(v => v.HasValue && double.IsFinite(v.Value) && v.Value > 1e-9);
    }
}
