using System.Text.Json;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// The JsonElement type exists for the same reason as JsonArrayParam: a caller —
/// human or model — naturally sends a native JSON object for a parameter whose
/// description says "JSON object", and every affected tool now accepts exactly
/// that. Measured live on modify_element(action:"move", translation:{x,y,z}):
/// failed with the host's generic "An error occurred invoking" error before this
/// fix, because the parameter was declared string and a JSON object cannot bind
/// to a C# string.
/// </summary>
public class JsonObjectParamTests
{
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void JsonElement_NativeObject_Parses()
    {
        var ok = JsonObjectParam.TryParse(Element("{\"x\":1,\"y\":2,\"z\":3}"), out var parsed);

        Assert.True(ok);
        Assert.Equal(1, (int)parsed["x"]!);
        Assert.Equal(2, (int)parsed["y"]!);
        Assert.Equal(3, (int)parsed["z"]!);
    }

    [Fact]
    public void JsonElement_EmptyNativeObject_Parses()
    {
        var ok = JsonObjectParam.TryParse(Element("{}"), out var parsed);

        Assert.True(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_JsonEncodedStringObject_StillParses()
    {
        // The pre-existing form: a JSON object encoded as a string. Must keep
        // working — some callers already send this correctly.
        var ok = JsonObjectParam.TryParse(Element("\"{\\\"x\\\":1,\\\"y\\\":2}\""), out var parsed);

        Assert.True(ok);
        Assert.Equal(1, (int)parsed["x"]!);
    }

    [Fact]
    public void JsonElement_Null_IsNotProvided()
    {
        JsonElement? value = null;
        var ok = JsonObjectParam.TryParse(value, out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_JsonNullLiteral_IsNotProvided()
    {
        var ok = JsonObjectParam.TryParse(Element("null"), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_EmptyString_IsNotProvided()
    {
        var ok = JsonObjectParam.TryParse(Element("\"\""), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_MalformedEncodedString_IsRejected()
    {
        var ok = JsonObjectParam.TryParse(Element("\"not json\""), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void JsonElement_NativeArray_IsRejected()
    {
        // An array is not an object: modify_element.translation must not silently
        // accept the wrong JSON shape.
        var ok = JsonObjectParam.TryParse(Element("[1,2,3]"), out var parsed);

        Assert.False(ok);
        Assert.Empty(parsed);
    }

    [Fact]
    public void InvalidObjectResult_ReportsTheReceivedValue()
    {
        var result = JsonObjectParam.InvalidObjectResult("modify_element", "translation", Element("[1,2,3]"));

        Assert.Contains("translation", result);
        Assert.Contains("InvalidInput", result);
    }
}
