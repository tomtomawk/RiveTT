using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Regression guards for the send_code_to_revit tool guidance.
///
/// The 2026-06-30 audit found the model-facing description had drifted toward a
/// permissive "Execute custom C# code in the Revit context" wording that nudged the
/// model to pick the script tool over dedicated tools (44/54 logged calls failed).
/// These tests pin the de-escalation guidance in the channels the model actually
/// reads: the MCP [Description] attribute on the server method, the server-level
/// ServerInstructions handshake, and the in-Revit UI tool description.
/// </summary>
public class SendCodeDescriptionTests
{
    private static string ServerToolDescription()
    {
        var method = typeof(ProjectTools).GetMethod(
            nameof(ProjectTools.SendCodeToRevit),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(attr);
        return attr!.Description;
    }

    private static string ReadSource(params string[] segments)
    {
        var parts = new string[4 + segments.Length];
        parts[0] = parts[1] = parts[2] = parts[3] = "..";
        Array.Copy(segments, 0, parts, 4, segments.Length);
        var path = Path.GetFullPath(Path.Combine(parts));
        return File.ReadAllText(path);
    }

    // Policy changed in 0.6.2: send_code_to_revit is a legitimate option, no longer a last resort
    // to be used only after the user's explicit consent. What stays pinned is the guidance that
    // makes the choice informed: the dedicated alternatives, the dry-run default, the sandbox.

    [Fact]
    public void ServerDescription_NoLongerCallsTheToolALastResort()
    {
        var desc = ServerToolDescription();
        Assert.DoesNotContain("LAST RESORT", desc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("consent", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legitimate option", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_KeepsTheDryRunAndSavedScriptWarnings()
    {
        var desc = ServerToolDescription();
        Assert.Contains("dry run", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scriptNotice", desc, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerDescription_PointsToDedicatedAlternatives()
    {
        var desc = ServerToolDescription();
        Assert.Contains("set_element_parameters", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filter_elements", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_DoesNotSteerToModalFamilyEditing()
    {
        // edit_family/open_family are the dedicated tools for family editing (P4.1
        // in PLAN_CORRECTION.md); send_code_to_revit's description must not
        // advertise family-internal editing as a valid escalation for itself.
        Assert.DoesNotContain("editing a family's internal definition", ServerToolDescription());
    }

    [Fact]
    public void ServerDescription_DropsPermissiveLegacyWording()
    {
        Assert.DoesNotContain("Execute custom C# code in the Revit context", ServerToolDescription());
    }

    [Fact]
    public void ServerInstructions_PresentSendCodeAsALegitimateOption()
    {
        var program = ReadSource("RiveTT.Server", "Program.cs");
        Assert.Contains("ServerInstructions", program);
        Assert.DoesNotContain("LAST RESORT", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("send_code_to_revit, a legitimate option", program, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginDescription_NoLongerCallsTheToolALastResort()
    {
        var source = ReadSource("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs");
        Assert.DoesNotContain("LAST RESORT", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legitimate option", source, StringComparison.OrdinalIgnoreCase);
    }
}
