using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Design;
using Xunit;

namespace RiveTT.Tests.Design;

/// <summary>
/// The charter as a check (docs/retex/2026-09-24/05): a clean 43 m² T2, the same one with the
/// defects the architect found in the field session (closed entrance, no ETEL, cramped shower
/// room, corridor too narrow), and the geometry underneath.
/// </summary>
public class DwellingValidatorTests
{
    // ── a compliant T2 ───────────────────────────────────────────────────────
    //
    //   y
    //  4900 +--------------+-------------------+
    //       |  Sde 2.4x2.7 |                   |   +--------------+
    //  2200 +--------------+  Séjour / Cuisine |   | Chambre 1    |
    //  2100 |  Entrée      :  4.2 x 4.9        |   | 3.3 x 3.3    |
    //     0 +--------------+-------------------+   +--------------+
    //       0            2400                6600 6700          10000   x
    //
    private const string CompliantT2 = @"{
      ""id"": ""A2"", ""level"": ""R+1"", ""typology"": ""T2"", ""targetAreaM2"": 45,
      ""rooms"": [
        { ""name"": ""Entrée"",           ""polygon"": [[0,0],[2400,0],[2400,2100],[0,2100]] },
        { ""name"": ""Séjour / Cuisine"", ""polygon"": [[2400,0],[6600,0],[6600,4900],[2400,4900]] },
        { ""name"": ""Sde"",              ""polygon"": [[0,2200],[2400,2200],[2400,4900],[0,4900]] },
        { ""name"": ""Chambre 1"",        ""polygon"": [[6700,0],[10000,0],[10000,3300],[6700,3300]] }
      ],
      ""partitions"": [
        { ""type"": ""CLO_Distribution_10"", ""p0"": [0,2150],    ""p1"": [2400,2150] },
        { ""type"": ""CLO_Distribution_10"", ""p0"": [2400,2150], ""p1"": [2400,4900] }
      ],
      ""doors"": [
        { ""family"": ""PTE_Porte palière"", ""type"": ""PP93x220 16"", ""at"": [1200,0] },
        { ""family"": ""PTE_Porte simple"",  ""type"": ""PP 93x204"",   ""at"": [6650,1500] },
        { ""family"": ""PTE_Porte simple"",  ""type"": ""PP 93x204"",   ""at"": [2400,3000] }
      ],
      ""equipment"": [
        { ""family"": ""ELC_ETEL"", ""type"": ""ETEL"", ""at"": [1200,2130] },
        { ""family"": ""SAN_Douche sans ressaut"", ""type"": ""90 x 120"",
          ""footprint"": [[0,3700],[900,3700],[900,4900],[0,4900]] },
        { ""family"": ""SAN_Lavabo"", ""type"": ""lavabo_60x55x85 cm_meuble vasque"",
          ""footprint"": [[1700,4350],[2300,4350],[2300,4900],[1700,4900]] },
        { ""family"": ""SAN_WC"", ""type"": ""Suspendu PMR"", ""at"": [2200,2600] },
        { ""family"": ""SAN_Meuble évier 120"", ""type"": ""120x65"", ""at"": [5000,4600] },
        { ""family"": ""ELC_Emplacement électroménager"", ""type"": ""RF"", ""at"": [4300,4600] },
        { ""family"": ""ELC_Emplacement électroménager"", ""type"": ""Cuisson"", ""at"": [5700,4600] }
      ]
    }";

    private static JObject Run(string json, JObject? overrides = null)
    {
        var errors = new List<string>();
        var spec = DwellingSpec.Parse(JObject.Parse(json), errors);
        Assert.Empty(errors);
        var rules = DwellingRuleSet.CreateDefault();
        rules.ApplyOverrides(overrides, errors);
        Assert.Empty(errors);
        return DwellingValidator.Validate(spec, rules);
    }

    private static JToken CheckOf(JObject report, string rule) =>
        report["checks"]!.First(c => c["rule"]!.Value<string>() == rule);

    [Fact]
    public void ACleanT2IsCompliant()
    {
        var report = Run(CompliantT2);

        var failed = report["checks"]!.Where(c => !c["ok"]!.Value<bool>())
            .Select(c => $"{c["rule"]}: {c["detail"]}").ToList();
        Assert.True(failed.Count == 0, string.Join("\n", failed));
        Assert.Empty(report["notEvaluated"]!);
        Assert.True(report["compliant"]!.Value<bool>());
        Assert.Equal(42.99, report["roomsAreaM2"]!.Value<double>(), 2);

        // The rules that cannot apply to a T2 are said so, not counted as passed.
        var notApplicable = report["notApplicable"]!.Select(n => n["rule"]!.Value<string>()).ToList();
        Assert.Contains("WC_SEPARE", notApplicable);
        Assert.Contains("CHAMBRE_DEPUIS_DGT", notApplicable);
        Assert.Contains("CIRC_INT_LARGEUR", notApplicable);
    }

    [Fact]
    public void TheMeasuredValuesAreReported()
    {
        var report = Run(CompliantT2);
        Assert.Equal(2100, CheckOf(report, "ENTREE_OUVERTE_SEJOUR")["value"]!.Value<double>());
        Assert.True(CheckOf(report, "CERCLE_150_SDE")["value"]!.Value<double>() >= 1500 - 15);
        Assert.Equal(0.117, CheckOf(report, "RATIO_CIRCULATION")["value"]!.Value<double>(), 3);
        Assert.Contains("rotated", CheckOf(report, "ENTREE_GABARIT")["detail"]!.Value<string>());
    }

    [Fact]
    public void TheDefectsOfTheFieldSessionAreCaught()
    {
        var json = JObject.Parse(CompliantT2);
        // Entrance closed by a partition, as in plan versions 1 and 2.
        ((JArray)json["partitions"]!).Add(JObject.Parse(@"{ ""type"": ""CLO_Distribution_7"", ""p0"": [2400,0], ""p1"": [2400,2100] }"));
        // ETEL forgotten.
        var equipment = (JArray)json["equipment"]!;
        equipment.Remove(equipment.First(e => e["family"]!.Value<string>() == "ELC_ETEL"));
        // Shower room of 2.6 m², as in version 1.
        var sde = json["rooms"]!.First(r => r["name"]!.Value<string>() == "Sde");
        sde["polygon"] = JArray.Parse("[[0,2200],[2400,2200],[2400,3300],[0,3300]]");

        var report = Run(json.ToString());

        Assert.False(report["compliant"]!.Value<bool>());
        Assert.False(CheckOf(report, "ENTREE_OUVERTE_SEJOUR")["ok"]!.Value<bool>());
        Assert.Contains("partition", CheckOf(report, "ENTREE_OUVERTE_SEJOUR")["detail"]!.Value<string>());
        Assert.False(CheckOf(report, "ETEL_ENTREE")["ok"]!.Value<bool>());
        Assert.False(CheckOf(report, "CERCLE_150_SDE")["ok"]!.Value<bool>());
        Assert.True(report["summary"]!["errors"]!.Value<int>() >= 3);
    }

    [Fact]
    public void ADoorBetweenEntranceAndLivingRoomClosesTheEntrance()
    {
        var json = JObject.Parse(CompliantT2);
        ((JArray)json["doors"]!).Add(JObject.Parse(@"{ ""family"": ""PTE_Porte simple"", ""type"": ""PP 93x204"", ""at"": [2400,1000] }"));
        var check = CheckOf(Run(json.ToString()), "ENTREE_OUVERTE_SEJOUR");
        Assert.False(check["ok"]!.Value<bool>());
        Assert.Contains("door", check["detail"]!.Value<string>());
    }

    [Fact]
    public void ACorridorOf80CmFailsAndOneOf95Passes()
    {
        string T3(double corridorWidth) => $@"{{
          ""id"": ""B1"", ""typology"": ""T3"",
          ""rooms"": [
            {{ ""name"": ""Entrée"", ""polygon"": [[0,0],[2400,0],[2400,2200],[0,2200]] }},
            {{ ""name"": ""Dgt"", ""polygon"": [[2400,0],[6400,0],[6400,{corridorWidth}],[2400,{corridorWidth}]] }}
          ]
        }}";

        var narrow = CheckOf(Run(T3(800)), "CIRC_INT_LARGEUR");
        Assert.False(narrow["ok"]!.Value<bool>());
        Assert.Contains("narrower than 900", narrow["detail"]!.Value<string>());

        var fine = CheckOf(Run(T3(950)), "CIRC_INT_LARGEUR");
        Assert.True(fine["ok"]!.Value<bool>());
        Assert.True(CheckOf(Run(T3(950)), "CIRC_INT_LARGEUR_MAX")["ok"]!.Value<bool>());

        // 1.82 m, "jugé excessif" in the session: passes the minimum, flagged as too wide.
        Assert.False(CheckOf(Run(T3(1820)), "CIRC_INT_LARGEUR_MAX")["ok"]!.Value<bool>());
    }

    [Fact]
    public void ANarrowingSplitsTheCorridor()
    {
        // 1.00 m corridor pinched to 0.80 m over 300 mm in the middle.
        var json = @"{ ""id"": ""X"", ""typology"": ""T3"", ""rooms"": [
            { ""name"": ""Dgt"", ""polygon"": [[0,0],[2000,0],[2000,200],[2300,200],[2300,0],[4300,0],[4300,1000],[0,1000]] } ] }";
        var check = CheckOf(Run(json), "CIRC_INT_LARGEUR");
        Assert.False(check["ok"]!.Value<bool>());
        Assert.Contains("splits into 2 parts", check["detail"]!.Value<string>());
    }

    [Fact]
    public void OverridesChangeTheThresholdAndAreReported()
    {
        var report = Run(CompliantT2, JObject.Parse(@"{ ""RATIO_CIRCULATION"": { ""max"": 0.10 }, ""SURF_CHAMBRE"": { ""enabled"": false } }"));
        Assert.False(CheckOf(report, "RATIO_CIRCULATION")["ok"]!.Value<bool>());
        Assert.DoesNotContain(report["checks"]!, c => c["rule"]!.Value<string>() == "SURF_CHAMBRE");
        Assert.Contains("RATIO_CIRCULATION.max = 0.1", report["overridesApplied"]!.Values<string>());
        // A warning-level failure does not make the dwelling non-compliant.
        Assert.True(report["compliant"]!.Value<bool>());
    }

    [Fact]
    public void UnknownOverridesAreRefusedNotIgnored()
    {
        var errors = new List<string>();
        var rules = DwellingRuleSet.CreateDefault();
        rules.ApplyOverrides(JObject.Parse(@"{ ""RATIO_CIRCULATIONS"": { ""max"": 0.1 }, ""ETEL_ENTREE"": { ""prefix"": ""X"" } }"), errors);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("unknown rule 'RATIO_CIRCULATIONS'"));
        Assert.Contains(errors, e => e.Contains("unknown value 'prefix'"));
    }

    [Fact]
    public void MissingDataIsNotEvaluatedAndMakesTheVerdictUnknown()
    {
        var report = Run(@"{ ""id"": ""Z"", ""typology"": ""T2"", ""rooms"": [
            { ""name"": ""Séjour"", ""polygon"": [[0,0],[5000,0],[5000,4500],[0,4500]] },
            { ""name"": ""Chambre"", ""polygon"": [[5100,0],[8500,0],[8500,3300],[5100,3300]] } ] }");
        var notEvaluated = report["notEvaluated"]!.Select(n => n["rule"]!.Value<string>()).ToList();
        Assert.Contains("ENTREE_OUVERTE_SEJOUR", notEvaluated);
        Assert.Contains("CERCLE_150_SDE", notEvaluated);
        Assert.StartsWith("unknown", report["compliant"]!.Value<string>());
    }

    // ── classification and geometry ──────────────────────────────────────────

    [Theory]
    [InlineData("Séjour / Cuisine", RoomKind.Living)]
    [InlineData("SEJOUR", RoomKind.Living)]
    [InlineData("Entrée", RoomKind.Entry)]
    [InlineData("Chambre 2", RoomKind.Bedroom)]
    [InlineData("Sde", RoomKind.ShowerRoom)]
    [InlineData("Salle d'eau", RoomKind.ShowerRoom)]
    [InlineData("Sdb", RoomKind.Bathroom)]
    [InlineData("WC", RoomKind.Wc)]
    [InlineData("Dgt", RoomKind.Circulation)]
    [InlineData("Dégagement", RoomKind.Circulation)]
    [InlineData("Cellier", RoomKind.Storage)]
    [InlineData("Palier", RoomKind.Unknown)]
    public void RoomNamesOfTheCharterAreClassified(string name, RoomKind kind)
    {
        Assert.Equal(kind, DwellingSpec.Classify(name));
    }

    [Fact]
    public void AnExplicitKindOverridesTheName()
    {
        var errors = new List<string>();
        var spec = DwellingSpec.Parse(JObject.Parse(
            @"{ ""rooms"": [ { ""name"": ""Pièce de nuit"", ""kind"": ""Bedroom"", ""polygon"": [{""x"":0,""y"":0},{""x"":3000,""y"":0},{""x"":3000,""y"":3000}] } ] }"), errors);
        Assert.Empty(errors);
        Assert.Equal(RoomKind.Bedroom, spec.Rooms.Single().Kind);
    }

    [Fact]
    public void MalformedRoomsAreReportedWithTheirIndex()
    {
        var errors = new List<string>();
        DwellingSpec.Parse(JObject.Parse(@"{ ""id"": ""Q"", ""rooms"": [ { ""name"": ""Sde"", ""polygon"": [[0,0],[1,1]] } ] }"), errors);
        Assert.Contains(errors, e => e.Contains("rooms[0]") && e.Contains("Sde"));
    }

    [Fact]
    public void TheLargestFreeCircleAvoidsObstacles()
    {
        var square = new List<Pt> { new(0, 0), new(1600, 0), new(1600, 1600), new(0, 1600) };
        Assert.InRange(new ClearanceField(square, new List<IReadOnlyList<Pt>>(), 10).LargestCircle().DiameterMm, 1580, 1600);

        var obstacle = new List<Pt> { new(0, 1100), new(500, 1100), new(500, 1600), new(0, 1600) };
        var withObstacle = new ClearanceField(square, new[] { (IReadOnlyList<Pt>)obstacle }, 10).LargestCircle().DiameterMm;
        Assert.True(withObstacle < 1500, $"measured {withObstacle}");
    }

    [Theory]
    [InlineData(1490, false)]
    [InlineData(1510, true)]
    public void TheCircleIsDecidedToTheMillimetreNotToTheGrid(double side, bool fits)
    {
        // On the grid alone a 1.49 m room could pass for Ø 1.50 m (review of 0.6.0).
        var report = Run($@"{{ ""id"": ""S"", ""typology"": ""T2"", ""rooms"": [
            {{ ""name"": ""Sde"", ""polygon"": [[0,0],[{side},0],[{side},{side}],[0,{side}]] }} ] }}");
        var check = CheckOf(report, "CERCLE_150_SDE");
        Assert.Equal(fits, check["ok"]!.Value<bool>());
        Assert.InRange(check["value"]!.Value<double>(), side - 2, side);
    }

    [Fact]
    public void AColumnInsideTheRoomIsNotFloor()
    {
        // 2.4 x 2.4 m room: a free Ø 1.50 m circle fits — until a 0.5 x 0.5 m column stands in
        // its middle. Revit returns such a column as an inner boundary loop.
        const string room = @"{ ""id"": ""P"", ""typology"": ""T2"", ""rooms"": [
            { ""name"": ""Séjour"", ""polygon"": [[0,0],[2400,0],[2400,2400],[0,2400]] HOLES } ] }";
        var open = CheckOf(Run(room.Replace(" HOLES", "")), "CERCLE_150_SEJOUR");
        Assert.True(open["ok"]!.Value<bool>());

        var withColumn = Run(room.Replace(" HOLES", @", ""holes"": [[[950,950],[1450,950],[1450,1450],[950,1450]]]"));
        Assert.False(CheckOf(withColumn, "CERCLE_150_SEJOUR")["ok"]!.Value<bool>());
        Assert.Equal(5.51, withColumn["rooms"]!.Single()["areaM2"]!.Value<double>(), 2);
    }

    [Theory]
    [InlineData("Ch1")]
    [InlineData("CH.2")]
    [InlineData("ch 3")]
    [InlineData("Chambre parents")]
    public void BedroomAbbreviationsAreRecognised(string name)
    {
        Assert.Equal(RoomKind.Bedroom, DwellingSpec.Classify(name));
    }

    [Fact]
    public void RectanglesFitInEitherOrientation()
    {
        var entry = new List<Pt> { new(0, 0), new(2300, 0), new(2300, 1300), new(0, 1300) };
        var field = new ClearanceField(entry, new List<IReadOnlyList<Pt>>(), 10);
        var fit = field.FitRectangle(1200, 2200);
        Assert.NotNull(fit);
        Assert.True(fit!.Value.Rotated);
        Assert.Null(field.FitRectangle(1400, 2400));
    }

    [Fact]
    public void SharedBoundaryOfAdjacentRooms()
    {
        var a = new List<Pt> { new(0, 0), new(2400, 0), new(2400, 2100), new(0, 2100) };
        var b = new List<Pt> { new(2400, 0), new(6600, 0), new(6600, 4900), new(2400, 4900) };
        Assert.Equal(2100, PlanGeometry.SharedBoundaryLength(a, b, 30), 1);
        var apart = new List<Pt> { new(2500, 0), new(6600, 0), new(6600, 4900), new(2500, 4900) };
        Assert.Equal(0, PlanGeometry.SharedBoundaryLength(a, apart, 30), 1);
    }
}
