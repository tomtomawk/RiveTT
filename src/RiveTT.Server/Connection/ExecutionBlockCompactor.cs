using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Connection;

/// <summary>
/// Sends the session half of the execution block only when it changes.
///
/// Every response carried the same fifteen lines — connector, both versions, Revit version and
/// process, document title, mode — and on a long session they add up to a real share of the
/// context (field report of 2026-09-24, 02 §5 and 09 §6). The fields that describe THIS call
/// (toolReadOnly, toolDestructive, supportsDryRun, writesAllowed, cached) stay on every
/// response; the session fields are replaced by sessionUnchanged: true when they are exactly
/// those of the previous response.
///
/// Never compacted: a version mismatch (the one message that must not be missed), the
/// session-establishing tools, and any response whose session fields differ from the last.
///
/// documentTitle and revitProcessId are NEVER removed, even unchanged. The compactor's memory
/// lives in the server process, and a client such as Claude Desktop keeps that process from
/// one conversation to the next: the first answer of a new conversation would otherwise say
/// "unchanged" to a reader that never saw the target (review of 0.6.0). Which Revit and which
/// file is the one thing an agent must read on every write.
/// </summary>
public sealed class ExecutionBlockCompactor
{
    /// <summary>Compacted when unchanged.</summary>
    public static readonly string[] SessionFields =
    {
        "connector", "pluginVersion", "mcpServerVersion", "revitVersion", "mode"
    };

    /// <summary>Part of the comparison, never removed.</summary>
    public static readonly string[] TargetFields = { "revitProcessId", "documentTitle" };

    private static readonly HashSet<string> AlwaysFull = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_project_info", "get_server_capabilities", "ping_revit"
    };

    private readonly object _lock = new();
    private string? _lastSignature;

    public JToken Compact(string method, JToken response)
    {
        if (response is not JObject obj || obj["execution"] is not JObject execution) return response;

        var signature = string.Join("\u001f",
            SessionFields.Concat(TargetFields).Select(field => execution[field]?.ToString(Formatting.None) ?? ""));
        bool unchanged;
        lock (_lock)
        {
            unchanged = signature == _lastSignature;
            _lastSignature = signature;
        }

        if (!unchanged || AlwaysFull.Contains(method) || execution["versionMismatch"] != null)
            return response;

        foreach (var field in SessionFields) execution.Remove(field);
        execution["sessionUnchanged"] = true;
        return response;
    }
}
