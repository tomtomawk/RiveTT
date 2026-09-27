using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates one or more point-based family instances (furniture, doors, windows, columns, etc.).
/// Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement,
/// door/window facing auto-detection, and rotation support.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class CreatePointBasedElementTool : IRiveTTTool
{
    public string Name => "create_point_based_element";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more point-based family instances (furniture, doors, windows, columns, etc.). Mirrors the fork's CreatePointElementEventHandler logic, including wall-hosted placement, door/window facing auto-detection, and rotation support.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var dataToken = input["data"];
        if (dataToken == null || dataToken.Type != JTokenType.Array)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "data array is required",
                suggestion: "Provide {\"data\": [{\"typeId\": 123, \"locationPoint\": {\"x\":0,\"y\":0,\"z\":0}, ...}]}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var createdIds = new List<long>();
        var warnings   = new List<string>();
        var details = new List<object>();
        // One entry per item that produced nothing, with the caller's key: a batch of 160
        // doors where 3 fail must say WHICH 3, so that only those are sent again.
        var failed = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);

        var index = 0;
        foreach (var item in dataToken)
        {
            var key = item is JObject keyed && keyed["key"]?.Type == JTokenType.String
                ? keyed["key"]!.Value<string>()
                : null;
            var warningsBefore = warnings.Count;
            var createdBefore = createdIds.Count;
            var detailsBefore = details.Count;
            try
            {
                if (item is not JObject spec)
                    warnings.Add($"Item {index} is not a JSON object.");
                else
                    ProcessPointElement(doc, spec, createdIds, warnings, details, dryRun, key);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to create element: {ex.Message}");
            }

            var produced = dryRun ? details.Count > detailsBefore : createdIds.Count > createdBefore;
            if (!produced)
            {
                failed.Add(new
                {
                    index,
                    key,
                    error = warnings.Count > warningsBefore
                        ? string.Join(" ", warnings.Skip(warningsBefore))
                        : "No element was produced for this item."
                });
            }
            index++;
        }

        var message = dryRun
            ? $"Previewed {details.Count} point-based element specification(s)."
            : $"Successfully created {createdIds.Count} element(s).";
        if (warnings.Count > 0)
            message += "\n\nWarnings:\n  - " + string.Join("\n  - ", warnings);

        return RiveTTResult<object>.Ok(new
        {
            message,
            dryRun,
            processed = dataToken.Count(),
            created = createdIds.Count,
            skipped = dataToken.Count() - (dryRun ? details.Count : createdIds.Count),
            warnings,
            failed,
            createdElementIds = createdIds,
            details
        });
    }

    private static void ProcessPointElement(Document doc, JObject item, List<long> createdIds,
        List<string> warnings, List<object> details, bool dryRun, string? key)
    {
        // Parse category (optional — inferred from typeId)
        var categoryStr = item["category"]?.Value<string>() ?? "";
        BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
        if (!string.IsNullOrWhiteSpace(categoryStr))
            Enum.TryParse(categoryStr.Replace(".", ""), true, out builtInCategory);

        // Parse locationPoint
        var locationPtToken = item["locationPoint"];
        if (locationPtToken == null)
        {
            warnings.Add("locationPoint is required");
            return;
        }
        var locationPoint = ParseXYZ(locationPtToken);

        // Parse optional parameters
        var requestedTypeId = item["typeId"]?.Value<long?>() ?? -1;
        var baseLevelMm     = item["baseLevel"]?.Value<double?>() ?? 0.0;
        var baseOffsetMm    = item["baseOffset"]?.Value<double?>() ?? 0.0;
        var levelId         = item["levelId"]?.Value<long?>() ?? -1;
        var rotationDeg     = item["rotation"]?.Value<double?>() ?? 0.0;
        var hostWallId      = item["hostWallId"]?.Value<long?>() ?? -1;
        var facingFlipped   = item["facingFlipped"]?.Value<bool?>() ?? false;
        var handFlipped     = item["handFlipped"]?.Value<bool?>() ?? false;
        var strictType      = item["strictType"]?.Value<bool?>() ?? false;
        var levelName       = item["levelName"]?.Value<string>();
        var familyName      = item["familyName"]?.Value<string>();
        var typeName        = item["typeName"]?.Value<string>();
        // Find the host wall from the insertion point instead of requiring its id: what a
        // batch of doors placed from a plan actually knows is where each door goes.
        var findHost        = item["findHost"]?.Value<bool?>() ?? false;
        // z semantics were the single most expensive ambiguity of the connector:
        // create_wall ignores locationLine.z (baseLevelId governs) while a hosted
        // insertion point needs an ABSOLUTE project elevation. Passing z=0 by
        // analogy produced 9 consecutive failures whose only message was Revit's
        // "instances do not cut anything", which never mentions elevation.
        var zMode = (item["zMode"]?.Value<string>() ?? "absolute").Trim().ToLowerInvariant();
        if (zMode is not ("absolute" or "relativetolevel"))
        {
            warnings.Add($"zMode '{zMode}' is not recognized. Use \"absolute\" (default) or \"relativeToLevel\".");
            return;
        }

        // Resolve levels
        var baseLevelFt = baseLevelMm / MmPerFoot;
        Level? baseLevel;
        if (levelId > 0)
        {
            baseLevel = doc.GetElement(ToolHelpers.ToElementId(levelId)) as Level;
            if (baseLevel == null)
            {
                warnings.Add($"levelId {levelId} is not a Level.");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(levelName))
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            baseLevel = levels.FirstOrDefault(l => l.Name == levelName)
                ?? levels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
            if (baseLevel == null)
            {
                warnings.Add($"No level named '{levelName}'. Levels: " +
                             string.Join(", ", levels.OrderBy(l => l.Elevation).Select(l => $"'{l.Name}'")));
                return;
            }
        }
        else
        {
            baseLevel = FindNearestLevel(doc, baseLevelFt);
        }
        if (baseLevel == null)
        {
            warnings.Add("No levels found in document");
            return;
        }

        if (zMode == "relativetolevel")
        {
            locationPoint = new XYZ(locationPoint.X, locationPoint.Y,
                baseLevel.Elevation + locationPoint.Z);
        }

        // Resolve family symbol
        FamilySymbol? symbol = null;
        var byName = !string.IsNullOrWhiteSpace(familyName) || !string.IsNullOrWhiteSpace(typeName);
        if (requestedTypeId <= 0 && byName)
        {
            // Half a name used to fall through to "the first active symbol of the category",
            // silently: a family asked for, another one placed.
            if (string.IsNullOrWhiteSpace(familyName))
            {
                warnings.Add($"typeName '{typeName}' needs its familyName. Nothing was placed.");
                return;
            }
            var inFamily = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            symbol = string.IsNullOrWhiteSpace(typeName)
                ? (inFamily.Count == 1 ? inFamily[0] : null)
                : inFamily.FirstOrDefault(s => s.Name == typeName)
                  ?? inFamily.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (symbol == null)
            {
                warnings.Add(inFamily.Count == 0
                    ? $"No loaded family named '{familyName}'."
                    : string.IsNullOrWhiteSpace(typeName)
                        ? $"Family '{familyName}' has {inFamily.Count} types: pass typeName. Types: " +
                          string.Join(", ", inFamily.Select(s => $"'{s.Name}'"))
                        : $"Family '{familyName}' has no type '{typeName}'. Types: " +
                          string.Join(", ", inFamily.Select(s => $"'{s.Name}'")));
                return;
            }
            builtInCategory = (BuiltInCategory)symbol.Category.Id.Value;
        }
        else if (requestedTypeId > 0)
        {
            var typeElemId = new ElementId(requestedTypeId);
            var typeElem = doc.GetElement(typeElemId);
            if (typeElem is FamilySymbol fs)
            {
                symbol = fs;
                builtInCategory = (BuiltInCategory)symbol.Category.Id.Value;
            }
        }

        if (builtInCategory == BuiltInCategory.INVALID)
        {
            warnings.Add($"Could not determine category — provide 'category' field or a valid 'typeId'");
            return;
        }

        if (symbol == null)
        {
            if (strictType)
            {
                warnings.Add($"A valid typeId is required; {requestedTypeId} was not found.");
                return;
            }
            // Fallback: prefer active symbol
            symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(builtInCategory)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.IsActive)
                ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(builtInCategory)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();

            if (symbol == null)
            {
                warnings.Add($"No family types available for category {builtInCategory}.");
                return;
            }
            if (requestedTypeId > 0)
                warnings.Add($"Requested typeId {requestedTypeId} not found. Defaulted to '{symbol.FamilyName}: {symbol.Name}' (ID: {ToolHelpers.GetElementIdValue(symbol.Id)})");
        }

        // Resolve the requested host now: both the preview and the real call must
        // validate the insertion point against it.
        Wall? hostWall = null;
        double? hostDistanceMm = null;
        if (hostWallId > 0)
        {
            var hostElem = doc.GetElement(ToolHelpers.ToElementId(hostWallId));
            if (hostElem is Wall resolvedWall)
                hostWall = resolvedWall;
            else
            {
                // It used to say "Using auto-detection" and place the instance with no host at
                // all: a door asked in wall X came out free-standing, reported as created.
                warnings.Add($"Requested hostWallId {hostWallId} is not a wall. Nothing was placed: pass a wall id, " +
                             "or findHost=true to take the wall found at the insertion point.");
                return;
            }
        }
        else if (findHost)
        {
            hostWall = HostWallFinder.Find(doc, locationPoint, baseLevel, 20, out var distance);
            if (hostWall == null)
            {
                warnings.Add($"findHost: no wall based on '{baseLevel.Name}' passes through " +
                             $"({locationPoint.X * MmPerFoot:F0}, {locationPoint.Y * MmPerFoot:F0}, " +
                             $"{locationPoint.Z * MmPerFoot:F0}) mm (tolerance: half the wall width + 20 mm). Nothing was placed.");
                return;
            }
            hostDistanceMm = Math.Round(distance, 1);
        }

        // A level-based family placed a full storey under its own level is the "floating /
        // sunken furniture" of the 2026-09-24 session in reverse: a z meant relative to the
        // level, sent as absolute. Nothing in a dwelling sits 1 m under its level.
        var levelBased = hostWall == null && symbol.Family?.FamilyPlacementType == FamilyPlacementType.OneLevelBased;
        var offsetFromLevelMm = (locationPoint.Z - baseLevel.Elevation) * MmPerFoot;
        if (levelBased && zMode == "absolute" && offsetFromLevelMm <= -1000)
        {
            warnings.Add($"locationPoint.z = {locationPoint.Z * MmPerFoot:F0} mm is ABSOLUTE and puts this level-based family " +
                         $"{-offsetFromLevelMm:F0} mm below its level '{baseLevel.Name}' ({baseLevel.Elevation * MmPerFoot:F0} mm). " +
                         "Nothing was placed. Pass zMode=\"relativeToLevel\" with z = 0 for a family standing on the level.");
            return;
        }

        if (hostWall != null)
        {
            var hostError = DescribeHostFit(hostWall, symbol, locationPoint, baseLevel);
            if (hostError != null)
            {
                warnings.Add(hostError);
                return;
            }
        }

        if (dryRun)
        {
            details.Add(new
            {
                kind = "point_based_family_instance",
                key,
                hostDistanceMm,
                levelBased,
                offsetFromLevelMm = Math.Round(offsetFromLevelMm, 1),
                typeId = ToolHelpers.GetElementIdValue(symbol.Id),
                familyName = symbol.FamilyName,
                typeName = symbol.Name,
                category = builtInCategory.ToString(),
                levelId = ToolHelpers.GetElementIdValue(baseLevel.Id),
                levelElevationMm = Math.Round(baseLevel.Elevation * MmPerFoot, 1),
                hostWallId = hostWall != null ? (long?)ToolHelpers.GetElementIdValue(hostWall.Id) : null,
                locationPointMm = locationPtToken.DeepClone(),
                zMode,
                resolvedZmm = Math.Round(locationPoint.Z * MmPerFoot, 1),
                rotationDeg,
                facingFlipped,
                handFlipped
            });
            return;
        }

        using var tx = new Transaction(doc, "RiveTT: Create Point-Based Element");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        try
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            FamilyInstance? instance = null;
            var zCorrectionFt = 0.0;

            // Create instance
            if (hostWall != null)
            {
                // Wall-hosted (doors, windows)
                instance = doc.Create.NewFamilyInstance(locationPoint, symbol, hostWall, baseLevel, StructuralType.NonStructural);
            }
            else
            {
                instance = doc.Create.NewFamilyInstance(locationPoint, symbol, baseLevel, StructuralType.NonStructural);
            }

            if (instance != null)
            {
                // Handle door/window facing
                if (builtInCategory == BuiltInCategory.OST_Doors ||
                    builtInCategory == BuiltInCategory.OST_Windows)
                {
                    doc.Regenerate();

                    bool shouldFlip = facingFlipped;

                    // Auto-detect facing based on which side of the wall the placement point is on
                    if (!shouldFlip)
                    {
                        var wall = instance.Host as Wall;
                        if (wall != null)
                        {
                            var locCurve = wall.Location as LocationCurve;
                            if (locCurve != null)
                            {
                                var wallStart = locCurve.Curve.GetEndPoint(0);
                                var wallEnd   = locCurve.Curve.GetEndPoint(1);
                                var wallDir   = new XYZ(wallEnd.X - wallStart.X, wallEnd.Y - wallStart.Y, 0).Normalize();
                                var wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();

                                var ir = locCurve.Curve.Project(locationPoint);
                                if (ir != null)
                                {
                                    var centerPt = ir.XYZPoint;
                                    double side = (locationPoint - centerPt).DotProduct(wallNormal);
                                    double facingDot = instance.FacingOrientation.DotProduct(wallNormal);

                                    if ((side < -1e-10 && facingDot > 0) ||
                                        (side > 1e-10  && facingDot < 0))
                                    {
                                        shouldFlip = true;
                                    }
                                }
                            }
                        }
                    }

                    if (shouldFlip && instance.CanFlipFacing)
                    {
                        instance.flipFacing();
                        doc.Regenerate();
                    }
                    else if (facingFlipped)
                        warnings.Add($"{key ?? "item"}: facingFlipped was requested but '{symbol.FamilyName}' cannot flip its facing.");
                }
                else if (facingFlipped)
                {
                    // Other categories (ETEL, equipment) used to drop the flip without a word:
                    // the response then read facingFlipped: false with no reason (2026-09-27).
                    if (instance.CanFlipFacing)
                    {
                        instance.flipFacing();
                        doc.Regenerate();
                    }
                    else
                        warnings.Add($"{key ?? "item"}: facingFlipped was requested but '{symbol.FamilyName}' cannot flip " +
                                     "its facing (no flip control in the family). For a wall-hosted family the side follows " +
                                     "the host wall: host it on the wall of the room it must face.");
                }

                if (handFlipped)
                {
                    if (instance.CanFlipHand)
                    {
                        instance.flipHand();
                        doc.Regenerate();
                    }
                    else
                        warnings.Add($"{key ?? "item"}: handFlipped was requested but '{symbol.FamilyName}' cannot flip its hand.");
                }

                // Handle rotation for non-hosted elements
                if (rotationDeg != 0 &&
                    builtInCategory != BuiltInCategory.OST_Doors &&
                    builtInCategory != BuiltInCategory.OST_Windows)
                {
                    var origin = locationPoint;
                    var rotAxis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                    var angleRad = rotationDeg * Math.PI / 180.0;
                    ElementTransformUtils.RotateElement(doc, instance.Id, rotAxis, angleRad);
                }

                // Measure where Revit put a level-based instance and move it to the asked
                // elevation: the level overload of NewFamilyInstance counted the level
                // elevation twice for furniture on upper floors (field report 2026-09-24).
                if (levelBased)
                {
                    doc.Regenerate();
                    zCorrectionFt = ElevationCorrection.Apply(doc, instance, locationPoint.Z);
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                warnings.Add($"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");
            else if (instance != null)
            {
                var instanceId = ToolHelpers.GetElementIdValue(instance.Id);
                createdIds.Add(instanceId);
                var appliedZ = (instance.Location as LocationPoint)?.Point.Z;
                details.Add(new
                {
                    kind = "point_based_family_instance",
                    key,
                    elementId = instanceId,
                    levelId = ToolHelpers.GetElementIdValue(instance.LevelId),
                    hostId = ToolHelpers.GetElementIdValue(instance.Host?.Id),
                    hostDistanceMm,
                    facingFlipped = instance.FacingFlipped,
                    handFlipped = instance.HandFlipped,
                    // What was applied, not what was asked.
                    appliedZmm = appliedZ.HasValue ? Math.Round(appliedZ.Value * MmPerFoot, 1) : (double?)null,
                    offsetFromLevelMm = appliedZ.HasValue
                        ? Math.Round((appliedZ.Value - baseLevel.Elevation) * MmPerFoot, 1)
                        : (double?)null,
                    zCorrectedByMm = Math.Abs(zCorrectionFt) > 0 ? Math.Round(zCorrectionFt * MmPerFoot, 1) : (double?)null
                });
            }
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Level? FindNearestLevel(Document doc, double elevationFt)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => Math.Abs(l.Elevation - elevationFt))
            .FirstOrDefault();
    }

    private static XYZ ParseXYZ(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }

    /// <summary>
    /// Explains, in millimetres, why an insertion point cannot work in this host —
    /// before Revit answers with "instances do not cut anything" or "cannot cut
    /// instance out of wall", messages that name neither the elevation nor the
    /// width that was actually the problem. Returns null when the fit is plausible.
    /// </summary>
    private static string? DescribeHostFit(Wall hostWall, FamilySymbol symbol, XYZ point, Level level)
    {
        try
        {
            var box = hostWall.get_BoundingBox(null);
            if (box != null)
            {
                var minZ = box.Min.Z;
                var maxZ = box.Max.Z;
                // Tolerance: an insertion exactly at the base or top is legitimate.
                if (point.Z < minZ - 1e-6 || point.Z > maxZ + 1e-6)
                {
                    return $"Insertion point z={point.Z * MmPerFoot:F0} mm is outside the vertical range of " +
                           $"host wall {ToolHelpers.GetElementIdValue(hostWall.Id)} " +
                           $"({minZ * MmPerFoot:F0} mm to {maxZ * MmPerFoot:F0} mm). " +
                           $"locationPoint.z is an ABSOLUTE project elevation: level '{level.Name}' sits at " +
                           $"{level.Elevation * MmPerFoot:F0} mm, so pass that plus the sill height, " +
                           "or set zMode=\"relativeToLevel\" to have z added to the level elevation.";
                }
            }

            var openingWidthFt = OpeningWidthFt(symbol);
            if (openingWidthFt > 0 && hostWall.Location is LocationCurve locationCurve)
            {
                var wallLengthFt = locationCurve.Curve.Length;
                if (openingWidthFt >= wallLengthFt)
                {
                    return $"Opening '{symbol.FamilyName} / {symbol.Name}' is {openingWidthFt * MmPerFoot:F0} mm wide " +
                           $"but host wall {ToolHelpers.GetElementIdValue(hostWall.Id)} is only " +
                           $"{wallLengthFt * MmPerFoot:F0} mm long, so the cut cannot fit. " +
                           "Pick a narrower type or lengthen the wall.";
                }
            }
        }
        catch
        {
            // A geometry probe must never block a placement Revit would accept.
        }

        return null;
    }

    private static double OpeningWidthFt(FamilySymbol symbol)
    {
        foreach (var builtIn in new[]
                 {
                     BuiltInParameter.DOOR_WIDTH,
                     BuiltInParameter.WINDOW_WIDTH,
                     BuiltInParameter.GENERIC_WIDTH,
                     BuiltInParameter.FAMILY_WIDTH_PARAM
                 })
        {
            var parameter = symbol.get_Parameter(builtIn);
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
                return parameter.AsDouble();
        }

        return 0;
    }
}
