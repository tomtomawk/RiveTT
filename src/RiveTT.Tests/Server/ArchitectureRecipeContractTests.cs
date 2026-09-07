using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Server;

public class ArchitectureRecipeContractTests
{
    [Fact]
    public void OptionalJsonTreatsNullAndUndefinedAsOmittedButNeverEmptyContainers()
    {
        Assert.False(JsonOptionalParam.IsProvided(null));
        Assert.False(JsonOptionalParam.IsProvided(default(JsonElement)));
        using var jsonNull = JsonDocument.Parse("null");
        Assert.False(JsonOptionalParam.IsProvided(jsonNull.RootElement));
        foreach (var json in new[] { "{}", "[]", "\"\"", "\"malformed\"", "false", "42" })
        {
            using var document = JsonDocument.Parse(json);
            Assert.True(JsonOptionalParam.IsProvided(document.RootElement));
        }
    }

    [Fact]
    public void OptionalJsonWrappersCannotUseNullableCheckAlone()
    {
        var tools = RepositoryFile.Path("src", "RiveTT.Server", "Tools");
        foreach (var path in Directory.GetFiles(tools, "*.cs"))
        {
            var source = File.ReadAllText(path);
            foreach (Match declaration in Regex.Matches(source, @"JsonElement\?\s+(\w+)\s*=\s*null"))
            {
                var name = declaration.Groups[1].Value;
                Assert.DoesNotContain($"if ({name} != null)", source);
            }
        }
    }

    [Theory]
    [InlineData("\"7\"", "7")]
    [InlineData("7", "7")]
    [InlineData("\"01\"", "01")]
    [InlineData("\"RK\"", "RK")]
    [InlineData("null", "A")]
    public void GridLabelsAcceptNumbersAndStringsWithoutLosingTheRequestedLabel(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(GridLabelParam.TryParse(document.RootElement, "A", out var label));
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("{}")]
    [InlineData("\"\"")]
    public void MalformedGridLabelsProduceAValidationError(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(GridLabelParam.TryParse(document.RootElement, "A", out _));
        var result = JObject.Parse(GridLabelParam.Invalid("yStartLabel"));
        Assert.False(result.Value<bool>("success"));
        Assert.Equal("validation", (string?)result["error"]?["stage"]);
        Assert.False((bool)result["error"]!["modelChanged"]!);
    }

    [Fact]
    public void CompactFamilyTypesPreserveExecutionOriginalCountersAndEveryItem()
    {
        var source = JObject.Parse("""
        {"count":2,"totalCount":12,"truncated":true,
         "execution":{"writesAllowed":false,"cached":true,"pluginVersion":"0.5.0","mcpServerVersion":"0.6.0","versionMismatch":true},
         "items":[{"familyTypeId":1,"typeName":"A"},{"familyTypeId":2,"typeName":"B"}]}
        """);
        var before = source.DeepClone();
        var compact = ToolResponseShaper.Shape("list_family_types", source, true, false);
        Assert.True(JToken.DeepEquals(before, source));
        Assert.True(JToken.DeepEquals(source["execution"], compact["execution"]));
        Assert.Equal(12, (int)compact["totalCount"]!);
        Assert.Equal(2, (int)compact["count"]!);
        Assert.Equal(new long[] { 1, 2 }, compact["items"]!.Select(x => (long)x["familyTypeId"]!));
    }

    [Fact]
    public void CompactOpeningsKeepDimensionsWithUnitsAndDistinctTotals()
    {
        var source = JObject.Parse("""
        {"totalRooms":2,"totalDoors":1,"totalWindows":0,"doorRoomOccurrences":2,
         "execution":{"cached":false},
         "rooms":[{"roomId":1,"doorCount":1,"doors":[{"elementId":10,
           "widthMeasurement":{"value":1.03,"unit":"meters","internalValue":3.379}}]}]}
        """);
        var compact = ToolResponseShaper.Shape("get_room_openings", source, true, false);
        Assert.Equal(1, (int)compact["totalDoors"]!);
        Assert.Equal(2, (int)compact["doorRoomOccurrences"]!);
        Assert.Equal("meters", (string?)compact["rooms"]![0]!["doors"]![0]!["widthMeasurement"]!["unit"]);
        Assert.True(JToken.DeepEquals(source["execution"], compact["execution"]));
    }
}
