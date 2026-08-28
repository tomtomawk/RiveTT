using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// The MCP server and the Revit plugin are separate binaries installed to separate
/// places, and on 2026-08-28 an installer run landed the plugin (0.4.0) without
/// replacing the running server (0.2.0). The 0.2.0 server kept publishing pre-0.3.0
/// tool names, so every renamed tool answered "Tool not found" while the unrenamed
/// ones worked — and the only version in the response, then called serverVersion, was
/// read from the plugin and said 0.4.0.
///
/// These tests pin the fix: the response reports BOTH versions, and reports the
/// disagreement instead of leaving it to be inferred from a missing tool.
/// </summary>
public class ConnectorVersionsTests
{
    private const string Server = "0.4.0.0";

    private static JObject Success(string pluginVersion) => new()
    {
        ["value"] = 1,
        ["execution"] = new JObject
        {
            ["connector"] = "RiveTT",
            ["pluginVersion"] = pluginVersion
        }
    };

    [Fact]
    public void Stamp_AddsTheServerVersionAlongsideThePluginVersion()
    {
        var stamped = (JObject)ConnectorVersions.Stamp(Success(Server), Server);
        var execution = (JObject)stamped["execution"]!;

        Assert.Equal(Server, execution["mcpServerVersion"]);
        Assert.Equal(Server, execution["pluginVersion"]);
    }

    [Fact]
    public void Stamp_MatchingVersions_ReportsNoMismatch()
    {
        var stamped = (JObject)ConnectorVersions.Stamp(Success(Server), Server);
        Assert.Null(stamped["execution"]!["versionMismatch"]);
    }

    [Fact]
    public void Stamp_DifferentVersions_ReportsBothAndHowToFixIt()
    {
        var stamped = (JObject)ConnectorVersions.Stamp(Success("0.2.0.0"), Server);
        var mismatch = stamped["execution"]!["versionMismatch"];

        Assert.NotNull(mismatch);
        Assert.Equal(Server, mismatch!["mcpServerVersion"]);
        Assert.Equal("0.2.0.0", mismatch["pluginVersion"]);
        // Restarting Revit is the intuitive move and the useless one: the half left
        // behind is not inside Revit. The suggestion has to say so, or the next reader
        // loses the same hour.
        Assert.Contains("Restarting Revit does not help", mismatch["suggestion"]!.ToString());
        Assert.Contains("fully QUIT their AI application", mismatch["suggestion"]!.ToString());
    }

    [Fact]
    public void Stamp_ToolNotFound_CarriesTheMismatchInTheErrorContext()
    {
        // The shape RiveTTRouter returns for an unknown tool — the exact symptom a
        // server/plugin split produces, so this is where the explanation must land.
        var failure = new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = "InvalidInput",
                ["message"] = "Tool 'say_hello' not found",
                ["context"] = new JObject { ["pluginVersion"] = "0.4.0.0" }
            }
        };

        var stamped = (JObject)ConnectorVersions.Stamp(failure, "0.2.0.0");
        var context = (JObject)stamped["error"]!["context"]!;

        Assert.Equal("0.2.0.0", context["mcpServerVersion"]);
        Assert.NotNull(context["versionMismatch"]);
    }

    [Fact]
    public void Stamp_TransportFailure_ReportsItsOwnVersionWithoutInventingAMismatch()
    {
        // Nothing reached Revit, so there is no plugin version to disagree with.
        // Flagging a mismatch here would be a false alarm on every dead pipe.
        var failure = TransportError.Describe(
            "get_project_info", new IOException("pipe closed"), 300);

        var stamped = (JObject)ConnectorVersions.Stamp(failure, Server);
        var context = (JObject)stamped["error"]!["context"]!;

        Assert.Equal(Server, context["mcpServerVersion"]);
        Assert.Null(context["versionMismatch"]);
    }

    [Fact]
    public void DescribeMismatch_UnknownPluginVersion_IsNotAMismatch()
    {
        Assert.Null(ConnectorVersions.DescribeMismatch(Server, null));
        Assert.Null(ConnectorVersions.DescribeMismatch(Server, ""));
        Assert.Null(ConnectorVersions.DescribeMismatch(Server, "   "));
    }

    [Fact]
    public void Stamp_NonObjectResponse_IsReturnedUntouched()
    {
        var token = JValue.CreateNull();
        Assert.Same(token, ConnectorVersions.Stamp(token, Server));
    }

    [Fact]
    public void McpServer_IsReadFromTheAssembly_NotATypedLiteral()
    {
        // Program.cs used to hardcode the MCP ServerInfo version, a second place to
        // forget when Directory.Build.props moves.
        var expected = typeof(ConnectorVersions).Assembly.GetName().Version?.ToString();
        Assert.Equal(expected, ConnectorVersions.McpServer);
        Assert.NotEqual("0.0.0.0", ConnectorVersions.McpServer);
    }
}

/// <summary>
/// Source guard for the plugin side of the same contract: the field naming the Revit
/// half must be pluginVersion. Named serverVersion, it made a server/plugin split
/// unobservable through the very field an operator would check.
/// </summary>
public class ExecutionVersionFieldSourceTests
{
    private static string RouterSource() => File.ReadAllText(
        Path.GetFullPath(Path.Combine(
            "..", "..", "..", "..", "..", "src", "RiveTT.Plugin", "RiveTTRouter.cs")));

    [Fact]
    public void Router_ReportsPluginVersion()
    {
        Assert.Contains("[\"pluginVersion\"] = PluginVersion", RouterSource());
    }

    [Fact]
    public void Router_NoLongerReportsServerVersion()
    {
        // The plugin cannot know the MCP server's version; only the server can stamp it.
        Assert.DoesNotContain("[\"serverVersion\"]", RouterSource());
    }
}
