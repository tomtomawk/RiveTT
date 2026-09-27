using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Design;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Places a level-based family IN A ROOM by its visible footprint: centred on an anchor, pushed
/// against the walls named (N/S/E/W), kept inside the room, checked for collisions.
///
/// Field report of 2026-09-24 (06 §3): furniture came out floating (Z counted twice), off
/// target (origin assumed at the centre), and colliding (bounding box taken for footprint).
/// The robust method written down after the session — place at Z of the level, rotate, measure
/// the visible solids, centre them on the target, push against the walls, check — is what this
/// tool does, in one call, with the result measured rather than assumed.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class PlaceInRoomTool : IRiveTTTool
{
    private static readonly BuiltInCategory[] ObstacleCategories =
    {
        BuiltInCategory.OST_Furniture, BuiltInCategory.OST_FurnitureSystems, BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Casework, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment
    };

    public string Name => "place_in_room";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Places a level-based family in a room by its visible footprint: centred on an anchor, pushed against the walls " +
        "named (N, S, E, W), kept inside the room, with the collisions and the final footprint reported.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var roomId = input["roomId"]?.Value<long>() ?? 0;
        var typeId = input["typeId"]?.Value<long>() ?? 0;
        var familyName = input["familyName"]?.Value<string>();
        var typeName = input["typeName"]?.Value<string>();
        var rotationDeg = input["rotationDeg"]?.Value<double>() ?? 0;
        // 0 by default: a fixture stands against the finish face. The 15 mm this used to
        // default to showed as a double line along every wall at 1:50 (session of 2026-09-27).
        var marginMm = input["marginMm"]?.Value<double>() ?? 0;
        var basisText = (input["footprintBasis"]?.Value<string>() ?? "visible").Trim().ToLowerInvariant();
        var offsetMm = input["offsetMm"]?.Value<double>() ?? 0;
        var against = (input["against"] as JArray)?.Select(t => (t.Value<string>() ?? "").Trim().ToUpperInvariant()).ToList()
                      ?? new List<string>();
        var dryRun = ToolHelpers.GetDryRun(input);

        if (doc.GetElement(ToolHelpers.ToElementId(roomId)) is not Room room || room.Area <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                $"roomId {roomId} is not a placed, enclosed room.",
                suggestion: "Read the room ids with export_room_data.");
        if (basisText is not ("visible" or "plan" or "full"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"footprintBasis accepts visible | plan | full; got '{basisText}'.");
        var invalidSides = against.Where(side => side is not ("N" or "S" or "E" or "W")).ToList();
        if (invalidSides.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"against accepts N, S, E, W (model axes: N = +Y, E = +X); got {string.Join(", ", invalidSides)}.");
        if ((against.Contains("N") && against.Contains("S")) || (against.Contains("E") && against.Contains("W")))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "against cannot name two opposite walls: the footprint can only touch one of them.");

        var symbol = ResolveSymbol(doc, typeId, familyName, typeName, out var symbolError);
        if (symbol == null) return symbolError!;
        if (symbol.Family?.FamilyPlacementType != FamilyPlacementType.OneLevelBased)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{symbol.FamilyName} : {symbol.Name}' is {symbol.Family?.FamilyPlacementType}, not a level-based family.",
                suggestion: "Hosted families (doors, windows, ETEL) go through create_door, create_window or " +
                            "create_point_based_element with findHost.");

        var level = doc.GetElement(room.LevelId) as Level;
        var outline = RoomOutline.OuterLoopMm(room);
        if (level == null || outline.Count < 3)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"The outline of room {roomId} could not be read.");
        var planView = PlanViewOf(doc, level);
        if (basisText == "plan" && planView == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"footprintBasis=plan needs a floor plan of level '{level.Name}', and none was found.",
                suggestion: "Create one with create_view(viewType: FloorPlan), or use footprintBasis=visible.");
        var (rMinX, rMinY, rMaxX, rMaxY) = PlanGeometry.Bounds(outline);

        var anchor = DwellingSpec.ParsePoint(input["anchorMm"]);
        var target = anchor ?? new Pt((rMinX + rMaxX) / 2, (rMinY + rMaxY) / 2);

        var warnings = new List<string>();
        using var tx = new Transaction(doc, "RiveTT: Place in room");
        try
        {
            tx.Start();
            var failures = TransactionFailureHandling.SuppressWarnings(tx);
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            var insertion = new XYZ(target.X / MmPerFoot, target.Y / MmPerFoot, level.Elevation + offsetMm / MmPerFoot);
            var instance = doc.Create.NewFamilyInstance(insertion, symbol, level, StructuralType.NonStructural);
            doc.Regenerate();
            var zCorrection = ElevationCorrection.Apply(doc, instance, insertion.Z);
            if (Math.Abs(rotationDeg) > 1e-9)
            {
                ElementTransformUtils.RotateElement(doc, instance.Id,
                    Line.CreateBound(insertion, insertion + XYZ.BasisZ), rotationDeg * Math.PI / 180);
                doc.Regenerate();
            }

            var footprint = Footprint(instance, basisText, planView);
            if (footprint == null)
            {
                warnings.Add("The family has no visible solid: it was placed by its insertion point, not by a footprint.");
                footprint = (insertion.X * MmPerFoot, insertion.Y * MmPerFoot, insertion.X * MmPerFoot, insertion.Y * MmPerFoot);
            }
            var (fMinX, fMinY, fMaxX, fMaxY) = footprint.Value;

            // A flat part (a flush shower tray, a floor-level zone) has no volume: the visible
            // solids are then only an accessory, and placing by them put a shower 100 mm into
            // the exterior wall (session of 2026-09-27). Say so, and name the other basis.
            if (basisText == "visible" && planView != null
                && Footprint(instance, "plan", planView) is { } drawn
                && Area(drawn) > 0 && Area(footprint.Value) < 0.5 * Area(drawn))
                warnings.Add($"The visible solids ({fMaxX - fMinX:F0} x {fMaxY - fMinY:F0} mm) cover less than half of what " +
                             $"the plan draws ({drawn.MaxX - drawn.MinX:F0} x {drawn.MaxY - drawn.MinY:F0} mm): part of the " +
                             "family is flat or symbolic (a flush shower tray, for one). Retry with footprintBasis=plan to " +
                             "place it by what the plan shows.");

            // Centre the footprint on the target, then push against the walls named: the first
            // wall met in the footprint's own strip, not the room's extent, which in an L-shaped
            // room is the far end of the L.
            var dx = target.X - (fMinX + fMaxX) / 2;
            var dy = target.Y - (fMinY + fMaxY) / 2;
            dx = PushAgainst(outline, target, against, 'E', 'W', dx, fMinX, fMaxX, fMinY + dy, fMaxY + dy, marginMm, rMinX, rMaxX);
            dy = PushAgainst(outline, target, against, 'N', 'S', dy, fMinY, fMaxY, fMinX + dx, fMaxX + dx, marginMm, rMinY, rMaxY);
            // Once more across: pushing N/S moved the strip the E/W walls are searched in.
            dx = PushAgainst(outline, target, against, 'E', 'W', dx, fMinX, fMaxX, fMinY + dy, fMaxY + dy, marginMm, rMinX, rMaxX);

            // Keep it inside the room's extent when it fits; say so when it cannot.
            var width = fMaxX - fMinX;
            var depth = fMaxY - fMinY;
            var tooLarge = width > rMaxX - rMinX - 2 * marginMm || depth > rMaxY - rMinY - 2 * marginMm;
            if (!tooLarge)
            {
                dx = Clamp(dx, rMinX + marginMm - fMinX, rMaxX - marginMm - fMaxX);
                dy = Clamp(dy, rMinY + marginMm - fMinY, rMaxY - marginMm - fMaxY);
            }
            else
            {
                warnings.Add($"The visible footprint ({width:F0} x {depth:F0} mm) is larger than the room " +
                             $"({rMaxX - rMinX:F0} x {rMaxY - rMinY:F0} mm, minus {marginMm:F0} mm margins): for an " +
                             "accessibility clearance family this means the room does not comply.");
            }

            ElementTransformUtils.MoveElement(doc, instance.Id, new XYZ(dx / MmPerFoot, dy / MmPerFoot, 0));
            doc.Regenerate();

            var final = Footprint(instance, basisText, planView) ??(fMinX + dx, fMinY + dy, fMaxX + dx, fMaxY + dy);
            var corners = new[]
            {
                new Pt(final.MinX, final.MinY), new Pt(final.MaxX, final.MinY),
                new Pt(final.MaxX, final.MaxY), new Pt(final.MinX, final.MaxY)
            };
            // An L-shaped room's extent is larger than the room: check the corners on the outline.
            var outside = corners.Where(c => !PlanGeometry.Contains(outline, c)
                                             && PlanGeometry.DistanceToBoundary(outline, c) > 5).ToList();
            if (outside.Count > 0)
                warnings.Add($"{outside.Count} corner(s) of the footprint fall outside the room outline (L-shaped room?): " +
                             "move the anchor or choose other walls.");

            var conflicts = Conflicts(doc, instance, level, final);
            var location = (instance.Location as LocationPoint)?.Point ?? insertion;
            // The footprint can sit inside the room while the insertion point, and whatever the
            // family draws around it, is in the wall.
            var locationMm = new Pt(location.X * MmPerFoot, location.Y * MmPerFoot);
            if (!PlanGeometry.Contains(outline, locationMm) && PlanGeometry.DistanceToBoundary(outline, locationMm) > 5)
                warnings.Add($"The insertion point ({locationMm.X:F0}, {locationMm.Y:F0}) mm is outside the room, " +
                             $"{PlanGeometry.DistanceToBoundary(outline, locationMm):F0} mm beyond its boundary: check the plan, " +
                             "or retry with footprintBasis=plan.");
            var payload = new
            {
                elementId = dryRun ? (long?)null : ToolHelpers.GetElementIdValue(instance.Id),
                familyName = symbol.FamilyName,
                typeName = symbol.Name,
                roomId,
                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString(),
                level = level.Name,
                rotationDeg,
                against,
                marginMm,
                footprintBasis = basisText,
                insertionPointMm = new[] { Math.Round(location.X * MmPerFoot, 1), Math.Round(location.Y * MmPerFoot, 1), Math.Round(location.Z * MmPerFoot, 1) },
                footprintMm = new
                {
                    minX = Math.Round(final.MinX, 1), minY = Math.Round(final.MinY, 1),
                    maxX = Math.Round(final.MaxX, 1), maxY = Math.Round(final.MaxY, 1),
                    width = Math.Round(final.MaxX - final.MinX, 1), depth = Math.Round(final.MaxY - final.MinY, 1)
                },
                insideRoom = outside.Count == 0 && !tooLarge,
                tooLargeForRoom = tooLarge,
                zCorrectedByMm = Math.Abs(zCorrection) > 0 ? Math.Round(zCorrection * MmPerFoot, 1) : (double?)null,
                conflicts,
                warnings
            };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    $"DryRun: '{symbol.FamilyName} : {symbol.Name}' would sit in '{room.Name}' as reported; " +
                    $"{conflicts.Count} collision(s).", payload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return TransactionFailureHandling.ToFailure(failures, "Revit rolled back the placement",
                    "Read errorGroups; retry with another anchor or rotation.");
            return RiveTTResult<object>.Ok(payload);
        }
        catch (Exception exception)
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"place_in_room could not place the family: {exception.Message}",
                suggestion: "Check the family type (level-based) and the room, then retry with dryRun.");
        }
    }

    private static double Clamp(double value, double min, double max) =>
        min > max ? value : Math.Max(min, Math.Min(max, value));

    private static double Area((double MinX, double MinY, double MaxX, double MaxY) box) =>
        Math.Max(0, box.MaxX - box.MinX) * Math.Max(0, box.MaxY - box.MinY);

    /// <summary>
    /// The shift along one axis that puts the footprint against the wall named for that axis
    /// (<paramref name="plus"/> = E or N, <paramref name="minus"/> = W or S), the wall being the
    /// first boundary met inside the footprint's strip on the other axis. Falls back to the room
    /// extent when the strip meets nothing.
    /// </summary>
    private static double PushAgainst(IReadOnlyList<Pt> outline, Pt target, List<string> against, char plus, char minus,
        double shift, double fMin, double fMax, double spanMin, double spanMax, double marginMm, double roomMin, double roomMax)
    {
        // A hair inside the strip, so a side wall the footprint touches is not taken for the one ahead.
        const double inset = 1;
        if (against.Contains(plus.ToString()))
            return (PlanGeometry.FirstBoundary(outline, target, plus, spanMin + inset, spanMax - inset) ?? roomMax) - marginMm - fMax;
        if (against.Contains(minus.ToString()))
            return (PlanGeometry.FirstBoundary(outline, target, minus, spanMin + inset, spanMax - inset) ?? roomMin) + marginMm - fMin;
        return shift;
    }

    /// <summary>A floor plan of the level, used to read what the plan draws of a family.</summary>
    private static View? PlanViewOf(Document doc, Level level) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel?.Id == level.Id);

    /// <summary>
    /// Axis-aligned footprint in model millimetres. visible: solids of volume &gt; 0 visible in 3D
    /// (never the bounding box); plan: what the level's floor plan draws, symbolic lines included;
    /// full: every model curve and solid, invisible clearances included.
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? Footprint(FamilyInstance instance, string basis, View? planView)
    {
        var extent = basis switch
        {
            "plan" when planView != null => FamilyExtents.Measure(instance, Transform.Identity,
                new Options { View = planView, IncludeNonVisibleObjects = false }, solidsOnly: false),
            "full" => FamilyExtents.Measure(instance, Transform.Identity,
                new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true }, solidsOnly: false),
            _ => FamilyExtents.Measure(instance, Transform.Identity, new Options { DetailLevel = ViewDetailLevel.Fine },
                solidsOnly: true)
        };
        if (extent.IsEmpty) return null;
        return (extent.MinX * MmPerFoot, extent.MinY * MmPerFoot, extent.MaxX * MmPerFoot, extent.MaxY * MmPerFoot);
    }

    private static List<object> Conflicts(Document doc, FamilyInstance placed,
        Level level, (double MinX, double MinY, double MaxX, double MaxY) footprint)
    {
        var conflicts = new List<object>();
        var others = new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(ObstacleCategories.ToList()))
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Where(fi => fi.Id != placed.Id && fi.LevelId == level.Id);
        foreach (var other in others)
        {
            var box = other.get_BoundingBox(null);
            if (box == null) continue;
            // Cheap reject on the box first, then the real test on the visible footprint.
            if (box.Max.X * MmPerFoot < footprint.MinX || box.Min.X * MmPerFoot > footprint.MaxX ||
                box.Max.Y * MmPerFoot < footprint.MinY || box.Min.Y * MmPerFoot > footprint.MaxY) continue;
            var theirs = Footprint(other, "visible", null);
            if (theirs == null) continue;
            var overlapX = Math.Min(footprint.MaxX, theirs.Value.MaxX) - Math.Max(footprint.MinX, theirs.Value.MinX);
            var overlapY = Math.Min(footprint.MaxY, theirs.Value.MaxY) - Math.Max(footprint.MinY, theirs.Value.MinY);
            if (overlapX <= 1 || overlapY <= 1) continue;
            conflicts.Add(new
            {
                id = ToolHelpers.GetElementIdValue(other.Id),
                familyName = other.Symbol?.FamilyName,
                typeName = other.Symbol?.Name,
                categoryBic = CategoryResolver.DescribeBuiltInCategory(other.Category),
                overlapMm = new[] { Math.Round(overlapX), Math.Round(overlapY) }
            });
        }
        return conflicts;
    }

    private static FamilySymbol? ResolveSymbol(Document doc, long typeId, string? familyName, string? typeName,
        out RiveTTResult<object>? error)
    {
        error = null;
        if (typeId > 0)
        {
            if (doc.GetElement(ToolHelpers.ToElementId(typeId)) is FamilySymbol byId) return byId;
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"typeId {typeId} is not a loadable family type.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
        {
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "Pass typeId, or familyName and typeName.");
            return null;
        }
        var inFamily = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
        var symbol = inFamily.FirstOrDefault(s => s.Name == typeName)
            ?? inFamily.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (symbol != null) return symbol;
        error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
            inFamily.Count == 0 ? $"No loaded family named '{familyName}'." : $"Family '{familyName}' has no type '{typeName}'.",
            suggestion: inFamily.Count == 0 ? "List the families with list_family_types." : "Types: " +
                string.Join(", ", inFamily.Select(s => $"'{s.Name}'")));
        return null;
    }
}
