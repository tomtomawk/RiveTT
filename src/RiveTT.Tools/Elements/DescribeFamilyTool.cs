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

namespace RiveTT.Tools.Elements;

/// <summary>
/// Describes how a loadable family type sits around its insertion point: placement type, the
/// Z rule that goes with it, facing direction, and three extents in FAMILY coordinates —
/// visible solids, all model geometry (invisible clearances included), bounding box.
///
/// Field report of 2026-09-24: furniture was placed wrong three times over, first assuming the
/// origin at the centre, then trusting the bounding box — which includes invisible clearance
/// geometry: a 1.40 x 1.90 m bed measured 2.85 x 3.90 m — then assuming Z absolute for every
/// family. The agency's catalogue of origins had to be measured by script. This read tool
/// measures it directly, on a placed instance or on the type's own geometry, without any
/// transaction.
/// </summary>
[ToolSafety(true, false)]
public sealed class DescribeFamilyTool : IRiveTTTool
{
    public string Name => "describe_family";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Describes a family type around its insertion point: placement type and Z rule, facing direction, and its " +
        "visible, full and bounding-box extents in family coordinates (mm).";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var instanceId = input["instanceId"]?.Value<long>() ?? 0;
        var typeId = input["typeId"]?.Value<long>() ?? 0;
        var familyName = input["familyName"]?.Value<string>();
        var typeName = input["typeName"]?.Value<string>();
        var planViewId = input["planViewId"]?.Value<long>() ?? 0;
        var detailLevelText = input["detailLevel"]?.Value<string>() ?? "Fine";

