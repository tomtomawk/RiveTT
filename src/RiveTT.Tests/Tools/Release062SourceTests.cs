using System.IO;
using System.Linq;
using RiveTT.Core.Results;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// 0.6.2 (docs/CHANGELOG_0.6.2.md): load_from_open, open_family on a project family, and the ILO
/// field report of 2026-10-13. The pure part (the suppressed-warning log) is tested for real; what
/// only a live Revit session can prove is locked on the source and listed for live verification in
/// the changelog.
/// </summary>
public class Release062SourceTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    // --- suppressed warnings: the text, not just the count ----------------------------------------

    [Fact]
    public void SuppressedWarningLogGroupsIdenticalMessagesInOrderOfFirstAppearance()
    {
        SuppressedWarningLog.Clear();
        SuppressedWarningLog.Record("Highlighted walls overlap.");
        SuppressedWarningLog.Record("Viewport is pinned.");
        SuppressedWarningLog.Record("Highlighted walls overlap.");
        SuppressedWarningLog.Record("   ");
        SuppressedWarningLog.Record(null);

        var drained = SuppressedWarningLog.Drain();

        Assert.Equal(2, drained.Count);
        Assert.Equal(("Highlighted walls overlap.", 2), drained[0]);
        Assert.Equal(("Viewport is pinned.", 1), drained[1]);
        Assert.Empty(SuppressedWarningLog.Drain());
    }

    [Fact]
    public void TheFailuresPreprocessorRecordsAndTheRouterSurfacesTheSuppressedWarnings()
    {
        var capture = Source("RiveTT.Tools", "Utilities", "TransactionFailureHandling.cs");
        Assert.Contains("SuppressedWarningLog.Record(failure.GetDescriptionText())", capture);

        var router = Source("RiveTT.Plugin", "RiveTTRouter.cs");
        Assert.Contains("SuppressedWarningLog.Clear()", router);
        Assert.Contains("SuppressedWarningLog.Drain()", router);
        Assert.Contains("\"suppressedWarnings\"", router);
    }

    // --- load_from_open ---------------------------------------------------------------------------

    [Fact]
    public void LoadFamilyPreviewsOnlyLoadFromOpenAndRefusesDryRunOnTheOtherActions()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        Assert.Contains("[ToolSafety(false, false, supportsDryRun: true)]", tool);
        // The router reads supportsDryRun per TOOL: without this guard, load + dryRun:true would LOAD.
        Assert.Contains("!string.Equals(action, \"load_from_open\", StringComparison.OrdinalIgnoreCase)", tool);
        Assert.Contains("does not support dryRun", tool);
    }

    [Fact]
    public void LoadFromOpenReadsEveryParameterTheWrapperPublishes()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        var wrapper = Source("RiveTT.Server", "Tools", "ProjectTools.cs");
        foreach (var key in new[] { "sourceDocument", "targetDocument", "renameTo", "activateTypes", "closeSource" })
        {
            Assert.Contains($"input[\"{key}\"]", tool);
            Assert.Contains($"[\"{key}\"]", wrapper);
        }
    }

    [Fact]
    public void LoadFromOpenLoadsOutsideAnyTransactionAndNeverFailsTheLoadOnACloseRefusal()
    {
        var tool = Source("RiveTT.Tools", "Elements", "LoadFamilyTool.cs");
        var load = tool.IndexOf("source.LoadFamily(target", System.StringComparison.Ordinal);
        var transaction = tool.IndexOf("RiveTT - load_from_open", System.StringComparison.Ordinal);
        Assert.True(load > 0 && transaction > load, "LoadFamily must run before, and outside, the rename/activation transaction");
        Assert.Contains("sourceClosed", tool);
        Assert.Contains("sourceCloseReason", tool);
        Assert.Contains("transaction ouverte sur", tool);
    }

    // --- open_family on a family of the project -----------------------------------------------------

    [Fact]
    public void OpenFamilyAcceptsAFamilyOfTheProjectOnBothSides()
    {
        var tool = Source("RiveTT.Tools", "Project", "FamilyAndTemplateDocumentTools.cs");
        var wrapper = Source("RiveTT.Server", "Tools", "DocumentTools.cs");
        Assert.Contains("target.EditFamily(family)", tool);
        foreach (var key in new[] { "familyId", "familyName", "targetDocument", "activate" })
        {
            Assert.Contains($"input[\"{key}\"]", tool);
            Assert.Contains($"[\"{key}\"]", wrapper);
        }
        Assert.Contains("load_from_open", tool);
        Assert.Contains("load_from_open", wrapper);
    }

    // --- ILO field report 13/10/2026 ----------------------------------------------------------------

    [Fact]
    public void DuplicateFamilyTypeReadsABareLengthInMillimetresAndReportsTheUnit()
    {
        var tool = Source("RiveTT.Tools", "Elements", "DuplicateFamilyTypeTool.cs");
        Assert.Contains("UnitTypeId.Millimeters", tool);
        Assert.Contains("SpecTypeId.Length", tool);
        Assert.Contains("internalValue = applied.InternalValue", tool);
        Assert.Contains("[ToolSafety(false, false, supportsDryRun: true)]", tool);
    }

    [Fact]
    public void PlaceViewportReportsWhereTheViewportIsNotWhatWasAskedFor()
    {
        var tool = Source("RiveTT.Tools", "Views", "PlaceViewportTool.cs");
        Assert.Contains("requestedCentreMm", tool);
        Assert.Contains("centreDeviationMm", tool);
        Assert.Contains("viewport.GetBoxCenter()", tool);
        Assert.Contains("moveIfPlaced", tool);
        Assert.Contains("moveIfPlaced", Source("RiveTT.Server", "Tools", "ViewTools.cs"));
        // The recorded position can win over the one passed to Create: SetBoxCenter on request.
        Assert.Contains("viewport.SetBoxCenter(position)", tool);
        Assert.Contains("[\"enforcePosition\"] = enforcePosition", Source("RiveTT.Server", "Tools", "ViewTools.cs"));
    }

    [Fact]
    public void ManageViewTemplatesCreatesATemplateFromAView()
    {
        var tool = Source("RiveTT.Tools", "Views", "ManageViewTemplatesTool.cs");
        Assert.Contains("\"create_from_view\" => CreateFromView(doc, input)", tool);
        Assert.Contains("view.CreateViewTemplate()", tool);
        Assert.Contains("create_from_view", Source("RiveTT.Server", "Tools", "ViewTools.cs"));
    }

    [Fact]
    public void ManageSheetSetsHandlesSheetCollectionsAndListingThemStaysReadOnly()
    {
        var tool = Source("RiveTT.Tools", "Project", "ManageSheetSetsTool.cs");
        Assert.Contains("[ReadOnlyActions(\"list\", \"list\", \"list_collections\")]", tool);
        Assert.Contains("SheetCollection.Create(doc, name!)", tool);
        Assert.Contains("sheet.SheetCollectionId = collection.Id", tool);
    }

    [Fact]
    public void BothSheetDuplicationToolsShareOneImplementationThatCopiesEveryTitleBlock()
    {
        var content = Source("RiveTT.Tools", "Sheets", "DuplicateSheetWithContentTool.cs");
        var views = Source("RiveTT.Tools", "Sheets", "DuplicateSheetWithViewsTool.cs");
        Assert.Contains("SheetDuplicator.Run(input, doc, Name, copyAnnotationsDefault: true, dryRun: dryRun)", content);
        Assert.Contains("SheetDuplicator.Run(input, doc, Name, copyAnnotationsDefault: false, dryRun: dryRun)", views);

        var shared = Source("RiveTT.Tools", "Sheets", "SheetDuplicator.cs");
        Assert.Contains("CopyTitleBlocks", shared);
        Assert.Contains("CopySheetParameters", shared);
        Assert.Contains("BuiltInParameter.SHEET_NUMBER", shared);

        var wrapper = Source("RiveTT.Server", "Tools", "ViewTools.cs");
        var start = wrapper.IndexOf("Name = \"duplicate_sheet_with_views\"", System.StringComparison.Ordinal);
        Assert.True(start >= 0);
        var section = wrapper.Substring(start, 3500);
        Assert.Contains("[\"newNumber\"] = newNumber", section);
    }

    [Fact]
    public void PlaceTitleBlockTakesAPositionSoASecondTitleBlockCanSitBesideTheFrame()
    {
        Assert.Contains("positionX", Source("RiveTT.Tools", "Elements", "CreateCurveElementTools.cs"));
        Assert.Contains("p[\"positionX\"]", Source("RiveTT.Server", "Tools", "SystemTypeAndCurveTools.cs"));
    }

    [Fact]
    public void OverrideGraphicsCanHideAndShowWholeCategoriesPerView()
    {
        var tool = Source("RiveTT.Tools", "Views", "OverrideGraphicsTool.cs");
        Assert.Contains("view.SetCategoryHidden(catId, hide)", tool);
        var wrapper = Source("RiveTT.Server", "Tools", "ViewTools.cs");
        Assert.Contains("(\"categories\", categories)", wrapper);
        Assert.Contains("(\"viewIds\", viewIds)", wrapper);
    }

    [Fact]
    public void ListWarningsCanSnapshotAndReturnOnlyTheNewOnes()
    {
        var tool = Source("RiveTT.Tools", "Project", "GetWarningsTool.cs");
        Assert.Contains("sinceSnapshot", tool);
        Assert.Contains("SnapshotKey", tool);
        Assert.Contains("[\"sinceSnapshot\"] = sinceSnapshot", Source("RiveTT.Server", "Tools", "ProjectTools.cs"));
    }

    [Fact]
    public void SendCodeTellsTheCallerToWarnTheUserAboutTheSavedScript()
    {
        Assert.Contains("scriptNotice", Source("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs"));
        var executor = Source("RiveTT.Tools", "CodeExecution", "RoslynExecutor.cs");
        Assert.Contains("sectionsNote", executor);
        Assert.Contains("ModificationOutsideTransactionException", executor);
    }

    [Fact]
    public void TheConnectorVersionIs062()
    {
        var props = File.ReadAllText(RepositoryFile.Path("Directory.Build.props"));
        Assert.Contains("<Version>0.6.2</Version>", props);
        Assert.Contains("<AssemblyVersion>0.6.2.0</AssemblyVersion>", props);
    }
}
