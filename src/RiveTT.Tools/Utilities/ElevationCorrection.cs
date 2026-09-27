using System;
using Autodesk.Revit.DB;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Puts a freshly placed level-based family instance at the elevation that was asked for,
/// whatever convention Revit applied to the Z of the insertion point.
///
/// Field report of 2026-09-24 (Revit 2026, agency template): furniture, sanitary fittings
/// and bikes placed with NewFamilyInstance(point, symbol, level, ...) and an ABSOLUTE Z came
/// out one storey too high — +2.89 m at R+1, +5.61 m at R+2 — because the level elevation
/// was counted twice. Hosted openings need the absolute Z, level-based families apparently
/// the relative one, and neither the API documentation nor the error messages say so.
/// Rather than betting on one convention, the instance is measured after placement and
/// moved by the difference; the caller is told when that happened.
/// </summary>
public static class ElevationCorrection
{
    /// <summary>1 mm, in feet.</summary>
    public const double ToleranceFt = 1.0 / LengthUnits.MmPerFoot;

    /// <summary>
    /// Moves <paramref name="instance"/> vertically so that its location point sits at
    /// <paramref name="targetZ"/> (absolute, feet). Returns the move applied in feet
    /// (target minus measured), 0 when the instance was already in place or is not a
    /// one-level-based family with a location point.
    /// </summary>
    public static double Apply(Document document, FamilyInstance instance, double targetZ)
    {
        if (!AppliesTo(instance)) return 0;
        if (instance.Location is not LocationPoint location) return 0;

        var delta = targetZ - location.Point.Z;
        if (Math.Abs(delta) <= ToleranceFt) return 0;

        ElementTransformUtils.MoveElement(document, instance.Id, new XYZ(0, 0, delta));
        document.Regenerate();
        return delta;
    }

    /// <summary>
    /// Only one-level-based families: a hosted family follows its host, and a work-plane or
    /// curve-based one has its own Z semantics that a vertical move could break.
    /// </summary>
    public static bool AppliesTo(FamilyInstance instance)
    {
        try
        {
            // A level-based instance reports its level as Host; anything else is a real host.
            return (instance.Host == null || instance.Host is Level)
                && instance.Symbol?.Family?.FamilyPlacementType == FamilyPlacementType.OneLevelBased;
        }
        catch
        {
            return false;
        }
    }
}
