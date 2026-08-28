using System;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Connection;

/// <summary>
/// Stamps every response with the version of THIS process — the MCP server — and says
/// so out loud when it disagrees with the plugin that answered.
///
/// The two halves are separate binaries with separate install destinations: the plugin
/// goes to %APPDATA%\Autodesk\Revit\Addins\&lt;year&gt;\RiveTT, the server to
/// %LOCALAPPDATA%\RiveTT\server. An installer run that lands one and not the other is
/// not hypothetical. On 2026-08-28 the running server exe could not be replaced, the
/// install stopped there, and a 0.2.0 server went on publishing the pre-0.3.0 tool
/// names to a 0.4.0 plugin. Every call to a renamed tool came back "Tool not found"
/// while unrenamed ones worked, which reads like a broken client cache and sent an
/// hour of diagnosis in the wrong direction.
///
/// What made it invisible: the single version in the response was called
/// execution.serverVersion and was read from the PLUGIN assembly. The one field an
/// operator would check to catch a server/plugin split was blind to it by construction.
/// It is now execution.pluginVersion, this one is execution.mcpServerVersion, and a
/// disagreement between them is reported rather than left to be inferred from a tool
/// name that no longer exists.
/// </summary>
public static class ConnectorVersions
{
    /// <summary>Version of the RiveTT.Server assembly — the process the MCP client launched.</summary>
    public static string McpServer { get; } =
        typeof(ConnectorVersions).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Adds mcpServerVersion to a response and, when the plugin reports a different
    /// version, a versionMismatch block. Returns the same token: the response is
    /// annotated in place, never replaced, so nothing a tool produced is lost.
    /// </summary>
    public static JToken Stamp(JToken response) => Stamp(response, McpServer);

    /// <summary>
    /// Overload taking the version explicitly. Public for the same reason TransportError
    /// is: the behavior worth locking down is reachable without a Revit session, and a
    /// test must be able to pin a mismatch without rebuilding at two versions.
    /// </summary>
    public static JToken Stamp(JToken response, string mcpServerVersion)
    {
        if (response is not JObject obj) return response;

        // Success carries an execution block; a failure carries error.context. Both are
        // annotated, and the failure case matters MORE: "Tool not found" is precisely
        // what a version split produces, so that is where the explanation must appear.
        var target = obj["execution"] as JObject;
        if (target == null && obj["error"] is JObject error)
        {
            target = error["context"] as JObject;
            if (target == null)
            {
                target = new JObject();
                error["context"] = target;
            }
        }
        if (target == null) return response;

        target["mcpServerVersion"] = mcpServerVersion;

        var mismatch = DescribeMismatch(mcpServerVersion, target["pluginVersion"]?.ToString());
        if (mismatch != null) target["versionMismatch"] = mismatch;
        return response;
    }

    /// <summary>
    /// Null when the two agree, or when the plugin version is unknown — a transport
    /// failure never reached Revit, so there is no second version to disagree with and
    /// claiming a mismatch there would be a false alarm.
    /// </summary>
    public static JObject? DescribeMismatch(string mcpServerVersion, string? pluginVersion)
    {
        if (string.IsNullOrWhiteSpace(pluginVersion)) return null;
        if (string.Equals(pluginVersion, mcpServerVersion, StringComparison.Ordinal)) return null;

        return new JObject
        {
            ["mcpServerVersion"] = mcpServerVersion,
            ["pluginVersion"] = pluginVersion,
            ["message"] =
                $"The MCP server is {mcpServerVersion} and the Revit plugin is {pluginVersion}. " +
                "They are installed separately and this session is running a mixed pair: the tool " +
                "names, parameters and response shape published by the server are those of its own " +
                "version, not the plugin's. A tool renamed between the two answers 'not found', " +
                "and a parameter added between the two is silently dropped.",
            // Written to be relayed, not quoted at a developer. The person reading it
            // does not know there are two halves and does not need to: the one action
            // that is theirs is to fully quit the AI application.
            ["suggestion"] =
                "Tell the user that RiveTT was only half updated, and what to do, in this order: " +
                "(1) fully QUIT their AI application — Claude, ChatGPT, whichever they use with " +
                "Revit — quitting the app, not just closing its window; (2) re-run " +
                "RiveTT-Setup-<version>.exe; (3) reopen the AI application, which is when it picks " +
                "up the new command list. Restarting Revit does not help: the half left behind is " +
                "not inside Revit. Do not explain the plugin/server split unless they ask."
        };
    }
}
