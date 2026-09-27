using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RiveTT.Core.Design;

/// <summary>What a room is for, as far as the dwelling rules are concerned.</summary>
public enum RoomKind
{
    Unknown,
    Entry,
    Living,      // séjour, séjour/cuisine
    Kitchen,
    Bedroom,
    ShowerRoom,  // salle d'eau
    Bathroom,    // salle de bain
    Wc,
    Circulation, // dégagement, circulation intérieure
    Storage      // rangement, cellier, placard, dressing
}

public sealed class SpecRoom
{
    public string Name { get; set; } = "";
    public RoomKind Kind { get; set; }
    public List<Pt> Polygon { get; set; } = new();
    /// <summary>Inner loops (columns, shafts): outside the room, and obstacles for the free areas.</summary>
    public List<List<Pt>> Holes { get; set; } = new();
    public long? ElementId { get; set; }
    public double AreaM2 => Math.Max(0, PlanGeometry.Area(Polygon) - Holes.Sum(h => PlanGeometry.Area(h))) / 1_000_000;
}

/// <summary>A wall or partition segment, on its axis, with its thickness.</summary>
public sealed class SpecPartition
{
    public Pt P0 { get; set; }
    public Pt P1 { get; set; }
    public double ThicknessMm { get; set; } = 100;
    public string? Type { get; set; }
}

public sealed class SpecDoor
{
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public Pt At { get; set; }
    /// <summary>Names of the rooms on both sides, when known (Revit: FromRoom/ToRoom).</summary>
    public List<string> Rooms { get; set; } = new();
    public long? ElementId { get; set; }
}

public sealed class SpecEquipment
{
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public Pt At { get; set; }
    /// <summary>Visible footprint in plan, when known; an obstacle for the clearance checks.</summary>
    public List<Pt>? Footprint { get; set; }
    public string? Room { get; set; }
    public long? ElementId { get; set; }
}

/// <summary>
/// One dwelling, as designed (JSON from the agent, validate_spec) or as built (read back
/// from Revit, validate_dwelling). Coordinates in millimetres. The schema is the one of
/// docs/retex/2026-09-24/08_architecture-spec-json.md.
/// </summary>
public sealed class DwellingSpec
{
    public string Id { get; set; } = "";
    public string? Level { get; set; }
    public string? Typology { get; set; }
    public string? Building { get; set; }
    public double? TargetAreaM2 { get; set; }
    public List<SpecRoom> Rooms { get; } = new();
    public List<SpecPartition> Partitions { get; } = new();
    public List<SpecDoor> Doors { get; } = new();
    public List<SpecEquipment> Equipment { get; } = new();

    /// <summary>Where this spec came from: "json" or "revit".</summary>
    public string Source { get; set; } = "json";

