using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RiveTT.Core.Design;

/// <summary>
/// Checks one dwelling against the rule set and says, rule by rule, what passed, what did not,
/// with the measured value — and what could not be evaluated, and why.
///
/// Field report of 2026-09-24: the charter was read once, left the context, and was not applied
/// before the third version of the plans (open entrance, ETEL, clearances, corridor widths). "The
/// agent no longer has to remember the charter: the validator carries it." A rule that cannot be
/// evaluated from the data at hand is reported under notEvaluated, never as passed.
/// </summary>
public static class DwellingValidator
{
    /// <summary>Tolerance for two room boundaries to count as the same line, in mm.</summary>
    private const double SharedEdgeToleranceMm = 30;

    public static JObject Validate(DwellingSpec spec, DwellingRuleSet rules, double gridStepMm = 20)
    {
        var checks = new JArray();
        var notEvaluated = new JArray();
        var notApplicable = new JArray();
        var notes = new List<string>();
        var rank = spec.TypologyRank;

        void Check(string ruleId, bool ok, object? value, object? threshold, string detail, IEnumerable<long>? ids = null)
        {
            var rule = rules[ruleId];
            if (!rule.Enabled) return;
            var entry = new JObject
            {
                ["rule"] = rule.Id,
                ["ok"] = ok,
                ["severity"] = rule.Severity.ToString().ToLowerInvariant(),
                ["label"] = rule.Label,
                ["source"] = rule.Source,
                ["detail"] = detail
            };
            if (value != null) entry["value"] = JToken.FromObject(value);
            if (threshold != null) entry["threshold"] = JToken.FromObject(threshold);
            var idList = ids?.ToList();
            if (idList != null && idList.Count > 0) entry["elementIds"] = new JArray(idList);
            checks.Add(entry);
        }

        // Missing data: the rule applies but cannot be decided. Never reported as passed.
        void Skip(string ruleId, string reason)
        {
            var rule = rules[ruleId];
            if (!rule.Enabled) return;
            notEvaluated.Add(new JObject { ["rule"] = rule.Id, ["label"] = rule.Label, ["reason"] = reason });
        }

        // The rule does not apply to this dwelling (typology, no such room by design).
        void NotApplicable(string ruleId, string reason)
        {
            var rule = rules[ruleId];
            if (!rule.Enabled) return;
            notApplicable.Add(new JObject { ["rule"] = rule.Id, ["reason"] = reason });
        }

        bool On(string ruleId) => rules[ruleId].Enabled;

        var totalArea = spec.Rooms.Sum(r => r.AreaM2);
        var entries = spec.RoomsOf(RoomKind.Entry).ToList();
        var livings = spec.RoomsOf(RoomKind.Living).ToList();
        var bedrooms = spec.RoomsOf(RoomKind.Bedroom).ToList();
        var circulations = spec.RoomsOf(RoomKind.Circulation).ToList();
        var wetRooms = spec.Rooms.Where(r => r.Kind is RoomKind.ShowerRoom or RoomKind.Bathroom).ToList();

        var withoutFootprint = spec.Equipment.Count(e => e.Footprint == null || e.Footprint.Count < 3);
        if (withoutFootprint > 0)
            notes.Add($"{withoutFootprint} equipment item(s) have no footprint: they are NOT deducted from the free circles and rectangles.");
        notes.Add("Door swings are not deducted from the free areas (\"hors débattement de porte\"): check them on the plan.");

        // ── ENTREE_OUVERTE_SEJOUR ────────────────────────────────────────────
        if (On("ENTREE_OUVERTE_SEJOUR"))
        {
            if (entries.Count == 0 || livings.Count == 0)
                Skip("ENTREE_OUVERTE_SEJOUR", entries.Count == 0 ? "no room of kind Entry (named Entrée)" : "no living room (Séjour)");
            else
            {
                var minOpening = rules["ENTREE_OUVERTE_SEJOUR"].Number("minOpeningMm");
                var best = 0.0;
                string? blockedBy = null;
                foreach (var entry in entries)
                    foreach (var living in livings)
                    {
                        var shared = PlanGeometry.SharedSegments(entry.Polygon, living.Polygon, SharedEdgeToleranceMm);
                        var sharedLength = shared.Sum(s => (s.B - s.A).Length);
                        var blocked = shared.Sum(s => spec.Partitions.Sum(p =>
                            PlanGeometry.CoveredLength(s, (p.P0, p.P1), p.ThicknessMm / 2 + SharedEdgeToleranceMm)));
                        var doorsOnIt = spec.Doors.Where(d => shared.Any(s => PlanGeometry.DistanceToSegment(d.At, s.A, s.B) < 150)).ToList();
                        var open = Math.Max(0, sharedLength - blocked);
                        if (doorsOnIt.Count > 0) { blockedBy = $"door {doorsOnIt[0].Family} {doorsOnIt[0].Type}".Trim(); open = 0; }
                        else if (blocked > 1) blockedBy = "partition";
                        best = Math.Max(best, open);
                    }
                Check("ENTREE_OUVERTE_SEJOUR", best >= minOpening, Math.Round(best), minOpening,
                    best >= minOpening
                        ? $"entrance open onto the living room over {best:F0} mm"
                        : best > 0
                            ? $"only {best:F0} mm of open boundary between entrance and living room"
                            : blockedBy != null
                                ? $"entrance and living room are separated by a {blockedBy}"
                                : "entrance and living room share no boundary (a wall between them, or not adjacent)",
                    entries.Concat(livings).Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
            }
        }

        // ── ENTREE_GABARIT ───────────────────────────────────────────────────
        if (On("ENTREE_GABARIT"))
        {
            if (entries.Count == 0) Skip("ENTREE_GABARIT", "no room of kind Entry (named Entrée)");
            else
            {
                var rule = rules["ENTREE_GABARIT"];
                var w = rule.Number("widthMm");
                var d = rule.Number("depthMm");
                var fit = entries.Select(e => (Room: e, Fit: Field(e, spec, StepFor(e, gridStepMm)).FitRectangle(w, d))).ToList();
                var hit = fit.FirstOrDefault(f => f.Fit != null);
                Check("ENTREE_GABARIT", hit.Fit != null, null, $"{w:F0} x {d:F0} mm",
                    hit.Fit != null
                        ? $"fits in '{hit.Room.Name}' at ({hit.Fit.Value.Min.X:F0}, {hit.Fit.Value.Min.Y:F0}) mm{(hit.Fit.Value.Rotated ? ", rotated 90°" : "")}"
                        : $"a free {w:F0} x {d:F0} mm rectangle fits nowhere in the entrance (checked in both orientations; not checked to be in front of the landing door)",
                    entries.Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
            }
        }

        // ── ETEL_ENTREE ──────────────────────────────────────────────────────
        if (On("ETEL_ENTREE"))
        {
            if (entries.Count == 0) Skip("ETEL_ENTREE", "no room of kind Entry (named Entrée)");
            else
            {
                var rule = rules["ETEL_ENTREE"];
                var prefix = rule.Text("familyPrefix");
                var maxDistance = rule.Number("maxDistanceMm");
                var etels = spec.Equipment.Where(e => StartsWith(e.Family, prefix)).ToList();
                var inEntry = etels.FirstOrDefault(e => IsIn(e, entries, maxDistance));
                Check("ETEL_ENTREE", inEntry != null, etels.Count, null,
                    inEntry != null
                        ? $"{inEntry.Family} found in the entrance"
                        : etels.Count == 0 ? $"no {prefix}* family in the dwelling" : $"{etels.Count} {prefix}* found, none in the entrance",
                    inEntry?.ElementId != null ? new[] { inEntry.ElementId.Value } : null);
            }
        }

        // ── CIRC_INT_LARGEUR ─────────────────────────────────────────────────
        if (On("CIRC_INT_LARGEUR"))
        {
            // A dwelling without a corridor (a T2 whose bedroom opens on the living room) is not
            // missing data: the rule has nothing to measure.
            if (circulations.Count == 0) NotApplicable("CIRC_INT_LARGEUR", "no corridor (no room named Dgt, Dégagement, Circulation)");
            foreach (var corridor in circulations)
            {
                var rule = rules["CIRC_INT_LARGEUR"];
                var min = rule.Number("minMm");
                var field = Field(corridor, spec, StepFor(corridor, gridStepMm));
                var (regions, _) = field.PassageRegions(min);
                var widest = field.LargestCircle().DiameterMm;
                var ok = regions == 1;
                var detail = regions switch
                {
                    0 => $"'{corridor.Name}' is narrower than {min:F0} mm everywhere",
                    1 => $"a {min:F0} mm wide disc can travel through '{corridor.Name}' (widest free circle {widest:F0} mm)",
                    _ => $"'{corridor.Name}' narrows below {min:F0} mm and splits into {regions} parts"
                };
                Check("CIRC_INT_LARGEUR", ok, Math.Round(widest), $"≥ {min:F0} mm", detail,
                    corridor.ElementId != null ? new[] { corridor.ElementId.Value } : null);

                if (On("CIRC_INT_LARGEUR_MAX"))
                {
                    // Wider than the aim over a real length, not just the widening at an L corner
                    // (an L of width w holds a circle of about 1.17 w in its corner).
                    var aim = rules["CIRC_INT_LARGEUR_MAX"].Number("targetMaxMm");
                    var margin = rules["CIRC_INT_LARGEUR_MAX"].Number("marginMm");
                    var (_, wideArea) = field.PassageRegions(aim + margin);
                    Check("CIRC_INT_LARGEUR_MAX", wideArea <= 0.1, Math.Round(widest), $"≈ {aim:F0} mm",
                        wideArea <= 0.1
                            ? $"'{corridor.Name}' stays close to {aim:F0} mm"
                            : $"'{corridor.Name}' is wider than {aim + margin:F0} mm over part of its length: space lost to circulation",
                        corridor.ElementId != null ? new[] { corridor.ElementId.Value } : null);
                }
            }
        }

        // ── RATIO_CIRCULATION ────────────────────────────────────────────────
        if (On("RATIO_CIRCULATION"))
        {
            if (totalArea <= 0) Skip("RATIO_CIRCULATION", "no room area");
            else
            {
                var max = rules["RATIO_CIRCULATION"].Number("max");
                var circulationArea = entries.Concat(circulations).Sum(r => r.AreaM2);
                var ratio = circulationArea / totalArea;
                Check("RATIO_CIRCULATION", ratio <= max + 1e-9, Math.Round(ratio, 3), max,
                    $"entrance + corridors = {circulationArea:F2} m² of {totalArea:F2} m² ({ratio:P0})");
            }
        }

        // ── CHAMBRE_DEPUIS_DGT ───────────────────────────────────────────────
        if (On("CHAMBRE_DEPUIS_DGT"))
        {
            var from = (int)rules["CHAMBRE_DEPUIS_DGT"].Number("fromTypology");
            if (rank == null) Skip("CHAMBRE_DEPUIS_DGT", "typology unknown");
            else if (rank < from) NotApplicable("CHAMBRE_DEPUIS_DGT", $"applies from T{from}");
            else if (!spec.Doors.Any(d => d.Rooms.Count >= 2))
                Skip("CHAMBRE_DEPUIS_DGT", "no door carries the names of the rooms it joins (doors[].rooms)");
            else
            {
                var circulationNames = circulations.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var served = bedrooms.Where(b => spec.Doors.Any(d =>
                    d.Rooms.Contains(b.Name, StringComparer.OrdinalIgnoreCase) && d.Rooms.Any(circulationNames.Contains))).ToList();
                var notServed = bedrooms.Except(served).ToList();
                Check("CHAMBRE_DEPUIS_DGT", notServed.Count == 0, served.Count, bedrooms.Count,
                    notServed.Count == 0
                        ? "every bedroom opens onto a corridor"
                        : "not reached from a corridor: " + string.Join(", ", notServed.Select(b => $"'{b.Name}'")),
                    notServed.Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
            }
        }

        // ── WC_SEPARE ────────────────────────────────────────────────────────
        if (On("WC_SEPARE"))
        {
            var from = (int)rules["WC_SEPARE"].Number("fromTypology");
            if (rank == null) Skip("WC_SEPARE", "typology unknown");
            else if (rank < from) NotApplicable("WC_SEPARE", $"applies from T{from}");
            else
            {
                var wc = spec.RoomsOf(RoomKind.Wc).ToList();
                Check("WC_SEPARE", wc.Count > 0, wc.Count, 1,
                    wc.Count > 0 ? $"separate WC: '{wc[0].Name}'" : "no separate WC room (a WC inside the Sde counts only if the programme accepts it)");
            }
        }

        // ── CUISINE_TYPO ─────────────────────────────────────────────────────
        if (On("CUISINE_TYPO"))
        {
            var rule = rules["CUISINE_TYPO"];
            var kitchenRooms = spec.Rooms.Where(r => r.Kind is RoomKind.Living or RoomKind.Kitchen).ToList();
            if (rank == null) Skip("CUISINE_TYPO", "typology unknown");
            else if (kitchenRooms.Count == 0) Skip("CUISINE_TYPO", "no living room or kitchen");
            else if (spec.Equipment.Count == 0) Skip("CUISINE_TYPO", "no equipment in the data");
            else
            {
                var inKitchen = spec.Equipment.Where(e => IsIn(e, kitchenRooms, 300)).ToList();
                var missing = new List<string>();
                if (rank <= 1)
                {
                    if (!inKitchen.Any(e => StartsWith(e.Family, rule.Text("kitchenettePrefix")))) missing.Add("kitchenette");
                }
                else
                {
                    if (!inKitchen.Any(e => rule.Texts("sinkPrefixes").Any(p => StartsWith(e.Family, p)))) missing.Add("sink");
                    foreach (var appliance in rule.Texts("applianceTypes"))
                        if (!inKitchen.Any(e => StartsWith(e.Family, rule.Text("appliancePrefix")) && Contains(e.Type, appliance)))
                            missing.Add($"{appliance} space");
                    if (rank >= 3 && !inKitchen.Any(e => StartsWith(e.Family, rule.Text("worktopPrefix")))) missing.Add("extra worktop");
                }
                Check("CUISINE_TYPO", missing.Count == 0, null, $"T{rank}",
                    missing.Count == 0 ? $"kitchen equipment complete for a T{rank}" : "missing: " + string.Join(", ", missing));
            }
        }

        // ── CERCLE_150_SDE / CERCLE_150_SEJOUR ───────────────────────────────
        CircleCheck("CERCLE_150_SDE", wetRooms, "no shower room or bathroom (named Sde, Sdb)");
        CircleCheck("CERCLE_150_SEJOUR", livings, "no living room (Séjour)");

        void CircleCheck(string ruleId, List<SpecRoom> candidates, string missingReason)
        {
            if (!On(ruleId)) return;
            if (candidates.Count == 0) { Skip(ruleId, missingReason); return; }
            var diameter = rules[ruleId].Number("diameterMm");
            var measured = candidates.Select(r => (Room: r, Circle: Field(r, spec, StepFor(r, gridStepMm)).LargestCircleRefined()))
                .OrderByDescending(m => m.Circle.DiameterMm).ToList();
            var best = measured[0];
            // Refined below the grid step with exact distances: a 1 mm tolerance is rounding.
            var ok = best.Circle.DiameterMm >= diameter - 1;
            Check(ruleId, ok, Math.Round(best.Circle.DiameterMm), diameter,
                ok
                    ? $"a free Ø {diameter:F0} mm circle fits in '{best.Room.Name}' (centre {best.Circle.Center.X:F0}, {best.Circle.Center.Y:F0})"
                    : $"largest free circle: Ø {best.Circle.DiameterMm:F0} mm in '{best.Room.Name}'" +
                      (measured.Count > 1 ? $" (best of {measured.Count} rooms)" : ""),
                measured.Where(m => m.Room.ElementId != null).Select(m => m.Room.ElementId!.Value));
        }

        // ── CHAMBRE_PMR_LIT ──────────────────────────────────────────────────
        if (On("CHAMBRE_PMR_LIT"))
        {
            if (rank is <= 1) NotApplicable("CHAMBRE_PMR_LIT", "T1: the bed stands in the living room");
            else if (bedrooms.Count == 0) Skip("CHAMBRE_PMR_LIT", "no bedroom (named Chambre)");
            else
            {
                var rule = rules["CHAMBRE_PMR_LIT"];
                var w = rule.Number("widthMm");
                var d = rule.Number("depthMm");
                // Room capacity, furniture ignored: the bed is what goes there.
                var fits = bedrooms.Where(b => new ClearanceField(b.Polygon, b.Holes.Cast<IReadOnlyList<Pt>>(), StepFor(b, gridStepMm))
                    .FitRectangle(w, d) != null).ToList();
                Check("CHAMBRE_PMR_LIT", fits.Count > 0, fits.Count, $"{w:F0} x {d:F0} mm",
                    fits.Count > 0
                        ? $"'{fits[0].Name}' can hold the bed and its clearances"
                        : $"no bedroom holds a free {w:F0} x {d:F0} mm rectangle: largest bedroom {bedrooms.Max(b => b.AreaM2):F2} m²",
                    bedrooms.Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
            }
        }

        // ── WC_PMR / DOUCHE_PLAIN_PIED ───────────────────────────────────────
        if (On("WC_PMR"))
        {
            var rule = rules["WC_PMR"];
            var wcs = spec.Equipment.Where(e => StartsWith(e.Family, rule.Text("familyPrefix"))).ToList();
            if (wcs.Count == 0) Skip("WC_PMR", spec.Equipment.Count == 0 ? "no equipment in the data" : "no WC family in the dwelling");
            else
            {
                var pmr = wcs.FirstOrDefault(e => Contains(e.Type, rule.Text("typeContains")));
                Check("WC_PMR", pmr != null, wcs.Count, null,
                    pmr != null ? $"{pmr.Family} · {pmr.Type}" : $"{wcs.Count} WC, none of a {rule.Text("typeContains")} type",
                    pmr?.ElementId != null ? new[] { pmr.ElementId.Value } : null);
            }
        }
        if (On("DOUCHE_PLAIN_PIED"))
        {
            var prefix = rules["DOUCHE_PLAIN_PIED"].Text("familyPrefix");
            if (spec.Equipment.Count == 0) Skip("DOUCHE_PLAIN_PIED", "no equipment in the data");
            else
            {
                var shower = spec.Equipment.FirstOrDefault(e => StartsWith(e.Family, prefix));
                Check("DOUCHE_PLAIN_PIED", shower != null, null, null,
                    shower != null ? $"{shower.Family} · {shower.Type}" : $"no {prefix} family",
                    shower?.ElementId != null ? new[] { shower.ElementId.Value } : null);
            }
        }

        // ── PORTES_PASSAGE ───────────────────────────────────────────────────
        if (On("PORTES_PASSAGE"))
        {
            if (spec.Doors.Count == 0) Skip("PORTES_PASSAGE", "no door in the data");
            else
            {
                var rule = rules["PORTES_PASSAGE"];
                var landingMarker = DwellingSpec.Normalize(rule.Text("landingFamilyContains"));
                var wrong = spec.Doors.Where(d =>
                {
                    var landing = DwellingSpec.Normalize(d.Family).Contains(landingMarker);
                    var expected = landing ? rule.Text("landingType") : rule.Text("interiorType");
                    return !string.Equals(d.Type.Trim(), expected, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                Check("PORTES_PASSAGE", wrong.Count == 0, spec.Doors.Count - wrong.Count, spec.Doors.Count,
                    wrong.Count == 0
                        ? "every door has the charter type"
                        : "other types: " + string.Join(", ", wrong.Take(8).Select(d => $"{d.Family} · {d.Type}")),
                    wrong.Where(d => d.ElementId != null).Select(d => d.ElementId!.Value));
            }
        }

        // ── areas ────────────────────────────────────────────────────────────
        if (On("SURF_CHAMBRE"))
        {
            var min = rules["SURF_CHAMBRE"].Number("minM2");
            if (bedrooms.Count == 0) NotApplicable("SURF_CHAMBRE", "no bedroom");
            foreach (var bedroom in bedrooms)
                Check("SURF_CHAMBRE", bedroom.AreaM2 >= min, Math.Round(bedroom.AreaM2, 2), min, $"'{bedroom.Name}': {bedroom.AreaM2:F2} m²",
                    bedroom.ElementId != null ? new[] { bedroom.ElementId.Value } : null);
        }
        if (On("SURF_SEJOUR_T2"))
        {
            if (rank == null) Skip("SURF_SEJOUR_T2", "typology unknown");
            else if (rank != 2) NotApplicable("SURF_SEJOUR_T2", "applies to T2 only");
            else if (livings.Count == 0) Skip("SURF_SEJOUR_T2", "no living room");
            else
            {
                var min = rules["SURF_SEJOUR_T2"].Number("minM2");
                var area = livings.Sum(r => r.AreaM2) + spec.RoomsOf(RoomKind.Kitchen).Sum(r => r.AreaM2);
                Check("SURF_SEJOUR_T2", area >= min, Math.Round(area, 2), min, $"living room + kitchen: {area:F2} m²");
            }
        }
        if (On("SURF_PIECE_PRINCIPALE"))
        {
            var min = rules["SURF_PIECE_PRINCIPALE"].Number("minM2");
            var principal = livings.Concat(bedrooms).ToList();
            if (principal.Count == 0) Skip("SURF_PIECE_PRINCIPALE", "no principal room (living room or bedroom)");
            var small = principal.Where(r => r.AreaM2 < min).ToList();
            if (principal.Count > 0)
                Check("SURF_PIECE_PRINCIPALE", small.Count == 0, Math.Round(principal.Min(r => r.AreaM2), 2), min,
                    small.Count == 0 ? "every principal room reaches the minimum (height / volume not checked)"
                                     : "below: " + string.Join(", ", small.Select(r => $"'{r.Name}' {r.AreaM2:F2} m²")),
                    small.Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
        }

        // ── consistency ──────────────────────────────────────────────────────
        if (On("TYPOLOGIE_COHERENTE"))
        {
            if (rank == null) Skip("TYPOLOGIE_COHERENTE", "typology unknown");
            else
            {
                var expected = Math.Max(0, rank.Value - 1);
                Check("TYPOLOGIE_COHERENTE", bedrooms.Count == expected, bedrooms.Count, expected,
                    $"T{rank}: {bedrooms.Count} bedroom(s), {expected} expected");
            }
        }
        if (On("PIECES_NOMMEES"))
        {
            var unknown = spec.Rooms.Where(r => r.Kind == RoomKind.Unknown).ToList();
            Check("PIECES_NOMMEES", unknown.Count == 0, spec.Rooms.Count - unknown.Count, spec.Rooms.Count,
                unknown.Count == 0 ? "every room is recognised" : "not recognised: " + string.Join(", ", unknown.Select(r => $"'{r.Name}'")),
                unknown.Where(r => r.ElementId != null).Select(r => r.ElementId!.Value));
        }
        if (On("SURFACE_PROGRAMME"))
        {
            if (spec.TargetAreaM2 is not > 0) NotApplicable("SURFACE_PROGRAMME", "no targetAreaM2 given");
            else
            {
                var tolerance = rules["SURFACE_PROGRAMME"].Number("tolerance");
                var gap = (totalArea - spec.TargetAreaM2.Value) / spec.TargetAreaM2.Value;
                Check("SURFACE_PROGRAMME", Math.Abs(gap) <= tolerance, Math.Round(totalArea, 2), spec.TargetAreaM2,
                    $"{totalArea:F2} m² for {spec.TargetAreaM2:F2} m² targeted ({gap:+0%;-0%;0%})");
            }
        }

        var failed = checks.Where(c => !c["ok"]!.Value<bool>()).ToList();
        return new JObject
        {
            ["dwelling"] = spec.Id,
            ["level"] = spec.Level,
            ["typology"] = spec.Typology,
            ["source"] = spec.Source,
            ["ruleSet"] = rules.Name,
            ["overridesApplied"] = new JArray(rules.OverridesApplied),
            ["compliant"] = Verdict(failed, notEvaluated),
            ["summary"] = new JObject
            {
                ["passed"] = checks.Count - failed.Count,
                ["errors"] = failed.Count(c => c["severity"]!.Value<string>() == "error"),
                ["warnings"] = failed.Count(c => c["severity"]!.Value<string>() == "warning"),
                ["notEvaluated"] = notEvaluated.Count,
                ["notApplicable"] = notApplicable.Count
            },
            ["roomsAreaM2"] = Math.Round(totalArea, 2),
            ["areaNote"] = "Sum of the room polygons: NOT a regulatory SHAB. For an opposable area use manage_area_plans.",
            ["rooms"] = new JArray(spec.Rooms.Select(r => new JObject
            {
                ["name"] = r.Name,
                ["kind"] = r.Kind.ToString(),
                ["areaM2"] = Math.Round(r.AreaM2, 2),
                ["elementId"] = r.ElementId
            })),
            ["checks"] = checks,
            ["notEvaluated"] = notEvaluated,
            ["notApplicable"] = notApplicable,
            ["notes"] = new JArray(notes)
        };
    }

    /// <summary>
    /// true only when no error-level rule failed AND every applicable rule could be decided;
    /// false as soon as one error-level rule failed; otherwise a string saying it is not known.
    /// </summary>
    private static JToken Verdict(List<JToken> failed, JArray notEvaluated)
    {
        if (failed.Any(c => c["severity"]!.Value<string>() == "error")) return false;
        if (notEvaluated.Count > 0) return $"unknown: {notEvaluated.Count} rule(s) could not be evaluated";
        return true;
    }

    /// <summary>10 mm in small rooms, where 20 mm would decide a Ø 1.50 m circle; the given step elsewhere.</summary>
    private static double StepFor(SpecRoom room, double step) => room.AreaM2 < 15 ? Math.Min(step, 10) : step;

    private static ClearanceField Field(SpecRoom room, DwellingSpec spec, double step)
    {
        var obstacles = spec.Equipment
            .Where(e => e.Footprint != null && e.Footprint.Count >= 3)
            .Where(e => e.Footprint!.Any(p => PlanGeometry.Contains(room.Polygon, p))
                        || PlanGeometry.Contains(room.Polygon, e.At))
            .Select(e => (IReadOnlyList<Pt>)e.Footprint!)
            // Columns and shafts inside the room outline are not floor.
            .Concat(room.Holes);
        return new ClearanceField(room.Polygon, obstacles, step);
    }

    private static bool IsIn(SpecEquipment equipment, IEnumerable<SpecRoom> rooms, double toleranceMm)
    {
        foreach (var room in rooms)
        {
            if (equipment.Room != null && string.Equals(equipment.Room, room.Name, StringComparison.OrdinalIgnoreCase)) return true;
            if (PlanGeometry.DistanceTo(room.Polygon, equipment.At) <= toleranceMm) return true;
        }
        return false;
    }

    private static bool StartsWith(string? value, string prefix) =>
        !string.IsNullOrEmpty(prefix) &&
        DwellingSpec.Normalize(value).StartsWith(DwellingSpec.Normalize(prefix), StringComparison.Ordinal);

    private static bool Contains(string? value, string part) =>
        !string.IsNullOrEmpty(part) &&
        DwellingSpec.Normalize(value).Contains(DwellingSpec.Normalize(part), StringComparison.Ordinal);
}
