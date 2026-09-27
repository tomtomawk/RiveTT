using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a component stair between two levels.
///
/// Why this exists now: the connector documented stairs as impossible because
/// "the standard Revit stair goes through a modal sketch editor". That is true of
/// stair-BY-SKETCH only. A component stair is built through
/// <see cref="StairsEditScope"/>, which is an API edit scope — it behaves like a
/// TransactionGroup, opens no UI, and is Autodesk's documented way to create
/// stairs programmatically. A building with vertical circulation was simply out
/// of reach for no reason.
///
/// Contract: StairsEditScope must be started with NO transaction open, runs are
/// created in transactions INSIDE the scope, and the scope is committed with a
/// failure preprocessor so a warning cannot open a modal dialog.
///
/// Multi-run stairs (field report 2026-09-24, bug 1.1): every run used to be created with
/// its location line at the BASE elevation, so a second run started on the floor instead
/// of on top of the first one — 9 risers instead of 17, no landing, while the preview had
/// promised one. CreateStraightRun reads the Z of the location line as the run's base
/// elevation in model coordinates; each run now starts at the top of the previous one, the
/// landing is checked with CanCreateAutomaticLanding, and a junction Revit cannot land is
/// refused with its geometry instead of leaving two disconnected runs.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public sealed class CreateStairTool : IRiveTTTool
{

    public string Name => "create_stair";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Creates a native component stair between two levels from one or more straight runs. " +
        "runs is [{p0:{x,y}, p1:{x,y}}, ...] in mm (plan coordinates; the levels drive the elevation). " +
        "Each run starts on top of the previous one and consecutive runs are joined by an automatic landing; " +
        "a junction Revit cannot land is refused unless requireLandings=false. The response reports the " +
        "risers Revit actually produced, run by run, against the ones the levels require.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var baseLevelId = input["baseLevelId"]?.Value<long>() ?? 0;
        var topLevelId = input["topLevelId"]?.Value<long>() ?? 0;
        var stairsTypeId = input["stairsTypeId"]?.Value<long>() ?? 0;
        var railingTypeId = input["railingTypeId"]?.Value<long>() ?? 0;
        var widthMm = input["widthMm"]?.Value<double>() ?? 0;
        var requireLandings = input["requireLandings"]?.Value<bool>() ?? true;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (baseLevelId <= 0 || topLevelId <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "baseLevelId and topLevelId are both required",
                suggestion: "Read the level ids from get_project_info.");

        var baseLevel = doc.GetElement(ToolHelpers.ToElementId(baseLevelId)) as Level;
        var topLevel = doc.GetElement(ToolHelpers.ToElementId(topLevelId)) as Level;
        if (baseLevel == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"baseLevelId {baseLevelId} is not a Level");
        if (topLevel == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"topLevelId {topLevelId} is not a Level");
        if (topLevel.Elevation <= baseLevel.Elevation)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"topLevel '{topLevel.Name}' ({topLevel.Elevation * MmPerFoot:F0} mm) must be ABOVE baseLevel " +
                $"'{baseLevel.Name}' ({baseLevel.Elevation * MmPerFoot:F0} mm)");

        if (!TryReadRuns(input["runs"], out var runInputs, out var runError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, runError);

        StairsType? stairsType = null;
        if (stairsTypeId > 0)
        {
            stairsType = doc.GetElement(ToolHelpers.ToElementId(stairsTypeId)) as StairsType;
            if (stairsType == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"stairsTypeId {stairsTypeId} is not a StairsType",
                    suggestion: "List the available ones with list_system_types(category: \"OST_Stairs\").");
        }

        var heightFt = topLevel.Elevation - baseLevel.Elevation;
        var planningType = stairsType ?? DefaultStairsType(doc);
        var maxRiserFt = ReadOrZero(() => planningType?.MaxRiserHeight ?? 0);
        var treadFt = ReadOrZero(() => planningType?.MinTreadDepth ?? 0);
        var typeWidthFt = ReadOrZero(() => planningType?.MinRunWidth ?? 0);
        var plan = StairRunPlanner.Build(runInputs, heightFt * MmPerFoot, maxRiserFt * MmPerFoot,
            treadFt * MmPerFoot, widthMm > 0 ? widthMm : typeWidthFt * MmPerFoot,
            maxRiserFt > 0 ? heightFt / maxRiserFt : (double?)null);

        if (dryRun)
        {
            // A junction no landing can bridge fails the preview: the real call would refuse it
            // (or, with requireLandings=false, leave two disconnected runs).
            if (plan.Problems.Count > 0 && requireLandings)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "DryRun: this stair cannot be built as described: " + string.Join(" ", plan.Problems),
                    suggestion: "Place each run so that it starts next to the end of the previous one: " +
                                "U-shaped, offset by the run width plus the well; L-shaped, at the corner; straight, " +
                                "one landing depth further along. Nothing was created.",
                    context: new Dictionary<string, object>
                    {
                        ["stage"] = "preview",
                        ["modelChanged"] = false,
                        ["plan"] = DescribePlan(plan)
                    });

            return RiveTTResult<object>.Ok(new
            {
                message = $"DryRun: a {runInputs.Count}-run stair would be created from '{baseLevel.Name}' to " +
                          $"'{topLevel.Name}' ({heightFt * MmPerFoot:F0} mm), each run starting on top of the previous one" +
                          (runInputs.Count > 1 ? $", joined by {runInputs.Count - 1} landing(s) if Revit accepts the junctions." : "."),
                baseLevel = baseLevel.Name,
                topLevel = topLevel.Name,
                heightMm = Math.Round(heightFt * MmPerFoot, 1),
                runCount = runInputs.Count,
                landingCount = Math.Max(0, runInputs.Count - 1),
                requireLandings,
                estimatedRiserCount = plan.EstimatedTotalRisers,
                desiredRiserCount = plan.DesiredRisers,
                maxRiserHeightMm = maxRiserFt > 0 ? Math.Round(maxRiserFt * MmPerFoot, 1) : (double?)null,
                totalRunLengthMm = Math.Round(runInputs.Sum(run => run.Length), 1),
                plan = DescribePlan(plan),
                stairsTypeId = stairsTypeId > 0 ? (long?)stairsTypeId : null,
                widthMm = widthMm > 0 ? (double?)widthMm : null,
                railingTypeId = railingTypeId > 0 ? (long?)railingTypeId : null,
                warnings = plan.Warnings.Concat(plan.Problems).ToArray(),
                // What a preview of this tool cannot know: only Revit decides.
                notVerified = new[]
                {
                    "whether Revit accepts each landing (checked with CanCreateAutomaticLanding on the real call)",
                    "the exact riser count of each run (estimated from length / tread depth; the type's " +
                    "begin/end-with-riser options can shift it by one)",
                    "clashes with walls, floors and openings"
                }
            });
        }

        StairsEditScope? scope = null;
        try
        {
            // The edit scope behaves like a transaction group: it must be started
            // with no transaction open, and the runs are created inside it.
            scope = new StairsEditScope(doc, "RiveTT: Create Stair");
            var stairsId = scope.Start(baseLevel.Id, topLevel.Id);

            var runs = new List<StairsRun>();
            var landingIds = new List<long>();
            var landingProblems = new List<object>();
            var landingMessages = new List<string>();
            // The plan's riser warnings are ESTIMATES for the preview. Once Revit has built the
            // stair, its own DesiredRisersNumber is the truth, checked below: repeating the
            // estimate told the caller to shorten runs that were exactly right (2026-09-27).
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "RiveTT: Stair Runs"))
            {
                tx.Start();
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

                if (stairsType != null)
                {
                    var stairs = doc.GetElement(stairsId);
                    if (stairs != null && stairs.GetTypeId() != stairsType.Id)
                        stairs.ChangeTypeId(stairsType.Id);
                }

                var stairsBaseFt = baseLevel.Elevation + BaseOffsetFt(doc.GetElement(stairsId));
                StairsRun? previousRun = null;
                for (var i = 0; i < runInputs.Count; i++)
                {
                    // StairsRun.TopElevation is relative to the stairs base; the location line
                    // wants the run's base in MODEL coordinates.
                    var runBaseFt = previousRun == null ? stairsBaseFt : stairsBaseFt + previousRun.TopElevation;
                    var line = Line.CreateBound(
                        new XYZ(runInputs[i].X0 / MmPerFoot, runInputs[i].Y0 / MmPerFoot, runBaseFt),
                        new XYZ(runInputs[i].X1 / MmPerFoot, runInputs[i].Y1 / MmPerFoot, runBaseFt));
                    var run = StairsRun.CreateStraightRun(doc, stairsId, line, StairsRunJustification.Center);
                    if (widthMm > 0) run.ActualRunWidth = widthMm / MmPerFoot;
                    runs.Add(run);

                    if (previousRun != null)
                    {
                        var junction = plan.Junctions[i - 1];
                        string? problem = null;
                        try
                        {
                            if (!StairsLanding.CanCreateAutomaticLanding(doc, previousRun.Id, run.Id))
                            {
                                problem = $"Revit cannot create an automatic landing between run {i} and run {i + 1} " +
                                          $"({junction.Layout} layout, {junction.GapMm:F0} mm between the end of run {i} " +
                                          $"and the start of run {i + 1}).";
                            }
                            else
                            {
                                // Revit may produce several landings for one junction.
                                var landings = StairsLanding.CreateAutomaticLanding(doc, previousRun.Id, run.Id);
                                if (landings == null || landings.Count == 0)
                                    problem = $"Revit created no landing between run {i} and run {i + 1}.";
                                else
                                    landingIds.AddRange(landings.Select(ToolHelpers.GetElementIdValue));
                            }
                        }
                        catch (Exception exception)
                        {
                            problem = $"Automatic landing between run {i} and run {i + 1} failed: {exception.Message}";
                        }

                        if (problem != null)
                        {
                            landingMessages.Add(problem);
                            landingProblems.Add(new
                            {
                                betweenRuns = new[] { i, i + 1 },
                                layout = junction.Layout,
                                gapMm = junction.GapMm,
                                angleDeg = junction.AngleDeg,
                                problem
                            });
                        }
                    }

                    previousRun = run;
                }

                if (landingProblems.Count > 0 && requireLandings)
                {
                    // Nothing half-built: a stair whose runs are not connected is what the
                    // field session got, and it had to be rebuilt by hand.
                    tx.RollBack();
                    scope.Cancel();
                    scope = null;
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        "The stair was NOT created: Revit could not join the runs with a landing. " +
                        string.Join(" ", landingMessages),
                        suggestion: "Put the start of each run next to the end of the previous one (U-shaped: offset by " +
                                    "the run width plus the well; L-shaped: at the corner), or pass requireLandings=false " +
                                    "to keep the runs and draw the landing as a floor.",
                        context: new Dictionary<string, object>
                        {
                            ["stage"] = "landing",
                            ["modelChanged"] = false,
                            ["rolledBack"] = true,
                            ["landingProblems"] = landingProblems,
                            ["plan"] = DescribePlan(plan)
                        });
                }
                if (landingProblems.Count > 0)
                    warnings.Add($"{landingProblems.Count} landing(s) could not be created (requireLandings=false): " +
                                 "the runs are not connected; draw the landing as a floor.");

                if (tx.Commit() != TransactionStatus.Committed)
                {
                    scope.Cancel();
                    scope = null;
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the stair runs: {TransactionFailureHandling.Describe(txFailures)}",
                        // Both refusals observed in practice came from these two,
                        // and the raw Revit text ("Impossible de créer l'escalier")
                        // names neither.
                        suggestion: "Two usual causes: (1) the run length does not fit the riser count needed " +
                                    $"for {heightFt * MmPerFoot:F0} mm — allow roughly tread depth x " +
                                    "(risers - 1); (2) the stair type is a catalogued precast type with a " +
                                    "fixed height and step count, which refuses an arbitrary level-to-level " +
                                    "height — pick a cast-in-place or assembled type from " +
                                    "list_system_types(OST_Stairs).",
                        context: TransactionFailureHandling.ToContext(txFailures, null));
                }
            }

            // Commit with a preprocessor: a stair commonly raises warnings, and an
            // unhandled one would open a modal dialog and freeze the MCP bridge.
            var scopeFailures = new TransactionFailureHandling.FailureCapture();
            scope.Commit(scopeFailures);
            scope = null;

            var created = doc.GetElement(stairsId) as Stairs;
            if (created == null)
            {
                // The scope's failure processing rolled the whole stair back.
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the stair when closing the edit scope: {TransactionFailureHandling.Describe(scopeFailures)}",
                    suggestion: "Nothing was created. Read errorGroups for the elements involved; the usual cause is a run " +
                                "or landing that collides with another component of the stair.",
                    context: TransactionFailureHandling.ToContext(scopeFailures, null));
            }

            var actualRisers = created.ActualRisersNumber;
            var desiredRisers = created.DesiredRisersNumber;

            // Read back what Revit built, run by run: the answer must report what was
            // applied, not what was asked.
            var runReports = new List<object>();
            for (var i = 0; i < runs.Count; i++)
            {
                var run = doc.GetElement(runs[i].Id) as StairsRun;
                runReports.Add(new
                {
                    runId = ToolHelpers.GetElementIdValue(runs[i].Id),
                    baseElevationMm = run == null ? (double?)null : Math.Round(ReadOrZero(() => run.BaseElevation) * MmPerFoot, 1),
                    topElevationMm = run == null ? (double?)null : Math.Round(ReadOrZero(() => run.TopElevation) * MmPerFoot, 1),
                    actualRisers = run == null ? (int?)null : ReadIntOrNull(() => run.ActualRisersNumber),
                    estimatedRisers = plan.Runs[i].EstimatedRisers,
                    elevationsRelativeTo = "stairs base"
                });
            }

            // Railings are created OUTSIDE the edit scope, and Revit creates one per
            // side of the stair. Most stair types create their own, in which case
            // Railing.Create fails with "already has associated railings" — which
            // reads as a tool error when it really means "nothing to do".
            var railingIds = new List<long>();
            string? railingError = null;
            var existingRailings = new List<long>();
            try
            {
                existingRailings.AddRange(created.GetAssociatedRailings().Select(ToolHelpers.GetElementIdValue));
            }
            catch
            {
                // Reading the associated railings is context, never the answer.
            }

            if (railingTypeId > 0 && existingRailings.Count > 0)
            {
                railingIds.AddRange(existingRailings);
                warnings.Add($"The stair type already created {existingRailings.Count} railing(s), so " +
                             "railingTypeId was not applied. Retype them with change_element_type if needed.");
            }
            else if (railingTypeId > 0)
            {
                try
                {
                    using var railingTx = new Transaction(doc, "RiveTT: Stair Railing");
                    railingTx.Start();
                    var railingFailures = TransactionFailureHandling.SuppressWarnings(railingTx);
                    var railings = Railing.Create(doc, stairsId, ToolHelpers.ToElementId(railingTypeId),
                        RailingPlacementPosition.Treads);
                    if (railingTx.Commit() == TransactionStatus.Committed)
                    {
                        if (railings != null)
                            railingIds.AddRange(railings.Select(ToolHelpers.GetElementIdValue));
                    }
                    else
                    {
                        railingError = "Revit rolled back the railing: " + TransactionFailureHandling.Describe(railingFailures);
                    }
                }
                catch (Exception exception)
                {
                    railingError = exception.Message;
                }
            }
            else
            {
                railingIds.AddRange(existingRailings);
            }

            if (actualRisers > 0 && desiredRisers > 0 && actualRisers != desiredRisers)
            {
                // Direction matters: MORE risers than needed means the run is too
                // long and overshoots the level. Telling the caller to lengthen a run
                // that is already too long is worse than saying nothing.
                var treadMm = created.ActualTreadDepth * MmPerFoot;
                var deltaRisers = actualRisers - desiredRisers;
                var correctionMm = treadMm > 0 ? Math.Abs(deltaRisers) * treadMm : 0;

                warnings.Add(deltaRisers > 0
                    ? $"The stair has {actualRisers} risers but only {desiredRisers} are needed to reach " +
                      $"'{topLevel.Name}': the runs are too LONG and overshoot the level" +
                      (correctionMm > 0 ? $" — shorten them by about {correctionMm:F0} mm in total" : "") +
                      ". Revit created the stair anyway."
                    : $"The stair has {actualRisers} risers but needs {desiredRisers} to reach " +
                      $"'{topLevel.Name}': the runs are too SHORT and stop below the level" +
                      (correctionMm > 0 ? $" — lengthen them by about {correctionMm:F0} mm in total" : "") +
                      ", or add a run. Revit created the stair anyway.");
            }

            return RiveTTResult<object>.Ok(new
            {
                message = $"Created a stair from '{baseLevel.Name}' to '{topLevel.Name}' " +
                          $"({runs.Count} run(s), {landingIds.Count} landing(s), {actualRisers} riser(s) of {desiredRisers} required).",
                stairsId = ToolHelpers.GetElementIdValue(stairsId),
                runIds = runs.Select(run => ToolHelpers.GetElementIdValue(run.Id)).ToList(),
                runs = runReports,
                landingIds,
                landingProblems,
                actualRiserCount = actualRisers,
                desiredRiserCount = desiredRisers,
                reachesTopLevel = desiredRisers == 0 || actualRisers == desiredRisers,
                actualTreadDepthMm = Math.Round(created.ActualTreadDepth * MmPerFoot, 1),
                actualRiserHeightMm = Math.Round(created.ActualRiserHeight * MmPerFoot, 1),
                railingIds,
                railingError,
                scopeWarnings = scopeFailures.Warnings.Take(10).ToList(),
                warnings
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"create_stair could not create the stair: {exception.Message}",
                suggestion: "A component stair needs two distinct levels, a run that fits in the model, and no " +
                            "other edit scope open. Check the run geometry and retry with dryRun first.");
        }
        finally
        {
            // Never leave an edit scope open: Revit would stay in stair edit mode
            // for the rest of the session.
            try { scope?.Cancel(); } catch { }
        }
    }

    private static object DescribePlan(StairRunPlanner.Plan plan) => new
    {
        heightMm = plan.HeightMm,
        desiredRisers = plan.DesiredRisers,
        riserHeightMm = plan.RiserHeightMm,
        treadDepthMm = Math.Round(plan.TreadDepthMm, 1),
        widthMm = Math.Round(plan.WidthMm, 1),
        estimatedTotalRisers = plan.EstimatedTotalRisers,
        runs = plan.Runs.Select(run => new
        {
            index = run.Index + 1,
            lengthMm = run.LengthMm,
            estimatedTreads = run.EstimatedTreads,
            estimatedRisers = run.EstimatedRisers,
            estimatedBaseMm = run.EstimatedBaseMm,
            estimatedTopMm = run.EstimatedTopMm
        }).ToArray(),
        junctions = plan.Junctions.Select(junction => new
        {
            betweenRuns = new[] { junction.FromRun + 1, junction.ToRun + 1 },
            layout = junction.Layout,
            gapMm = junction.GapMm,
            angleDeg = junction.AngleDeg,
            bridgeable = junction.Bridgeable,
            problem = junction.Problem
        }).ToArray(),
        elevationsRelativeTo = "base level"
    };

    private static bool TryReadRuns(JToken? token, out List<StairRunPlanner.RunInput> runs, out string error)
    {
        runs = new List<StairRunPlanner.RunInput>();
        error = "";

        if (token is not JArray array || array.Count == 0)
        {
            error = "runs is required: [{p0:{x,y}, p1:{x,y}}, ...] in mm";
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JObject run || run["p0"] is not JObject start || run["p1"] is not JObject end)
            {
                error = "each run must be {p0:{x,y}, p1:{x,y}} in mm";
                return false;
            }

            // Plan coordinates only: the elevation of every run is derived from the base
            // level and the runs below it, never from a z the caller would have to compute.
            var input = new StairRunPlanner.RunInput(
                start["x"]?.Value<double>() ?? 0,
                start["y"]?.Value<double>() ?? 0,
                end["x"]?.Value<double>() ?? 0,
                end["y"]?.Value<double>() ?? 0);

            if (input.Length < 1)
            {
                error = "a run cannot have coincident start and end points";
                return false;
            }

            runs.Add(input);
        }

        return true;
    }

    /// <summary>
    /// The type StairsEditScope.Start gives a new stair — the document default — so the preview
    /// plans with the tread and riser of the stair that will really be built.
    /// </summary>
    private static StairsType? DefaultStairsType(Document doc)
    {
        try
        {
            var defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.StairsType);
            if (defaultId != ElementId.InvalidElementId && doc.GetElement(defaultId) is StairsType byDefault)
                return byDefault;
        }
        catch
        {
            // Fall back to any stair type: an estimate is better than none.
        }
        return new FilteredElementCollector(doc)
            .OfClass(typeof(StairsType))
            .Cast<StairsType>()
            .FirstOrDefault();
    }

    private static double BaseOffsetFt(Element? stairs)
    {
        try
        {
            var parameter = stairs?.get_Parameter(BuiltInParameter.STAIRS_BASE_OFFSET);
            return parameter != null && parameter.HasValue ? parameter.AsDouble() : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double ReadOrZero(Func<double> read)
    {
        try { return read(); } catch { return 0; }
    }

    private static int? ReadIntOrNull(Func<int> read)
    {
        try { return read(); } catch { return null; }
    }
}