    /// <summary>Number of bedrooms implied by the typology: T1 = 0, T2 = 1, T3 = 2...</summary>
    public int? TypologyRank
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Typology)) return null;
            var digits = new string(Typology.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : null;
        }
    }

    public IEnumerable<SpecRoom> RoomsOf(RoomKind kind) => Rooms.Where(r => r.Kind == kind);

    // ── parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses one dwelling object. Accepts polygons as [[x,y],...] or [{x,y},...], points
    /// as [x,y] or {x,y}. Collects every problem instead of stopping at the first one.
    /// </summary>
    public static DwellingSpec Parse(JObject json, List<string> errors)
    {
        var spec = new DwellingSpec
        {
            Id = json["id"]?.Value<string>() ?? "",
            Level = json["level"]?.Value<string>(),
            Typology = json["typology"]?.Value<string>(),
            Building = json["building"]?.Value<string>(),
            TargetAreaM2 = json["targetAreaM2"]?.Value<double?>()
        };
        var label = string.IsNullOrEmpty(spec.Id) ? "dwelling" : $"dwelling {spec.Id}";

        if (json["rooms"] is JArray rooms)
        {
            for (var i = 0; i < rooms.Count; i++)
            {
                if (rooms[i] is not JObject room) { errors.Add($"{label}: rooms[{i}] is not an object"); continue; }
                var name = room["name"]?.Value<string>() ?? "";
                var polygon = ParsePolygon(room["polygon"]);
                if (polygon == null || polygon.Count < 3)
                {
                    errors.Add($"{label}: room '{name}' (rooms[{i}]) needs a polygon of at least 3 points [[x,y],...] in mm");
                    continue;
                }
                var kindText = room["kind"]?.Value<string>();
                var holes = new List<List<Pt>>();
                if (room["holes"] is JArray holeArray)
                    foreach (var hole in holeArray)
                    {
                        var ring = ParsePolygon(hole);
                        if (ring == null || ring.Count < 3) errors.Add($"{label}: room '{name}' has a hole that is not a polygon");
                        else holes.Add(ring);
                    }
                spec.Rooms.Add(new SpecRoom
                {
                    Name = name,
                    Kind = kindText != null && Enum.TryParse<RoomKind>(kindText, true, out var kind) ? kind : Classify(name),
                    Polygon = polygon,
                    Holes = holes,
                    ElementId = room["elementId"]?.Value<long?>()
                });
            }
        }
        else errors.Add($"{label}: rooms is required");

        foreach (var (item, i) in Items(json["partitions"]))
        {
            var p0 = ParsePoint(item["p0"]);
            var p1 = ParsePoint(item["p1"]);
            if (p0 == null || p1 == null) { errors.Add($"{label}: partitions[{i}] needs p0 and p1"); continue; }
            spec.Partitions.Add(new SpecPartition
            {
                P0 = p0.Value, P1 = p1.Value,
                ThicknessMm = item["thicknessMm"]?.Value<double?>() ?? GuessThickness(item["type"]?.Value<string>()),
                Type = item["type"]?.Value<string>()
            });
        }

        foreach (var (item, i) in Items(json["doors"]))
        {
            var at = ParsePoint(item["at"]);
            if (at == null) { errors.Add($"{label}: doors[{i}] needs at [x,y]"); continue; }
            spec.Doors.Add(new SpecDoor
            {
                Family = item["family"]?.Value<string>() ?? "",
                Type = item["type"]?.Value<string>() ?? "",
                At = at.Value,
                Rooms = item["rooms"] is JArray names ? names.Select(n => n.Value<string>() ?? "").ToList() : new List<string>(),
                ElementId = item["elementId"]?.Value<long?>()
            });
        }

        foreach (var (item, i) in Items(json["equipment"]))
        {
            var at = ParsePoint(item["at"] ?? item["hostAt"]);
            var footprint = ParsePolygon(item["footprint"]);
            if (at == null && footprint != null && footprint.Count > 0)
                at = new Pt(footprint.Average(p => p.X), footprint.Average(p => p.Y));
            if (at == null && item["room"] == null)
            {
                errors.Add($"{label}: equipment[{i}] needs at [x,y], a footprint, or a room");
                continue;
            }
            spec.Equipment.Add(new SpecEquipment
            {
                Family = item["family"]?.Value<string>() ?? "",
                Type = item["type"]?.Value<string>() ?? "",
                At = at ?? default,
                Footprint = footprint,
                Room = item["room"]?.Value<string>(),
                ElementId = item["elementId"]?.Value<long?>()
            });
        }

        return spec;
    }

    private static IEnumerable<(JObject Item, int Index)> Items(JToken? token)
    {
        if (token is not JArray array) yield break;
        for (var i = 0; i < array.Count; i++)
            if (array[i] is JObject item) yield return (item, i);
    }

    public static Pt? ParsePoint(JToken? token)
    {
        try
        {
            switch (token)
            {
                case JArray array when array.Count >= 2:
                    return new Pt(array[0].Value<double>(), array[1].Value<double>());
                case JObject obj when obj["x"] != null && obj["y"] != null:
                    return new Pt(obj["x"]!.Value<double>(), obj["y"]!.Value<double>());
            }
        }
        catch (FormatException) { }
        return null;
    }

    public static List<Pt>? ParsePolygon(JToken? token)
    {
        if (token is not JArray array) return null;
        var points = new List<Pt>();
        foreach (var item in array)
        {
            var point = ParsePoint(item);
            if (point == null) return null;
            points.Add(point.Value);
        }
        // A closed ring repeating its first point is accepted.
        if (points.Count > 3 && Math.Abs(points[0].X - points[^1].X) < 1e-6 && Math.Abs(points[0].Y - points[^1].Y) < 1e-6)
            points.RemoveAt(points.Count - 1);
        return points;
    }

    /// <summary>Thickness from the agency's type naming (CLO_Distribution_7 / _10), else 100 mm.</summary>
    private static double GuessThickness(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return 100;
        var digits = new string(type!.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var cm) && cm is > 3 and < 80 ? cm * 10 : 100;
    }

    /// <summary>
    /// Room kind from its name, in the charter's vocabulary (French) with English fallbacks.
    /// Accents and case are ignored; the leading word decides ("Chambre 1", "Sde PMR").
    /// </summary>
    public static RoomKind Classify(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return RoomKind.Unknown;
        bool Starts(params string[] prefixes) => prefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal));

        if (Starts("sejour", "salon", "living", "piece de vie", "piece principale")) return RoomKind.Living;
        if (Starts("entree", "entry", "hall d'entree")) return RoomKind.Entry;
        if (Starts("cuisine", "kitchen", "kitchenette")) return RoomKind.Kitchen;
        if (Starts("chambre", "ch ", "bedroom") || System.Text.RegularExpressions.Regex.IsMatch(n, @"^ch[\s\.\-_]*\d"))
            return RoomKind.Bedroom;
        if (Starts("sde", "salle d'eau", "salle deau", "shower")) return RoomKind.ShowerRoom;
        if (Starts("sdb", "salle de bain", "bathroom")) return RoomKind.Bathroom;
        if (Starts("wc", "toilette")) return RoomKind.Wc;
        if (Starts("dgt", "degagement", "circulation", "couloir", "corridor")) return RoomKind.Circulation;
        if (Starts("rgt", "rangement", "cellier", "placard", "dressing", "buanderie", "storage")) return RoomKind.Storage;
        return RoomKind.Unknown;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text!.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c == '’' ? '\'' : c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
