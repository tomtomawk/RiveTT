using System.Text.Json;

namespace RiveTT.Server.Tools;

internal static class JsonOptionalParam
{
    // MCP can bind an omitted optional JsonElement as Undefined, or an explicit
    // null as a present nullable value whose ValueKind is Null.
    internal static bool IsProvided(JsonElement? value) =>
        value is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
}
