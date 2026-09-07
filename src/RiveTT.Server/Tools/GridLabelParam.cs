using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Tools;

internal static class GridLabelParam
{
    internal static bool TryParse(JsonElement? value, string defaultLabel, out string label)
    {
        label = defaultLabel;
        if (!JsonOptionalParam.IsProvided(value)) return true;
        var element = value!.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            label = element.GetString()!;
            return !string.IsNullOrWhiteSpace(label);
        }
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _))
        {
            label = element.GetRawText();
            return true;
        }
        return false;
    }

    internal static string Invalid(string parameter) => new JObject
    {
        ["success"] = false,
        ["error"] = new JObject
        {
            ["code"] = "InvalidInput", ["tool"] = "create_grid", ["stage"] = "validation",
            ["modelChanged"] = false, ["message"] = $"{parameter} must be a non-empty string or an integer.",
            ["suggestion"] = "Pass labels as strings (\"RK\", \"7\", \"01\"). Choose the matching naming style."
        }
    }.ToString();
}
