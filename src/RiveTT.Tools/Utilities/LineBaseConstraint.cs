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
        // `baseLevel` was ambiguous between an ID and an elevation. Reject it
        // rather than preserve a path that can silently corrupt geometry (D-20).
        if (spec["baseLevel"] is { Type: not JTokenType.Null })
            throw new ArgumentException("baseLevel is no longer supported. Use baseLevelId for a Revit level ID, or baseElevationMm for an absolute elevation in mm.");
        if (spec["baseLevelId"] is { Type: not JTokenType.Null } levelToken
            && levelToken.Type != JTokenType.Integer)
            throw new ArgumentException("baseLevelId must be an integer Revit level ID.");
        long? id = spec["baseLevelId"]?.Value<long?>();
        var elevation = spec["baseElevationMm"]?.Value<double?>();
        var offset = spec["baseOffset"]?.Value<double?>() ?? 0;
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
