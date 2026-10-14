using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Tools.Elements;
using RiveTT.Tools.Views;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// The two field briefs of October 2026 (ferronnerie session; notices PC): send_code_to_revit saving and
/// arguments, text notes, batch_export PDF options, family extrusion, elevations, capture_view.
/// The pure parts run for real; the rest is locked on the source and listed for live verification in
/// docs/CHANGELOG_0.6.2.md.
/// </summary>
public class Release062BriefsTests
{
    private static string Source(params string[] parts) =>
        File.ReadAllText(RepositoryFile.Path(new[] { "src" }.Concat(parts).ToArray()));

    // --- send_code_to_revit -----------------------------------------------------------------------

    [Fact]
    public void TheScriptIsSavedBeforeTheSandboxAndTheCompilerCanRefuseIt()
    {
        var tool = Source("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs");
        var saved = tool.IndexOf("PersistScript(code!, scriptName, reusable, scriptArgs)", StringComparison.Ordinal);
        var sandbox = tool.IndexOf("CodeSandbox.Validate(code!)", StringComparison.Ordinal);
        var compile = tool.IndexOf("RoslynExecutor.CheckCompiles(", StringComparison.Ordinal);
        Assert.True(saved > 0 && saved < sandbox && sandbox < compile,
            "a dryRun, a sandbox refusal and a compile error must all leave a saved script for fromScript + edits");
        Assert.DoesNotContain("wouldSaveTo", tool);
        Assert.Contains("WithSavedScript(compileFailure", tool);
    }

    [Fact]
    public void SavedArgumentsFollowTheirScriptAndTheLatestOneWins()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rivett-args-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "20260101_000000_demo.args.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(folder, "20260102_000000_demo.args.json"), "{\"a\":2}");
            File.WriteAllText(Path.Combine(folder, "20260103_000000_other.args.json"), "{\"a\":3}");

