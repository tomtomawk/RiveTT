using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// validate_spec as the MCP client sees it: a native JSON object in, a report out, no Revit.
/// </summary>
public class DesignToolsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private const string SmallT1 = @"{
      ""id"": ""A6"", ""typology"": ""T1"",
      ""rooms"": [
        { ""name"": ""Entrée"", ""polygon"": [[0,0],[2400,0],[2400,1300],[0,1300]] },
        { ""name"": ""Séjour"", ""polygon"": [[0,1300],[5000,1300],[5000,5300],[0,5300]] }
      ]
    }";

    [Fact]
    public void ADesignIsCheckedWithoutRevit()
    {
        var report = JObject.Parse(DesignTools.ValidateSpec(Json(SmallT1)));

        Assert.True(report["success"]!.Value<bool>());
        Assert.False(report["modelChanged"]!.Value<bool>());
        var dwelling = report["dwellings"]!.Single();
        Assert.Equal("A6", dwelling["dwelling"]!.Value<string>());
        // The entrance opens fully onto the living room: 2 400 mm of shared boundary.
        var open = dwelling["checks"]!.First(c => c["rule"]!.Value<string>() == "ENTREE_OUVERTE_SEJOUR");
        Assert.True(open["ok"]!.Value<bool>());
        // No shower room in the data: not evaluated...
        Assert.Contains(dwelling["notEvaluated"]!, n => n["rule"]!.Value<string>() == "CERCLE_150_SDE");
        // ...but a failed error-level rule (no ETEL) decides the verdict anyway.
        Assert.False(dwelling["checks"]!.First(c => c["rule"]!.Value<string>() == "ETEL_ENTREE")["ok"]!.Value<bool>());
        Assert.False(dwelling["compliant"]!.Value<bool>());
        Assert.Equal(1, report["summary"]!["nonCompliant"]!.Value<int>());
    }

    [Fact]
    public void SeveralDwellingsAreCheckedTogether()
    {
        var report = JObject.Parse(DesignTools.ValidateSpec(Json($@"{{ ""dwellings"": [ {SmallT1}, {SmallT1.Replace("A6", "A7")} ] }}")));
        Assert.Equal(2, report["summary"]!["dwellingCount"]!.Value<int>());
    }

    [Fact]
    public void ProblemsInTheSpecAreListedNotThrown()
    {
        var report = JObject.Parse(DesignTools.ValidateSpec(
            Json(@"{ ""id"": ""X"", ""rooms"": [ { ""name"": ""Sde"", ""polygon"": [[0,0]] } ] }"),
            Json(@"{ ""NOT_A_RULE"": { ""enabled"": false } }")));
        Assert.False(report["success"]!.Value<bool>());
        var problems = report["error"]!["problems"]!.Values<string>().ToList();
        Assert.Contains(problems, p => p!.Contains("NOT_A_RULE"));
        Assert.Contains(problems, p => p!.Contains("rooms[0]"));
    }

    [Fact]
    public void TheRuleCatalogueCarriesTheSourceOfEachRule()
    {
        var catalogue = JObject.Parse(DesignTools.ValidateSpec(listRules: true));
        var rules = catalogue["rules"]!.ToList();
        Assert.Contains(rules, r => r["rule"]!.Value<string>() == "CERCLE_150_SDE"
                                    && r["source"]!.Value<string>()!.Contains("à vérifier"));
        Assert.Contains(rules, r => r["rule"]!.Value<string>() == "RATIO_CIRCULATION"
                                    && r["source"]!.Value<string>()!.Contains("Pratique agence"));
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r["label"]!.Value<string>())));
    }

    [Fact]
    public void AMissingSpecIsAStructuredRefusal()
    {
        var refusal = JObject.Parse(DesignTools.ValidateSpec());
        Assert.False(refusal["success"]!.Value<bool>());
        Assert.Equal("InvalidInput", refusal["error"]!["code"]!.Value<string>());
    }
}
