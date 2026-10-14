using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;
using RiveTT.Tools.Elements;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Non-regression for the field reports written against 0.5.2 (docs/CHANGELOG_0.6.2.md §6):
///   1. place_viewport dryRun:true read the viewport AFTER the rollback and failed with
///      "The referenced object is not valid";
///   2. an argument no tool declares was dropped silently and a DIFFERENT call ran
///      (export_elements_data(category=...) exported 10 208 elements);
///   3. export_elements_data without parameterNames emptied the columns of two homonymous sheet
///      parameters (SHEET_NUMBER / VIEWER_SHEET_NUMBER), for 41 sheets, with no signal.
/// The pure parts run for real; what needs Revit is locked on the source.
/// </summary>
public class FieldReport0_5_2RegressionTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    // --- 1. place_viewport dryRun ---------------------------------------------------------------

    [Fact]
    public void PlaceViewportBuildsItsPayloadBeforeTheDryRunRollback()
    {
        var tool = Source("RiveTT.Tools", "Views", "PlaceViewportTool.cs");

        var payload = tool.IndexOf("var payload = new", StringComparison.Ordinal);
        var rollback = tool.IndexOf("ChangePreview.Rollback(tx)", StringComparison.Ordinal);
        Assert.True(payload > 0, "the payload must be built once, into a variable");
        Assert.True(rollback > payload,
            "reading the viewport after the rollback is what threw 'The referenced object is not valid'");

        // The preview must hand over the ALREADY BUILT payload, never an inline object that reads
        // the (rolled back) viewport while it is being constructed.
        var probed = tool.IndexOf("ChangePreview.Probed(", rollback, StringComparison.Ordinal);
        Assert.True(probed > rollback);
        var call = tool.Substring(probed, 400);
        Assert.Contains("payload);", call);
        Assert.DoesNotContain("viewport.Id", call);
        Assert.DoesNotContain("viewport.Rotation", call);
    }

    // --- 2. unknown arguments are refused, not dropped --------------------------------------------

    [Theory]
    [InlineData("export_elements_data", "category")]
    [InlineData("export_elements_data", "filterCategory")]
    [InlineData("filter_by_parameter_value", "filterCategory")]
    [InlineData("filter_by_parameter_value", "filterRule")]
    [InlineData("filter_by_parameter_value", "filterValue")]
    [InlineData("override_graphics", "notAParameter")]
    [InlineData("filter_elements", "data")]
    public void AnArgumentTheToolDoesNotDeclareIsReportedNotDropped(string tool, string key)
    {
        var unrecognized = ToolArgumentGuard.Unrecognized(tool, new[] { key });

        Assert.Equal(new[] { key }, unrecognized);
    }

    [Theory]
    [InlineData("export_elements_data", "categories")]
    [InlineData("export_elements_data", "parameterNames")]
    [InlineData("duplicate_view", "newName")]
    [InlineData("duplicate_view", "viewId")]
    [InlineData("override_graphics", "cutForegroundPatternId")]
    [InlineData("filter_by_parameter_value", "parameterName")]
    [InlineData("place_viewport", "enforcePosition")]
    public void TheDeclaredArgumentsAreAccepted(string tool, string key)
    {
        Assert.Empty(ToolArgumentGuard.Unrecognized(tool, new[] { key }));
    }

    [Fact]
    public void ClientMetadataKeysAndDryRunOnAReadToolAreNotRefused()
    {
        Assert.Empty(ToolArgumentGuard.Unrecognized("get_current_view_elements", new[] { "_meta", "dryRun" }));
    }

    [Fact]
    public void ARefusalNamesTheWrongKeyThePossibleRightOneAndTheAcceptedList()
    {
        var rejection = ToolArgumentGuard.Rejection("export_elements_data", new[] { "category" });

        var error = rejection["error"]!;
        Assert.False(rejection["success"]!.Value<bool>());
        Assert.Equal("InvalidInput", error["code"]!.Value<string>());
        Assert.False(error["modelChanged"]!.Value<bool>());
        Assert.Contains("'category' -> 'categories'", error["suggestion"]!.Value<string>());
        var context = (JObject)error["context"]!;
        Assert.Equal("category", context["unrecognizedParameters"]![0]!.Value<string>());
        Assert.Contains("categories", context["acceptedParameters"]!.Select(t => t.Value<string>()));
    }

    [Fact]
    public void TheGuardIsWiredIntoTheServerPipeline()
    {
        var program = Source("RiveTT.Server", "Program.cs");
        Assert.Contains("AddCallToolFilter", program);
        Assert.Contains("ToolArgumentGuard.Check(context.Params)", program);
    }

    [Fact]
    public void EveryWrapperParameterIsKnownToTheGuardSoNoValidCallIsRefused()
    {
        // The accepted names are built from the wrappers by reflection: this fails if the map
        // is empty or loses a tool, which would silently turn the guard off or refuse everything.
        foreach (var tool in new[] { "export_elements_data", "duplicate_view", "place_viewport", "modify_element", "create_room" })
            Assert.NotNull(ToolArgumentGuard.AcceptedParameters(tool));
        Assert.Contains("start", ToolArgumentGuard.AcceptedParameters("modify_element")!);
        Assert.Contains("end", ToolArgumentGuard.AcceptedParameters("modify_element")!);
    }

    // --- 3. homonymous parameters ----------------------------------------------------------------

    [Fact]
    public void ANonEmptyValueIsNotErasedByAnEmptyHomonym()
    {
        // SHEET_NUMBER = "A-101", VIEWER_SHEET_NUMBER = "" (listed second): the column was empty.
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Numéro de la feuille", "A-101");
        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Numéro de la feuille", "");

        Assert.Equal("A-101", bag["Numéro de la feuille"]);
        Assert.Empty(ambiguous);
    }

    [Fact]
    public void AnEmptyValueIsReplacedByTheNonEmptyHomonymListedAfterIt()
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Jeu de feuilles", "");
        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Jeu de feuilles", "PRO");

        Assert.Equal("PRO", bag["Jeu de feuilles"]);
        Assert.Empty(ambiguous);
    }

    [Fact]
    public void TwoDifferentNonEmptyHomonymsKeepTheFirstAndAreReported()
    {
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Numéro de la feuille", "A-101");
        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Numéro de la feuille", "A-101");
        ExportElementsDataTool.AddNamedValue(bag, ambiguous, "Numéro de la feuille", "B-202");

        Assert.Equal("A-101", bag["Numéro de la feuille"]);
        Assert.Equal(new[] { "A-101", "B-202" }, ambiguous["Numéro de la feuille"].OrderBy(v => v).ToArray());
    }

    [Fact]
    public void TheExportNoLongerAssignsParametersByNameWithLastOneWins()
    {
        var tool = Source("RiveTT.Tools", "Elements", "ExportElementsDataTool.cs");
        Assert.DoesNotContain("instanceParams[defName!] =", tool);
        Assert.DoesNotContain("typeParams[defName!] =", tool);
        Assert.Contains("ambiguousParameterNames", tool);
    }

    // --- the other defects of the same reports, on the source ---------------------------------------

    [Fact]
    public void ModifyElementDryRunCommitsInsideAGroupSoRevitCommitFailuresAreSeen()
    {
        var tool = Source("RiveTT.Tools", "Elements", "ModifyElementTool.cs");
        Assert.Contains("new TransactionGroup(doc", tool);
        Assert.Contains("probeGroup.RollBack()", tool);
        Assert.DoesNotContain("if (!dryRun && tx.Commit()", tool);
        Assert.Contains("set_endpoints", tool);
        Assert.Contains("location.Curve = Line.CreateBound(newStart, newEnd)", tool);
    }

    [Fact]
    public void CreateWallReportsARefusedCommitAsTransactionFailedWithTheRealCause()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreateLineBasedElementTool.cs");
        var refused = tool.IndexOf("refusedCommit != null", StringComparison.Ordinal);
        Assert.True(refused > 0);
        var block = tool.Substring(refused, 300);
        Assert.Contains("RiveTTErrorCode.TransactionFailed", block);
        Assert.Contains("detach_wall_constraint", tool);
    }

    [Fact]
    public void CreateRoomNamesTheFieldARoomRefusesInsteadOfFailingTheCall()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreateRoomTool.cs");
        Assert.Contains("SetField(room, BuiltInParameter.ROOM_NAME", tool);
        Assert.Contains("fieldsFailed", tool);
    }

    [Fact]
    public void ACustomVolumeKeepsWhatIsInsideAndLeavesOutCamerasLinksAndSketches()
    {
        var tool = Source("RiveTT.Tools", "Elements", "GetElementsInSpatialVolumeTool.cs");
        Assert.Contains("IsWithin(e,", tool);
        Assert.Contains("IsNonGeometric(e)", tool);
        Assert.Contains("\"intersect\"", tool);
    }

    [Fact]
    public void ViewportsCanBeReadBackAndDuplicateViewTakesANameAndHatchingHasItsParameters()
    {
        Assert.Contains("centreMm", Source("RiveTT.Tools", "Elements", "GetCurrentViewElementsTool.cs"));
        Assert.Contains("input[\"newName\"]", Source("RiveTT.Tools", "Views", "DuplicateViewTool.cs"));
        Assert.Contains("SetCutForegroundPatternId", Source("RiveTT.Tools", "Views", "OverrideGraphicsTool.cs"));
        Assert.Contains("sheetCollection", Source("RiveTT.Tools", "Sheets", "BatchCreateSheetsTool.cs"));
        Assert.Contains("DiagnoseRoomEnclosure", Source("RiveTT.Server", "Tools", "ElementTools.cs"));
    }
}
