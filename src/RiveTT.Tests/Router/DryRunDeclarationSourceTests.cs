using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Router;

/// <summary>
/// Keeps <c>[ToolSafety(..., supportsDryRun: true)]</c> and the tool's actual behavior in
/// step, in BOTH directions:
///
/// - a tool that READS dryRun but does not declare it gets its preview request refused by
///   the router — a working feature turned off by an omitted argument;
/// - a tool that DECLARES it but never reads it is the original defect wearing a new
///   label: the router would stamp mutated:false on a write that happened.
///
/// Source-level for the same reason as ServerRuntimeParameterContractTests: reflection
/// cannot see which JSON keys a method reads, and a runtime check would need Revit.
///
/// Enumerating tools by hand is what let PathSafetySourceTests miss six path-accepting
/// tools, so this walks the whole of RiveTT.Tools instead. Exemptions are named below,
/// with a reason.
/// </summary>
public class DryRunDeclarationSourceTests
{
    /// <summary>
    /// Tools whose source mentions dryRun without accepting it as a parameter. Each entry
    /// needs a reason; anything else is a bug.
    /// </summary>
    private static readonly Dictionary<string, string> MentionsWithoutAccepting =
        new(StringComparer.Ordinal)
        {
            ["GetServerCapabilitiesTool"] =
                "documents the dryRun contract for every other tool; read-only, previews nothing itself"
        };

    private static readonly Regex ToolClass = new(
        @"\[ToolSafety\(([^)]*)\)\]\s*(?:public\s+)?(?:sealed\s+)?class\s+(\w+)\s*:\s*([^\{]+)\{",
        RegexOptions.Compiled);

    private static string ToolsRoot => Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "RiveTT.Tools"));

    private sealed record ToolSource(string Class, string File, bool ReadOnly, bool Declares, bool Reads);

    private static IEnumerable<ToolSource> Tools()
    {
        foreach (var file in Directory.GetFiles(ToolsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var source = File.ReadAllText(file);
            var matches = ToolClass.Matches(source);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.Groups[3].Value.Contains("IRiveTTTool", StringComparison.Ordinal))
                    continue;

                // The class body runs to the next [ToolSafety] in the file, or its end:
                // several tools share one file (DocumentLifecycleTools, IfcRebuild*).
                var start = match.Index + match.Length;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
                var body = source[start..end];

                var args = match.Groups[1].Value;
                yield return new ToolSource(
                    Class: match.Groups[2].Value,
                    File: Path.GetFileName(file),
                    ReadOnly: args.Split(',')[0].Trim() == "true",
                    Declares: args.Contains("supportsDryRun: true", StringComparison.Ordinal),
                    Reads: body.Contains("dryRun", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void EveryToolThatReadsDryRun_DeclaresIt()
    {
        var offenders = Tools()
            .Where(t => t.Reads && !t.Declares && !t.ReadOnly)
            .Where(t => !MentionsWithoutAccepting.ContainsKey(t.Class))
            .Select(t => $"{t.Class} ({t.File}) reads dryRun but does not declare supportsDryRun: true")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Their preview works but the router refuses to let callers ask for it:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryToolThatDeclaresDryRun_ReadsIt()
    {
        var offenders = Tools()
            .Where(t => t.Declares && !t.Reads)
            .Select(t => $"{t.Class} ({t.File}) declares supportsDryRun: true but never reads dryRun")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "The router would stamp mutated:false on a write that actually happened:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoReadOnlyTool_DeclaresDryRun()
    {
        // supportsDryRun on a read tool is noise at best: it has nothing to preview, and
        // the router never stamps a preview for it.
        var offenders = Tools()
            .Where(t => t.ReadOnly && t.Declares)
            .Select(t => t.Class)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Read-only tools cannot preview anything: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheScanSeesTheWholeCatalogue()
    {
        // A regex that silently stops matching would make every test above vacuous.
        var all = Tools().ToList();
        Assert.True(all.Count > 150, $"Only {all.Count} tools found in {ToolsRoot} — the scan is broken.");
        Assert.Contains(all, t => t.Class == "DeleteElementTool" && t.Declares && t.Reads);
        Assert.Contains(all, t => t.Class == "CreateGridTool" && !t.Declares && !t.Reads);
    }

    [Fact]
    public void ExemptionsAllCarryAReason()
    {
        Assert.All(MentionsWithoutAccepting, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
    }
}
