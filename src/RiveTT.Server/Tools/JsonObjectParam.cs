using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Tools;

/// <summary>
/// Same defect as <see cref="JsonArrayParam"/>, for scalar JSON objects instead of
/// arrays: a vector {x,y,z}, a line {p0,p1}, a map of parameter overrides, etc.
///
/// Every one of these parameters was declared <c>string</c>/<c>string? = null</c>
/// and parsed with <c>JObject.Parse(x)</c>, while its own description says "JSON
/// object" — an appellant that follows the description and sends a native JSON
/// object cannot bind to a C# string, and the host answers the generic "An error
/// occurred invoking '&lt;tool&gt;'" before the method body runs, with nothing
/// reaching Revit. Measured live on modify_element(action:"move", translation:
/// {x,y,z}): the object form the description shows fails; only a JSON-encoded
/// string form ever worked. <see cref="System.Text.Json.JsonElement"/> accepts
/// whichever shape actually arrived, exactly like JsonArrayParam does for arrays.
/// </summary>
internal static class JsonObjectParam
{
    internal static bool TryParse(System.Text.Json.JsonElement? value, out JObject parsed)
    {
        parsed = new JObject();
        if (value is not { } element) return false;

        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                parsed = JObject.Parse(element.GetRawText());
                return true;
            case System.Text.Json.JsonValueKind.String:
                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text)) return false;
                try
                {
                    parsed = JObject.Parse(text);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            default:
                return false;
        }
    }

    /// <summary>
    /// Structured refusal in the same shape as a runtime failure, so a malformed
    /// value is never rendered as a broken tool.
    /// </summary>
    internal static string InvalidObjectResult(string tool, string parameterName, System.Text.Json.JsonElement? value)
    {
        return new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = "InvalidInput",
                ["tool"] = tool,
                ["message"] = $"{parameterName} must be a JSON object (received: \"{value}\")",
                ["suggestion"] = $"Pass {parameterName} as a JSON object, e.g. {{\"x\":0,\"y\":0,\"z\":0}}, " +
                                 "or as a JSON-encoded string of the same object.",
                ["stage"] = "validation",
                ["modelChanged"] = false
            }
        }.ToString();
    }
}