            Assert.Equal(2, SendCodeToRevitTool.LoadSavedArgs(folder, "demo")!["a"]!.Value<int>());
            Assert.Null(SendCodeToRevitTool.LoadSavedArgs(folder, "missing"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void LargeArgumentsCanComeFromAFileAndTheChangeReportIsCompactByDefault()
    {
        var tool = Source("RiveTT.Tools", "Elements", "SendCodeToRevitTool.cs");
        var wrapper = Source("RiveTT.Server", "Tools", "ProjectTools.cs");
        foreach (var key in new[] { "scriptArgsFile", "mergeScriptArgs", "verboseChanges" })
        {
            Assert.Contains($"input[\"{key}\"]", tool);
            Assert.Contains($"p[\"{key}\"]", wrapper);
        }
        Assert.Contains("SafeArgsFile", tool);   // a plain .json name, no folder, no ..

        var executor = Source("RiveTT.Tools", "CodeExecution", "RoslynExecutor.cs");
        Assert.Contains("ToCompactReport", executor);
        Assert.Contains("createdAuxiliaryCount", executor);
        Assert.Contains("VerboseChangesRequested", executor);
    }

    [Fact]
    public void CompileErrorsNameTheClosestMembersAndTheKnownMistakes()
    {
        var worker = Source("RiveTT.Tools", "CodeExecution", "RoslynCompilerWorker.cs");
        Assert.Contains("MemberHint(d, compilation)", worker);
        Assert.Contains("SetPlainText", worker);
        Assert.Contains("CS1061", worker);
    }

    // --- text notes -------------------------------------------------------------------------------

    [Theory]
    [InlineData("Texte", true)]
    [InlineData("Text", true)]
    [InlineData("texte", true)]
    [InlineData("Contenu", true)]
    [InlineData("Police de texte", false)]
    [InlineData("Taille du texte", false)]
    public void TheContentOfATextNoteHasAColumnOfItsOwn(string column, bool expected)
    {
        Assert.Equal(expected, ExportElementsDataTool.IsTextColumn(column));
    }

    [Fact]
    public void TextNotesAreReadSearchedAndEditedThroughTheirFormattedText()
    {
        var tool = Source("RiveTT.Tools", "Annotations", "ManageTextNotesTool.cs");
        Assert.Contains("formatted.Find(search, start, caseSensitive, wholeWord)", tool);
        Assert.Contains("formatted.SetPlainText(range, replacement)", tool);
        Assert.Contains("plan.Note.SetFormattedText(plan.Formatted)", tool);
        // The same code serves the preview and the real call: nothing is written outside the transaction.
        Assert.Contains("[ReadOnlyActions(\"read\", \"read\", \"find\")]", tool);
        Assert.Contains("manage_text_notes", Source("RiveTT.Server", "Tools", "ElementTools.cs"));

        Assert.Contains("TryTextNoteColumn", Source("RiveTT.Tools", "Elements", "ExportElementsDataTool.cs"));
        Assert.Contains("textNote.GetFormattedText().GetPlainText()", Source("RiveTT.Tools", "Elements", "GetElementParametersTool.cs"));
        Assert.Contains("\"Text Notes\"", Source("RiveTT.Tools", "Utilities", "CategoryResolver.cs"));
    }

    // --- batch_export, list_files -------------------------------------------------------------------

    [Fact]
    public void BatchExportPdfTakesTheOptionsOfRevitsDialogAndReportsEachFile()
    {
        var tool = Source("RiveTT.Tools", "Project", "BatchExportTool.cs");
        var wrapper = Source("RiveTT.Server", "Tools", "CreationTools.cs");
        foreach (var key in new[] { "rasterQuality", "vectorQualityDpi", "colorMode", "paperFormat", "orientation",
                     "zoomPercent", "paperPlacement", "combine", "combinedFileName", "fileName", "overwriteExisting" })
        {
            Assert.Contains($"input[\"{key}\"]", tool);
            Assert.Contains($"p[\"{key}\"]", wrapper);
        }
        Assert.Contains("replacedExistingFile", tool);
        Assert.Contains("sizeBytes", tool);
    }

    [Fact]
    public void AFolderCanBeListedReadOnlyThroughThePathGate()
    {
        var tool = Source("RiveTT.Tools", "Project", "ListFilesTool.cs");
        Assert.Contains("[ToolSafety(true, false)]", tool);
        Assert.Contains("PathSafety.TryResolveSafe(directory", tool);
        Assert.Contains("list_files", Source("RiveTT.Server", "Tools", "ProjectTools.cs"));
    }

    // --- family extrusion ---------------------------------------------------------------------------

    [Fact]
    public void TheFamilyExtrusionIsValidatedFirstAndUsesNurbSplineNeverHermite()
    {
        var tool = Source("RiveTT.Tools", "Elements", "CreateFamilyExtrusionTool.cs");
        Assert.Contains("NurbSpline.CreateCurve(degree, knots, list", tool);
        Assert.DoesNotContain("HermiteSpline.Create", tool);
        Assert.Contains("ValidateLoops(loops, issues)", tool);
        Assert.Contains("MinSegmentMm = 1.2", tool);
        Assert.Contains("simplifiedCurves", tool);
        Assert.Contains("createdCurveTypes", tool);

        // Validation and the preview come before the transaction.
        var validate = tool.IndexOf("ValidateLoops(loops, issues)", StringComparison.Ordinal);
        var dryRun = tool.IndexOf("if (dryRun)", StringComparison.Ordinal);
        var transaction = tool.IndexOf("new Transaction(doc", StringComparison.Ordinal);
        Assert.True(validate < dryRun && dryRun < transaction);
        Assert.Contains("create_family_extrusion", Source("RiveTT.Server", "Tools", "ElementTools.cs"));
    }

    // --- elevations, slabs ------------------------------------------------------------------------

    [Fact]
    public void LevelsCarryProjectAndSharedElevationsAndSlabTopsCanBeMeasured()
    {
        var info = Source("RiveTT.Tools", "Project", "GetProjectInfoTool.cs");
        Assert.Contains("projectElevationMm", info);
        Assert.Contains("sharedElevationMm", info);
        Assert.Contains("measure_floor_top", Source("RiveTT.Server", "Tools", "ElementTools.cs"));
        Assert.Contains("sharedMm", Source("RiveTT.Tools", "Elements", "MeasureFloorTopTool.cs"));

        var resolver = Source("RiveTT.Tools", "Utilities", "ParameterNameResolver.cs");
        Assert.Contains("ROOF_BASE_LEVEL_PARAM", resolver);
        Assert.Contains("ROOF_SLOPE", resolver);
    }

    // --- capture_view -------------------------------------------------------------------------------

    [Fact]
    public void AnElevationMappingIsGivenInTheViewAxes()
    {
        // 1000 x 2000 mm crop of an elevation (right = +X, up = +Z), top-left corner at x 0, z 2000, in a
        // 500 x 1000 px image: 2 mm per pixel, no margin.
        var frame = new CaptureMapping.ViewFrame(true, 0, 0, 1000, 2000, "Elevation", false,
            new[] { 1.0, 0, 0 }, new[] { 0, 0, 1.0 }, new[] { 0.0, 0, 2000.0 });

        var mapping = JObject.FromObject(CaptureMapping.Compute(frame, 500, 1000));

        Assert.True(mapping["available"]!.Value<bool>());
        Assert.Equal(2.0, mapping["mmPerPixel"]!.Value<double>());
        Assert.Equal(new[] { 0.0, 0.0, 2000.0 }, mapping["originTopLeftModelMm"]!.Select(v => v.Value<double>()).ToArray());
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, mapping["upDirection"]!.Select(v => v.Value<double>()).ToArray());
    }

    [Fact]
    public void CaptureViewCanFrameTheModelAndHidesTheUsersSelection()
    {
        var tool = Source("RiveTT.Tools", "Views", "CaptureViewTool.cs");
        Assert.Contains("FitCropToModel", tool);
        Assert.Contains("element is DatumPlane", tool);
        Assert.Contains("hideSelection", tool);
        Assert.Contains("Selection.SetElementIds(savedSelection)", tool);
        var wrapper = Source("RiveTT.Server", "Tools", "VisualTools.cs");
        Assert.Contains("[\"fitToModel\"] = fitToModel", wrapper);
        Assert.Contains("[\"hideSelection\"] = hideSelection", wrapper);
    }
}
