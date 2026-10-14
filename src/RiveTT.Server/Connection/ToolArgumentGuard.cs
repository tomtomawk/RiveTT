using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Connection;

/// <summary>
/// Rejects a tool call that carries an argument the tool does not declare.
///
/// The MCP layer binds arguments to the wrapper's parameters BY NAME and drops the rest without
/// a word, so a misspelt or invented key produced a normal-looking answer to a different
/// question (field report, session 0.5.2): <c>export_elements_data(category=...)</c> exported
/// all 10 208 elements because the key is <c>categories</c>; <c>duplicate_view(newName=...)</c>
/// kept the name "Copie 1"; <c>filter_by_parameter_value(filterRule=...)</c> returned 3.6 MB
/// unfiltered. The runtime never saw those keys, so no check there could have caught them: this
/// is the only place that sees the raw arguments.
///
/// The accepted names come from the wrappers themselves (reflection over the
/// [McpServerTool] methods), so the list cannot drift from the surface.
/// </summary>
public static class ToolArgumentGuard
{
    private static readonly Lazy<Dictionary<string, HashSet<string>>> Accepted = new(BuildAcceptedMap);

    /// <summary>
    /// dryRun is harmless on a read tool, and many clients send it everywhere. On a WRITE tool
    /// without a dryRun parameter it is exactly the key that must not be dropped silently: the
    /// caller asked for a preview and the call would apply.
    /// </summary>
    private static readonly string[] ReadOnlyPrefixes =
    {
        "get_", "list_", "find_", "analyze_", "check_", "measure_", "audit_", "export_", "ping_revit",
        "detect_clashes", "count_lines_per_view", "describe_", "validate_", "diagnose_", "capture_view", "ifc_get_",
        "ifc_list_", "ifc_export_", "ifc_validate_", "ifc_analyze_", "ifc_compare_"
    };

    /// <summary>Wrong key a caller is likely to type → the real one, where the guess is not obvious.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["category"] = "categories",
        ["categoryFilter"] = "categories",
        ["names"] = "parameterNames",
        ["parameters"] = "parameterNames",
        ["name"] = "newName"
    };

    private static Dictionary<string, HashSet<string>> BuildAcceptedMap()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var types = typeof(ToolArgumentGuard).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null);
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute?.Name == null) continue;
                map[attribute.Name] = method.GetParameters()
                    .Where(parameter => parameter.ParameterType != typeof(RevitConnectionManager)
                                        && parameter.ParameterType != typeof(CancellationToken)
                                        && parameter.ParameterType != typeof(McpServer)
                                        && parameter.Name != null)
                    .Select(parameter => parameter.Name!)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }
        return map;
    }

    /// <summary>The names a tool declares, or null for a tool this server does not know.</summary>
    public static IReadOnlyCollection<string>? AcceptedParameters(string toolName)
        => Accepted.Value.TryGetValue(toolName, out var names) ? names : null;

    /// <summary>The argument keys the tool does not declare (empty when all are known).</summary>
    public static IReadOnlyList<string> Unrecognized(string toolName, IEnumerable<string> argumentKeys)
    {
        if (!Accepted.Value.TryGetValue(toolName, out var accepted)) return Array.Empty<string>();
        var readOnly = ReadOnlyPrefixes.Any(prefix => toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return argumentKeys
            .Where(key => !key.StartsWith("_", StringComparison.Ordinal)
                          && !accepted.Contains(key)
                          && !(readOnly && key == "dryRun"))
            .ToList();
    }

    /// <summary>The structured refusal, in the shape every other RiveTT error has.</summary>
    public static JObject Rejection(string toolName, IReadOnlyList<string> unrecognized)
    {
        var accepted = Accepted.Value[toolName].OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var hints = unrecognized
            .Select(key => (Key: key, Suggestion: Suggest(key, accepted)))
            .Where(pair => pair.Suggestion != null)
            .Select(pair => $"'{pair.Key}' -> '{pair.Suggestion}'")
            .ToList();
        return new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = "InvalidInput",
                ["tool"] = toolName,
                ["message"] = $"'{toolName}' does not accept: {string.Join(", ", unrecognized)}. "
                              + "Nothing was executed: an unknown argument used to be dropped silently, "
                              + "which turned a typo into an unfiltered or unrenamed call that looked successful.",
                ["suggestion"] = hints.Count > 0
                    ? "Did you mean " + string.Join("; ", hints) + "? Accepted parameters are listed in context."
                    : "Use only the accepted parameters listed in context.",
                ["stage"] = "contract",
                ["modelChanged"] = false,
                ["context"] = new JObject
                {
                    ["unrecognizedParameters"] = new JArray(unrecognized),
                    ["acceptedParameters"] = new JArray(accepted)
                }
            }
        };
    }

    /// <summary>Applies the guard to a raw call. Null means the call is fine to run.</summary>
    public static CallToolResult? Check(CallToolRequestParams? request)
    {
        if (request?.Arguments == null || request.Arguments.Count == 0) return null;
        var unrecognized = Unrecognized(request.Name, request.Arguments.Keys);
        if (unrecognized.Count == 0) return null;
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = Rejection(request.Name, unrecognized).ToString() }]
        };
    }

    private static string? Suggest(string key, IReadOnlyList<string> accepted)
    {
        if (Aliases.TryGetValue(key, out var alias) && accepted.Contains(alias, StringComparer.Ordinal)) return alias;
        var exactIgnoringCase = accepted.FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
        if (exactIgnoringCase != null) return exactIgnoringCase;
        var best = accepted
            .Select(name => (Name: name, Distance: Distance(key.ToLowerInvariant(), name.ToLowerInvariant())))
            .OrderBy(pair => pair.Distance)
            .FirstOrDefault();
        return best.Name != null && best.Distance <= Math.Max(2, key.Length / 3) ? best.Name : null;
    }

    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var current = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = current;
            }
        }
        return row[b.Length];
    }
}
