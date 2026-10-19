using System.IO;
using System.Linq;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// 0.6.3 (docs/CHANGELOG_0.6.3.md): load_from_open on an ACTIVE source family, the family ElementId
/// report on reload, and the verification block. The decisions are tested for real; the Revit-typed
/// glue is locked on the source and listed for live verification in the changelog.
/// </summary>
public class Release063SourceTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    private const string ProjectPath = @"J:\Affaires\E23_005_PRO_001.rvt";
    private const string FamilyCopy = @"C:\Temp\RiveTT\OpenedFamilies\x\Haworth_Chair_Very-Task1.rfa";

    // --- defect 1: identity of the active document -------------------------------------------------

    [Fact]
    public void TheSameDocumentIsRecognisedWhenTheHandlesAreNotTheSameObject()
    {
        // The field report: the active family was not recognised, so the close ran on the active document.
        Assert.True(FamilyReloadSupport.SameDocument(false, FamilyCopy, FamilyCopy.ToUpperInvariant(),
            "Haworth_Chair_Very-Task1", "Haworth_Chair_Very-Task1"));
        Assert.True(FamilyReloadSupport.SameDocument(true, null, null, null, null));
    }

    [Fact]
    public void DifferentPathsAreDifferentDocumentsEvenWithTheSameTitle()
    {
        Assert.False(FamilyReloadSupport.SameDocument(false, FamilyCopy, ProjectPath, "A", "A"));
        Assert.False(FamilyReloadSupport.SameDocument(false, FamilyCopy, "", "A", "A"));
    }

    [Fact]
    public void TwoNeverSavedDocumentsAreMatchedByTitleOnly()
    {
        Assert.True(FamilyReloadSupport.SameDocument(false, "", null, "Famille1", "famille1"));
        Assert.False(FamilyReloadSupport.SameDocument(false, "", "", "Famille1", "Famille2"));
        Assert.False(FamilyReloadSupport.SameDocument(false, "", "", null, null));
    }

    [Fact]
    public void ABackgroundSourceIsClosedDirectly()
        => Assert.Equal(SourceClosePlan.CloseDirectly, FamilyReloadSupport.DecideClose(false, ProjectPath));

    [Fact]
    public void AnActiveSourceReactivatesTheSavedTargetBeforeClosing()
        => Assert.Equal(SourceClosePlan.ReactivateTargetThenClose, FamilyReloadSupport.DecideClose(true, ProjectPath));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnActiveSourceWithANeverSavedTargetIsTheOnlyRefusalAndSaysWhy(string? targetPath)
    {
        Assert.Equal(SourceClosePlan.TargetNeverSaved, FamilyReloadSupport.DecideClose(true, targetPath));
        var reason = FamilyReloadSupport.TargetNeverSavedReason("Haworth", "Projet1");
        Assert.Contains("never saved", reason);
        Assert.Contains("Projet1", reason);
    }

    [Fact]
    public void LoadFromOpenLoadsThenReactivatesThenClosesAndNeverRollsTheLoadBack()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        var load = tool.IndexOf("source.LoadFamily(target", System.StringComparison.Ordinal);
        var activate = tool.IndexOf("OpenAndActivateDocument(target.PathName)", System.StringComparison.Ordinal);
        var close = tool.IndexOf("source.Close(false)", System.StringComparison.Ordinal);
        Assert.True(load > 0 && activate > load && close > activate, "LoadFamily, then activation, then Close");
        Assert.DoesNotContain("ReferenceEquals(active, source)", tool);
        Assert.Contains("FamilyReloadSupport.SameDocument(active, source)", tool);
        Assert.Contains("DeferredSourceClose.Schedule", tool);
        Assert.Contains("wouldReactivate", tool);
        Assert.Contains("sourceClosePending", tool);
        Assert.Contains("session.Cache.InvalidateAll()", tool);
        Assert.Contains("documentTitleHint", tool);
        Assert.Contains("documentTitleHint", Source("RiveTT.Plugin", "RiveTTRouter.cs"));
    }

    // --- defect 2: family ElementId ------------------------------------------------------------------

    [Theory]
    [InlineData(9709680L, 11549513L, true)]
    [InlineData(9709680L, 9709680L, false)]
    public void AChangedFamilyIdIsReported(long previous, long current, bool changed)
        => Assert.Equal(changed, FamilyReloadSupport.IdChanged(previous, current));

    [Fact]
    public void ANewFamilyHasNoPreviousIdAndIsNeverReportedAsChanged()
        => Assert.False(FamilyReloadSupport.IdChanged(null, 42));

    [Fact]
    public void BothLoadActionsReportThePreviousFamilyId()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        Assert.Contains("familyIdChanged = FamilyReloadSupport.IdChanged(before?.FamilyId, loadedFamilyId)", tool);
        Assert.Contains("var familyIdChanged = FamilyReloadSupport.IdChanged(previousFamilyId, familyId)", tool);
        Assert.Contains("FindFamily(doc, Path.GetFileNameWithoutExtension(safeFamilyPath))", tool);
        Assert.Contains("previousFamilyId = before?.FamilyId", tool);
        Assert.Contains("previousFamilyId,", tool);
        // The overload with IFamilyLoadOptions on the source document is the one that keeps the Family.
        Assert.Contains("source.LoadFamily(target, new OverwritingFamilyLoadOptions(overwrite))", tool);
    }

    // --- defect 3: verification block (option B) -------------------------------------------------------

    [Fact]
    public void LoadFromOpenAndLoadReturnAVerificationReadInTheTarget()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        Assert.Equal(2, tool.Split("FamilyReloadSupport.BuildVerification(").Length - 1);
        var support = Source("RiveTT.Tools", "Utilities", "FamilyReloadSupport.cs");
        foreach (var key in new[] { "familyCountWithName", "instanceCount", "idPreserved", "typeIdsPreserved", "instancesPreserved" })
            Assert.Contains(key, support);
    }

    [Fact]
    public void TheReadOnlySandboxIsLeftAsItWas()
    {
        // Option A (app.Documents in readonly) was not taken: the sandbox surface does not grow.
        var analyzer = Source("RiveTT.Tools", "CodeExecution", "ReadOnlyScriptAnalyzer.cs");
        Assert.Contains("OpenAndActivateDocument", analyzer);
    }

    // --- version -----------------------------------------------------------------------------------------

    [Fact]
    public void PluginAndServerShareTheSingleVersionSource063()
    {
        var props = File.ReadAllText(RepositoryFile.Path("Directory.Build.props"));
        Assert.Contains("<Version>0.6.3</Version>", props);
        Assert.Contains("<AssemblyVersion>0.6.3.0</AssemblyVersion>", props);
        Assert.Contains("<FileVersion>0.6.3.0</FileVersion>", props);
    }
}
