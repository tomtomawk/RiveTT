using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Security;

/// <summary>
/// Every tool that accepts a file path from the caller must send it through
/// PathSafety.TryResolveSafe before touching the filesystem.
///
/// This test used to be a hand-written [InlineData] list of 17 tools. It passed while six
/// others — export_schedule, save_as_document, create_document, open_document, open_family,
/// load_family, manage_images — read a caller path and used it unchecked, because nothing
/// added them to the list. That is the same failure mode the repository already fixed for
/// the tool inventory: a list maintained by hand stops being maintained, silently.
///
/// So it SCANS. A tool discovered tomorrow is covered the day it is written, and an
/// exemption has to be argued for in the table below rather than obtained by omission.
/// </summary>
public class PathSafetySourceTests
{
    /// <summary>A caller-supplied parameter that denotes a filesystem location.</summary>
    private static readonly Regex PathParameter = new(
        @"input\[""(\w*(?:[Pp]ath|Directory|[Ff]older|[Ff]ileName)\w*)""\]", RegexOptions.Compiled);

    /// <summary>Any use of that location against the filesystem or the Revit document API.</summary>
    private static readonly Regex FilesystemUse = new(
        @"\b(File|Directory|FileInfo|DirectoryInfo)\s*\.|\.SaveAs\(|new\s+StreamWriter|new\s+StreamReader|\.Export\(|LoadFamily\(|ImageTypeOptions\(|OpenAndActivateDocument\(|OpenDocumentFile\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Files that read a path-shaped parameter without ever reaching the filesystem with
    /// it. Each needs a reason; anything else is a bug.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["CreateCurveElementTools.cs"] =
            "\"path\" here is a geometric polyline ([{x,y,z}, ...]), not a filesystem path",
        ["CreateRailingTool.cs"] =
            "\"path\" here is the railing's baseline geometry, not a filesystem path",
        ["ManageLinksTool.cs"] =
            "gated through PathSafety for newPath; the other path reads are Revit's own ExternalFileReference values, not caller input",
    };

    private static string ToolsRoot => Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "RiveTT.Tools"));

    private static IEnumerable<(string File, string Source, string[] Params)> PathAcceptingFiles()
    {
        foreach (var file in Directory.GetFiles(ToolsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var source = File.ReadAllText(file);
            var parameters = PathParameter.Matches(source)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (parameters.Length > 0)
                yield return (Path.GetFileName(file), source, parameters);
        }
    }

    [Fact]
    public void EveryToolTakingACallerPath_GatesItThroughPathSafety()
    {
        var offenders = PathAcceptingFiles()
            .Where(t => FilesystemUse.IsMatch(t.Source))
            .Where(t => !t.Source.Contains("PathSafety.TryResolveSafe(", StringComparison.Ordinal))
            .Where(t => !Exempt.ContainsKey(t.File))
            .Select(t => $"{t.File} reads {string.Join(", ", t.Params)} and touches the filesystem unchecked")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Caller-supplied paths reaching the filesystem without PathSafety:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheScanFindsTheToolsItIsMeantToCover()
    {
        // A regex that quietly stopped matching would make the test above vacuous. These
        // are the six that the [InlineData] version missed, plus one it did cover.
        var files = PathAcceptingFiles().Select(t => t.File).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "ExportScheduleTool.cs", "DocumentLifecycleTools.cs", "DocumentCreationTools.cs",
                     "FamilyAndTemplateDocumentTools.cs", "LoadFamilyTool.cs", "ManageImagesTool.cs",
                     "ExportToExcelTool.cs",
                 })
        {
            Assert.Contains(expected, files);
        }
    }

    [Fact]
    public void ExemptionsAllCarryAReason()
    {
        Assert.All(Exempt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
    }

    [Fact]
    public void ToolsWritingToACallerNamedFile_RefuseToOverwriteUnlessAsked()
    {
        // Creating a file and destroying one are different acts. A tool that names its own
        // output (batch_export, ifc_export_* write generated names into a directory) is a
        // different question and is not covered here.
        foreach (var file in new[]
                 {
                     "ExportScheduleTool.cs", "ExportToExcelTool.cs",
                 })
        {
            var source = File.ReadAllText(
                Directory.GetFiles(ToolsRoot, file, SearchOption.AllDirectories).Single());
            Assert.Contains("PathSafety.CanWriteTo(", source);
        }
    }
}
