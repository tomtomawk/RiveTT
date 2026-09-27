using System;
using System.Collections.Generic;
using System.Linq;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Plans a multi-run straight stair in plan and elevation BEFORE Revit is asked to build it,
/// with plain millimetres so it can be unit-tested without Revit.
///
/// Field report of 2026-09-24, bug 1.1: two runs asked from RDC to R+1 (2 890 mm) produced
/// 9 risers instead of 17, both runs going from 0 to 1 530 mm, no landing — while the preview
/// had promised "1 automatic landing, 17 risers". Every run had been created at the base
/// elevation. The builder now stacks each run on the top of the previous one; this planner
/// gives the preview the same arithmetic, and refuses the junctions no landing can bridge.
///
/// Revit derives a straight run's treads from its length rounded to a multiple of the tread
/// depth (StairsRun.CreateStraightRun). The riser count per run is therefore an ESTIMATE:
/// the stair type's begin/end-with-riser options can shift it by one.
/// </summary>
public static class StairRunPlanner
{
    public sealed record RunInput(double X0, double Y0, double X1, double Y1)
    {
        public double Length => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
        public double DirX => (X1 - X0) / Length;
        public double DirY => (Y1 - Y0) / Length;
    }

    public sealed record RunPlan(int Index, double LengthMm, int EstimatedTreads, int EstimatedRisers,
        double EstimatedBaseMm, double EstimatedTopMm);

    public sealed record Junction(int FromRun, int ToRun, string Layout, double GapMm, double AngleDeg,
        bool Bridgeable, string? Problem);

    public sealed record Plan(
        double HeightMm, int DesiredRisers, double RiserHeightMm, double TreadDepthMm, double WidthMm,
        IReadOnlyList<RunPlan> Runs, IReadOnlyList<Junction> Junctions,
        int EstimatedTotalRisers, IReadOnlyList<string> Warnings, IReadOnlyList<string> Problems);

    /// <summary>
    /// A landing joins the end of one run to the start of the next. Farther than two run
    /// widths plus this margin apart, no single landing can span them.
    /// </summary>
    public const double MaxJunctionMarginMm = 2000;

    public static Plan Build(IReadOnlyList<RunInput> runs, double heightMm, double maxRiserMm,
        double treadDepthMm, double widthMm)
    {
        var warnings = new List<string>();
        var problems = new List<string>();

        var desired = maxRiserMm > 0 ? (int)Math.Ceiling(heightMm / maxRiserMm - 1e-9) : 0;
        var riser = desired > 0 ? heightMm / desired : 0;

        var plans = new List<RunPlan>();
        var elevation = 0.0;
        for (var i = 0; i < runs.Count; i++)
        {
            var length = runs[i].Length;
            var treads = treadDepthMm > 0 ? (int)Math.Round(length / treadDepthMm, MidpointRounding.AwayFromZero) : 0;
            var risers = treadDepthMm > 0 ? treads + 1 : 0;
            var top = elevation + risers * riser;
            plans.Add(new RunPlan(i, Math.Round(length, 1), treads, risers, Math.Round(elevation, 1), Math.Round(top, 1)));
            elevation = top;
        }

        var junctions = new List<Junction>();
        for (var i = 0; i + 1 < runs.Count; i++)
            junctions.Add(Classify(i, runs[i], runs[i + 1], widthMm));
        foreach (var junction in junctions.Where(j => !j.Bridgeable))
            problems.Add(junction.Problem!);

        var total = plans.Sum(p => p.EstimatedRisers);
        if (desired > 0 && treadDepthMm > 0 && total != desired)
        {
            var delta = desired - total;
            warnings.Add(delta > 0
                ? $"The runs give about {total} risers but {desired} are needed for {heightMm:F0} mm: the stair would " +
                  $"stop about {delta * riser:F0} mm below the top level. Lengthen the runs by about " +
                  $"{delta * treadDepthMm:F0} mm in total (one tread of {treadDepthMm:F0} mm per missing riser)."
                : $"The runs give about {total} risers but only {desired} are needed for {heightMm:F0} mm: the stair would " +
                  $"overshoot the top level. Shorten the runs by about {-delta * treadDepthMm:F0} mm in total.");
        }

        return new Plan(Math.Round(heightMm, 1), desired, Math.Round(riser, 1), treadDepthMm, widthMm,
            plans, junctions, total, warnings, problems);
    }

    /// <summary>
    /// The layout of a junction between run <paramref name="i"/> and the next: straight
    /// (collinear, same direction), L (perpendicular), U (antiparallel, side by side), and
    /// whether one landing can plausibly bridge it.
    /// </summary>
    public static Junction Classify(int i, RunInput from, RunInput to, double widthMm)
    {
        var gapX = to.X0 - from.X1;
        var gapY = to.Y0 - from.Y1;
        var gap = Math.Sqrt(gapX * gapX + gapY * gapY);
        var dot = from.DirX * to.DirX + from.DirY * to.DirY;
        var angle = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180 / Math.PI;

        string layout;
        if (angle < 5) layout = "straight";
        else if (Math.Abs(angle - 90) < 5) layout = "L";
        else if (angle > 175) layout = "U";
        else layout = "angled";

        var width = widthMm > 0 ? widthMm : 1000;
        var maxGap = 2 * width + MaxJunctionMarginMm;
        string? problem = null;

        // Run k+1 must leave from beyond the end of run k along run k's direction for a
        // straight junction; starting behind it means the two runs overlap in plan.
        var along = gapX * from.DirX + gapY * from.DirY;
        if (layout == "straight" && along < -1)
            problem = $"Run {i + 2} starts {-along:F0} mm BEHIND the end of run {i + 1}: the two runs overlap in plan. " +
                      $"Start run {i + 2} at or beyond ({from.X1:F0}, {from.Y1:F0}) mm, one landing depth further.";
        else if (gap > maxGap)
            problem = $"Run {i + 2} starts {gap:F0} mm from the end of run {i + 1}; no single landing bridges more than " +
                      $"about {maxGap:F0} mm (2 x width + {MaxJunctionMarginMm:F0} mm). Move run {i + 2} next to the end of run {i + 1}.";

        return new Junction(i, i + 1, layout, Math.Round(gap, 1), Math.Round(angle, 1), problem == null, problem);
    }
}