        if (!Enum.TryParse<ViewDetailLevel>(detailLevelText, true, out var detailLevel) || detailLevel == ViewDetailLevel.Undefined)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"detailLevel '{detailLevelText}' is not supported: Coarse | Medium | Fine.");

        FamilyInstance? instance = null;
        FamilySymbol? symbol = null;
        if (instanceId > 0)
        {
            instance = doc.GetElement(ToolHelpers.ToElementId(instanceId)) as FamilyInstance;
            if (instance == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"instanceId {instanceId} is not a family instance.");
            symbol = instance.Symbol;
        }
        else if (typeId > 0)
        {
            symbol = doc.GetElement(ToolHelpers.ToElementId(typeId)) as FamilySymbol;
            if (symbol == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"typeId {typeId} is not a loadable family type.");
        }
        else if (!string.IsNullOrWhiteSpace(familyName))
        {
            var inFamily = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inFamily.Count == 0)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"No loaded family named '{familyName}'.",
                    suggestion: "List the loaded families with list_family_types(familyNameFilter).");
            symbol = string.IsNullOrWhiteSpace(typeName)
                ? (inFamily.Count == 1 ? inFamily[0] : null)
                : inFamily.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (symbol == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    string.IsNullOrWhiteSpace(typeName)
                        ? $"Family '{familyName}' has {inFamily.Count} types: pass typeName."
                        : $"Family '{familyName}' has no type '{typeName}'.",
                    suggestion: "Types: " + string.Join(", ", inFamily.Select(s => $"'{s.Name}'")));
        }
        else
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "Pass instanceId, typeId, or familyName (+ typeName).");
        }

        // A placed instance is the most faithful sample: it carries the real parameters.
        var sampledFrom = "instance";
        if (instance == null)
        {
            instance = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .FirstOrDefault(i => i.Symbol?.Id == symbol!.Id);
            sampledFrom = instance != null ? "first placed instance of the type" : "type geometry (no instance placed)";
        }

        View? planView = null;
        if (planViewId > 0)
        {
            planView = doc.GetElement(ToolHelpers.ToElementId(planViewId)) as View;
            if (planView == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"planViewId {planViewId} is not a view.");
        }

        try
        {
            var family = symbol!.Family;
            var placement = SafeRead(() => family.FamilyPlacementType.ToString()) ?? "unknown";

            Element sample = (Element?)instance ?? symbol;
            var toLocal = instance != null ? instance.GetTransform().Inverse : Transform.Identity;

            var visible = FamilyExtents.Measure(sample, toLocal,
                new Options { DetailLevel = detailLevel, IncludeNonVisibleObjects = false }, solidsOnly: true);
            var full = FamilyExtents.Measure(sample, toLocal,
                new Options { DetailLevel = detailLevel, IncludeNonVisibleObjects = true }, solidsOnly: false);
            FamilyExtents.Extent? plan = null;
            if (planView != null && instance != null)
                plan = FamilyExtents.Measure(instance, toLocal,
                    new Options { View = planView, IncludeNonVisibleObjects = false }, solidsOnly: false);

            FamilyExtents.Extent? box = null;
            if (instance != null && SafeRead(() => instance.get_BoundingBox(null)) is BoundingBoxXYZ bbox)
            {
                box = new FamilyExtents.Extent();
                foreach (var corner in Corners(bbox)) box.Add(toLocal.OfPoint(corner));
            }

            if (visible.IsEmpty && full.IsEmpty)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"No geometry could be read for '{symbol.FamilyName} : {symbol.Name}' ({sampledFrom}).",
                    suggestion: instance == null
                        ? "An unplaced, inactive type exposes no geometry. Place one instance (create_point_based_element), " +
                          "describe it with instanceId, then delete it."
                        : "The instance has no 3D geometry at this detail level; try detailLevel Fine or a planViewId.");

            object? facing = null, hand = null;
            if (instance != null)
            {
                facing = Vector(toLocal.OfVector(SafeRead(() => instance.FacingOrientation) ?? XYZ.Zero));
                hand = Vector(toLocal.OfVector(SafeRead(() => instance.HandOrientation) ?? XYZ.Zero));
            }

            var warnings = new List<string>();
            if (box != null && !visible.IsEmpty && (box.Width - visible.Width > 100 || box.Depth - visible.Depth > 100))
                warnings.Add($"The bounding box ({box.Width:F0} x {box.Depth:F0} mm) is larger than the visible solids " +
                             $"({visible.Width:F0} x {visible.Depth:F0} mm): it includes invisible clearances. Place and check " +
                             "against visibleExtentMm, never against the bounding box.");
            if (!visible.IsEmpty && (Math.Abs(visible.CenterX) > 50 || Math.Abs(visible.CenterY) > 50))
                warnings.Add($"The origin is NOT at the centre of the visible footprint: the centre sits at " +
                             $"({visible.CenterX:F0}, {visible.CenterY:F0}) mm from it, in family coordinates.");

            return RiveTTResult<object>.Ok(new
            {
                familyName = symbol.FamilyName,
                typeName = symbol.Name,
                typeId = ToolHelpers.GetElementIdValue(symbol.Id),
                category = symbol.Category?.Name,
                categoryBic = CategoryResolver.DescribeBuiltInCategory(symbol.Category),
                sampledFrom,
                instanceId = instance == null ? (long?)null : ToolHelpers.GetElementIdValue(instance.Id),
                placement,
                hosting = SafeRead(() => family.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR)?.AsValueString()),
                zRule = ZRule(placement),
                originLocal = new[] { 0, 0, 0 },
                facingOrientationLocal = facing,
                handOrientationLocal = hand,
                units = "mm, family coordinates (rotation 0, insertion point at 0,0,0)",
                detailLevel = detailLevel.ToString(),
                visibleExtentMm = visible.IsEmpty ? null : visible.ToReport(),
                fullExtentMm = full.IsEmpty ? null : full.ToReport(),
                planViewExtentMm = plan == null || plan.IsEmpty ? null : plan.ToReport(),
                boundingBoxMm = box?.ToReport(),
                notes = new[]
                {
                    "visibleExtentMm: solids of volume > 0 visible in 3D — the physical footprint to place against walls.",
                    "fullExtentMm: every model curve and solid, invisible ones included — clearances and usage zones.",
                    "planViewExtentMm (with planViewId): what the plan view draws, symbolic lines included.",
                    "boundingBoxMm: Revit's box, invisible geometry included; not a footprint."
                },
                warnings
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"describe_family could not measure the family: {exception.Message}",
                suggestion: "Retry with an instanceId of a placed instance.");
        }
    }

    private static string ZRule(string placement) => placement switch
    {
        nameof(FamilyPlacementType.OneLevelBased) =>
            "relativeToLevel: stands on its level (create_point_based_element: zMode relativeToLevel, z = 0; the elevation is measured and corrected after placement)",
        nameof(FamilyPlacementType.OneLevelBasedHosted) =>
            "absolute: hosted (door, window, ETEL) — the insertion Z is the ABSOLUTE elevation, level + sill",
        nameof(FamilyPlacementType.WorkPlaneBased) => "workPlane: Z comes from the work plane it is placed on",
        nameof(FamilyPlacementType.TwoLevelsBased) => "twoLevels: base and top levels drive the height",
        _ => "see placement"
    };

    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        var t = box.Transform ?? Transform.Identity;
        foreach (var x in new[] { box.Min.X, box.Max.X })
            foreach (var y in new[] { box.Min.Y, box.Max.Y })
                foreach (var z in new[] { box.Min.Z, box.Max.Z })
                    yield return t.OfPoint(new XYZ(x, y, z));
    }

    private static object Vector(XYZ vector) => new[]
    {
        Math.Round(vector.X, 3), Math.Round(vector.Y, 3), Math.Round(vector.Z, 3)
    };

    private static T? SafeRead<T>(Func<T?> read) where T : class
    {
        try { return read(); } catch { return null; }
    }
}
