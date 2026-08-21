using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using Xunit;

namespace RevitCortex.Tests.Server;

/// <summary>
/// Locks the parameter TYPES the MCP surface may publish.
///
/// A <c>bool?</c> parameter breaks the whole call: the host answers the generic
/// "An error occurred invoking '&lt;tool&gt;'", the request never reaches Revit,
/// nothing is audited and no transaction runs. Measured live on 0.2.0:
/// list_system_types(category) answered normally while
/// list_system_types(category, includeLoadable: true) — a single added bool? —
/// failed. Nullable long/double/string parameters bind correctly; only the
/// nullable boolean is affected.
///
/// 148 parameters across 110 tools were nullable booleans, including dryRun on
/// fifteen write tools — so "preview before writing" was impossible on those,
/// and the failure looked like a broken tool rather than a type problem. Flags
/// with a documented default are now plain <c>bool</c>; the few whose third
/// state is meaningful travel as a "true"/"false" string through
/// <see cref="RevitCortex.Server.Tools.TriStateFlag"/>.
/// </summary>
public class McpParameterTypeContractTests
{
    private static IEnumerable<(MethodInfo Method, string ToolName)> McpTools()
    {
        var assembly = typeof(RevitCortex.Server.Tools.ProjectTools).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() == null) continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute == null) continue;
                yield return (method, attribute.Name ?? method.Name);
            }
        }
    }

    [Fact]
    public void NoMcpToolPublishesANullableBooleanParameter()
    {
        var offenders = McpTools()
            .SelectMany(tool => tool.Method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(bool?))
                .Select(parameter => $"{tool.ToolName}.{parameter.Name}"))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Nullable boolean parameters make the tool call fail before it reaches Revit. " +
            "Use a plain bool with the documented default, or TriStateFlag for a real " +
            "three-state flag:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryMcpToolIsDiscoverableAndNamed()
    {
        var tools = McpTools().ToList();

        // Sanity: if reflection stops finding the tools, the test above would pass
        // vacuously and the guard would be worthless.
        Assert.True(tools.Count > 250, $"Only {tools.Count} MCP tools discovered by reflection.");
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.ToolName)));
    }

    [Fact]
    public void DryRunIsAlwaysForwardedAsAPlainBoolean()
    {
        // dryRun is the safety contract of the whole connector: it must never be
        // the type that silently breaks the call.
        var dryRunParameters = McpTools()
            .SelectMany(tool => tool.Method.GetParameters()
                .Where(parameter => parameter.Name == "dryRun")
                .Select(parameter => (tool.ToolName, parameter)))
            .ToList();

        Assert.NotEmpty(dryRunParameters);
        Assert.All(dryRunParameters, entry => Assert.Equal(typeof(bool), entry.parameter.ParameterType));
    }
}
