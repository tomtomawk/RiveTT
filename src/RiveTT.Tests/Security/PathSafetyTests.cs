using System;
using System.IO;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Security;

/// <summary>
/// PathSafety — the gate every caller-supplied file path passes before a tool reads or
/// writes it. No Revit dependency, so these run as plain facts.
///
/// The policy changed on 2026-08-31 from an ALLOW list of the current user's personal
/// folders to a DENY list of system locations. The allow list refused
/// P:\Projets\... — the agency's own project drive, where the exports are meant to go —
/// while save_as_document wrote a .rvt anywhere because it never called this. It cost
/// the daily gesture and left open the door it named. These tests pin both halves of the
/// new rule: the project drive works, the system directories do not.
/// </summary>
public class PathSafetyTests
{
    [Fact]
    public void NullOrWhitespace_IsRejected()
    {
        Assert.False(PathSafety.TryResolveSafe(null, out _, out var err1));
        Assert.NotEmpty(err1);
        Assert.False(PathSafety.TryResolveSafe("", out _, out _));
        Assert.False(PathSafety.TryResolveSafe("   ", out _, out _));
    }

    [Fact]
    public void PathUnderTemp_IsAccepted()
    {
        var path = Path.Combine(Path.GetTempPath(), "rivett-test.csv");
        Assert.True(PathSafety.TryResolveSafe(path, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void PathUnderDocuments_IsAccepted()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(docs, "export.xlsx");
        Assert.True(PathSafety.TryResolveSafe(path, out var resolved, out _));
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Theory]
    // The cases the old allow list refused, and which cost real work.
    [InlineData(@"P:\Projets\2026-047 College\04 EXE\quantitatif.xlsx")]
    [InlineData(@"\\srv-fichiers\Projets\2026-047\maquette.ifc")]
    [InlineData(@"D:\Maquettes\export.csv")]
    public void ProjectDrive_IsAccepted(string path)
    {
        Assert.True(PathSafety.TryResolveSafe(path, out var resolved, out var err), err);
        Assert.NotEmpty(resolved);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll")]
    [InlineData(@"C:\ProgramData\Autodesk\secret.txt")]
    public void SystemLocation_IsRejected(string path)
    {
        Assert.False(PathSafety.TryResolveSafe(path, out _, out var err));
        Assert.Contains("system directory", err);
    }

    [Fact]
    public void RiveTTOwnState_IsRejected()
    {
        // The audit log is the evidence of what an agent did. A tool able to overwrite it
        // could erase its own trace, so RiveTT's folder is denied like a system one.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.False(PathSafety.TryResolveSafe(
            Path.Combine(local, "RiveTT", "audit.jsonl"), out _, out var err));
        Assert.Contains("RiveTT", err);
    }

    [Fact]
    public void TraversalIntoADeniedRoot_IsRejected()
    {
        // GetFullPath collapses ".." BEFORE the deny check, which is what makes traversal
        // ineffective instead of merely detected.
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var sneaky = Path.Combine(docs, @"..\..\..\..\..\..\Windows\win.ini");
        Assert.False(PathSafety.TryResolveSafe(sneaky, out _, out _));
    }

    [Fact]
    public void RelativePath_IsRejected()
    {
        // A relative path resolves against the process working directory — Revit's install
        // folder — which is neither predictable nor anything a caller meant.
        Assert.False(PathSafety.TryResolveSafe("export.xlsx", out _, out var err));
        Assert.Contains("absolute", err);
    }

    [Fact]
    public void UncPath_IsAccepted_WithOrWithoutTheLegacyFlag()
    {
        // allowUnc used to gate this. Network shares are now accepted for every tool, so
        // the parameter no longer changes the outcome; it stays for source compatibility.
        Assert.True(PathSafety.TryResolveSafe(@"\\server\share\model.rvt", out var a, out _));
        Assert.True(PathSafety.TryResolveSafe(@"\\server\share\model.rvt", out var b, out _, allowUnc: true));
        Assert.Equal(a, b);
        Assert.Equal(@"\\server\share\model.rvt", a);
    }

    [Fact]
    public void ForwardSlashUncPath_IsNormalized()
    {
        Assert.True(PathSafety.TryResolveSafe("//server/share/links/grid.ifc", out var resolved, out _));
        Assert.StartsWith(@"\\server\share", resolved);
    }

    [Fact]
    public void CanWriteTo_AllowsANewFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "rivett-" + Guid.NewGuid().ToString("N") + ".csv");
        Assert.True(PathSafety.CanWriteTo(path, overwrite: false, out _));
    }

    [Fact]
    public void CanWriteTo_RefusesAnExistingFileUnlessAsked()
    {
        var path = Path.Combine(Path.GetTempPath(), "rivett-" + Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(path, "existing");
        try
        {
            // Overwriting is not the same act as writing: destroying someone's file must be
            // asked for, not inferred from the fact that a path was given.
            Assert.False(PathSafety.CanWriteTo(path, overwrite: false, out var err));
            Assert.Contains("already exists", err);
            Assert.Contains("overwrite=true", err);

            Assert.True(PathSafety.CanWriteTo(path, overwrite: true, out _));
            // And the file is left untouched by the check itself.
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
