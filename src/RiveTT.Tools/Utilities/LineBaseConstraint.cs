using System;
using Newtonsoft.Json.Linq;

namespace RiveTT.Tools.Utilities;

/// <summary>Separates a level identifier from an absolute project elevation (mm).</summary>
public sealed class LineBaseConstraint
{
    public LineBaseConstraint(long? levelId, double elevationMm, double offsetMm)
        => (LevelId, ElevationMm, OffsetMm) = (levelId, elevationMm, offsetMm);
    public long? LevelId { get; }
    public double ElevationMm { get; }
    public double OffsetMm { get; }
    public static LineBaseConstraint Parse(JObject spec)
    {
        if (spec["baseLevelId"] is { Type: not JTokenType.Null } levelToken
            && levelToken.Type != JTokenType.Integer)
            throw new ArgumentException("baseLevelId must be an integer Revit level ID.");
        long? id = spec["baseLevelId"]?.Value<long?>();
        var legacy = spec["baseLevel"]?.Value<double?>();
        var elevation = spec["baseElevationMm"]?.Value<double?>();
        var offset = spec["baseOffset"]?.Value<double?>() ?? 0;
        // baseLevel was unitless in the public schema. Accept it as an ID alias,
        // never convert an element ID to millimetres. The old zero default survives.
        if (legacy.HasValue && legacy.Value != 0)
        {
            if (!double.IsFinite(legacy.Value) || legacy <= 0 || legacy != Math.Truncate(legacy.Value)
                || legacy >= long.MaxValue)
                throw new ArgumentException("baseLevel is a level ID alias. Use baseElevationMm for an absolute elevation in mm.");
            if (id.HasValue && id != (long)legacy.Value)
                throw new ArgumentException("baseLevel and baseLevelId identify different levels.");
            id = (long)legacy.Value;
        }
        if (id.HasValue && id <= 0)
            throw new ArgumentException("baseLevelId must be a positive level ID.");
        if (id.HasValue && elevation.HasValue)
            throw new ArgumentException("Use either baseLevelId (relative baseOffset in mm) or baseElevationMm (absolute project Z in mm), not both.");
        if (!double.IsFinite(elevation ?? 0) || !double.IsFinite(offset))
            throw new ArgumentException("Base elevation and offset must be finite millimetre values.");
        return new LineBaseConstraint(id, elevation ?? 0, offset);
    }

    public double RelativeOffsetMm(double levelElevationMm)
        => LevelId.HasValue ? OffsetMm : ElevationMm + OffsetMm - levelElevationMm;
}
