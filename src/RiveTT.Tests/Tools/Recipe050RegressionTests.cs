using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Tools;
using RiveTT.Tools.Project;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

public class Recipe050RegressionTests
{
    [Theory]
    [InlineData("{\"baseLevelId\":608,\"baseOffset\":100}", 3000, 100)]
    [InlineData("{\"baseElevationMm\":3500,\"baseOffset\":100}", 3000, 600)]
    public void LevelIdIsNeverConvertedIntoMillimetres(string json, double levelElevation, double expectedOffset)
        => Assert.Equal(expectedOffset, LineBaseConstraint.Parse(JObject.Parse(json)).RelativeOffsetMm(levelElevation));

    [Theory]
    [InlineData("{\"baseLevel\":608,\"baseLevelId\":609}")]
    [InlineData("{\"baseLevel\":608.5}")]
    [InlineData("{\"baseLevelId\":608,\"baseElevationMm\":0}")]
    [InlineData("{\"baseLevelId\":-1}")]
    [InlineData("{\"baseLevelId\":608.5}")]
    [InlineData("{\"baseLevel\":608}")]
    [InlineData("{\"baseLevel\":0}")]
    public void AmbiguousLevelInputsAreRejected(string json)
        => Assert.Throws<ArgumentException>(() => LineBaseConstraint.Parse(JObject.Parse(json)));

    [Fact]
    public void DimensionSentinelsAndMissingSegmentsCannotBecomeSuccesses()
    {
        Assert.True(DimensionMeasurementValidation.IsValid(new double?[] { 1, 2 }));
        foreach (var bad in new double?[] { null, -2, -1, 0, double.NaN, double.PositiveInfinity })
            Assert.False(DimensionMeasurementValidation.IsValid(new double?[] { 1, bad }));
        Assert.False(DimensionMeasurementValidation.IsValid(Array.Empty<double?>()));
    }

    [Fact]
    public void HeadingsAreNotConfusedWithIdenticalParameterData()
    {
        var headings = new[] { "Type", "Nom" };
        Assert.True(ScheduleRowClassification.IsColumnHeading(headings, headings, new[] { true, true }));
        Assert.False(ScheduleRowClassification.IsColumnHeading(headings, headings, new[] { false, false }));
        Assert.False(ScheduleRowClassification.IsColumnHeading(headings, new[] { "Porte", "A" }, new[] { true, true }));
        Assert.False(ScheduleRowClassification.IsColumnHeading(headings, new[] { "Type" }, new[] { true }));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(10, 10, false)]
    [InlineData(-0.1, 5, true)]
    [InlineData(5, 10.1, true)]
    public void CropCheckIncludesBoundaryAndRejectsOutside(double x, double y, bool outside)
        => Assert.Equal(outside, ViewCropDiagnostics.IsOutside(x, y, 0, 0, 10, 10));

    [Theory]
    [InlineData(".RVT", true)]
    [InlineData(".rfa", true)]
    [InlineData(".rte", true)]
    [InlineData(".rft", true)]
    [InlineData(".ifc", true)]
    [InlineData(".dwg", false)]
    [InlineData(".pdf", false)]
    [InlineData(".exe", false)]
    public void OpenFileOnlyRoutesRevitDocumentFormats(string extension, bool supported)
        => Assert.Equal(supported, OpenFileTool.SupportsExtension(extension));

    [Fact]
    public void LibraryReadsDoNotGrantWriteAccessToProgramData()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT 2027", "Family Templates", "Generic Model.rft");
        Assert.False(PathSafety.TryResolveSafe(path, out _, out _));
        Assert.True(PathSafety.TryResolveSafe(path, out _, out _, allowRevitLibraryRead: true));
        Assert.False(PathSafety.TryResolveSafe(Path.ChangeExtension(path, ".exe"), out _, out _, allowRevitLibraryRead: true));
        Assert.False(PathSafety.TryResolveSafe(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "blocked.rft"),
            out _, out _, allowRevitLibraryRead: true));
    }

    [Fact]
    public void CompactParameterResponseKeepsUnitsResolutionAndEmptyValues()
    {
        var payload = JObject.Parse("""
        {"execution":{"pluginVersion":"0.5.0.0"},"foundCount":1,"elements":[{"elementId":608,
        "parameters":[{"name":"Nom du type","requestedName":"Type Name","value":null,"hasValue":false},
        {"name":"Longueur","value":1000,"unit":"mm","internalValue":3.280839895,"hasValue":true}]}]}
        """);
        var result = ToolResponseShaper.Shape("get_element_parameters", payload, true, false);
        Assert.Equal(2, ((JArray)result["elements"]![0]!["parameters"]!).Count);
        Assert.Equal("Type Name", result["elements"]![0]!["parameters"]![0]!["requestedName"]);
        Assert.Equal("mm", result["elements"]![0]!["parameters"]![1]!["unit"]);
        Assert.Equal(3.280839895, result["elements"]![0]!["parameters"]![1]!["internalValue"]!.Value<double>());
        Assert.True(JToken.DeepEquals(payload["execution"], result["execution"]));
    }

    [Fact]
    public void EveryFailureHandlerIsInstalledAfterStartingItsTransaction()
    {
        var root = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "RiveTT.Tools"));
        var count = 0;
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(source,
                @"(?m)^\s*(?:var \w+ = )?TransactionFailureHandling\.(?:SuppressWarnings|FromInput)\((\w+)"))
            {
                count++;
                var tx = call.Groups[1].Value;
                var prefix = source[..call.Index];
                var creation = prefix.LastIndexOf(tx + " = new Transaction(", StringComparison.Ordinal);
                var start = prefix.LastIndexOf(tx + ".Start(", StringComparison.Ordinal);
                Assert.True(start >= 0 && start > creation,
                    $"{file}: {tx} must start before failure handling is installed.");
            }
        }
        Assert.True(count >= 180, "The transaction scan must cover the entire runtime.");
    }

    [Fact]
    public void DimensionAttemptsHaveIndependentRollbackAndOriginalGeometryReferences()
    {
        var source = Read("Annotations", "CreateDimensionsTool.cs");
        Assert.Contains("new SubTransaction(doc)", source);
        Assert.Contains("itemTx.RollBack()", source);
        Assert.Contains("dim.AreReferencesAvailable", source);
        Assert.Contains("DimensionMeasurementValidation.IsValid(values)", source);
        Assert.Contains("instance.GetSymbolGeometry()", source);
        Assert.DoesNotContain(".GetInstanceGeometry()", source);
        Assert.Contains("linePoint - measurementDirection * half", source);
        Assert.Contains("normal.CrossProduct(measurementDirection)", source);
    }

    [Fact]
    public void LinkLifecycleCallsDoNotOpenTransactions()
    {
        var source = Read("Project", "ManageLinksTool.cs");
        foreach (var method in new[] { "ReloadLink", "UnloadLink", "ReloadLinkFrom" })
        {
            var start = source.IndexOf("private static RiveTTResult<object> " + method + "(", StringComparison.Ordinal);
            var end = source.IndexOf("private static RiveTTResult<object>", start + 1, StringComparison.Ordinal);
            var body = source[start..end];
            Assert.DoesNotContain("new Transaction(", body);
            Assert.Contains("clearsUndoHistory = true", body);
        }
    }

    [Fact]
    public void ListingSavedSelectionsCannotTriggerTheLoadSelectionBranch()
    {
        var source = Read("Elements", "ManageSelectionTool.cs");
        Assert.Contains("\"list\" => Load(doc, input, requireName: false)", source);
        Assert.Contains("var name = requireName ? input[\"name\"]?.Value<string>() : null;", source);
        Assert.True(source.IndexOf("if (string.IsNullOrEmpty(name))", source.IndexOf("private static RiveTTResult<object> Load(", StringComparison.Ordinal), StringComparison.Ordinal)
            < source.IndexOf("uidoc.Selection.SetElementIds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("C:relative.rvt")]
    [InlineData(@"\relative.rvt")]
    [InlineData("relative.rvt")]
    public void FilePathsCannotDependOnRevitsWorkingDirectory(string path)
        => Assert.False(PathSafety.TryResolveSafe(path, out _, out _));

    [Fact]
    public void InstallerRejectsPackagedContextAndReportsShadowServers()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "builder", "installer", "RiveTT.iss")));
        Assert.Contains("GetCurrentPackageFullName", source);
        var initialize = source[source.IndexOf("function InitializeSetup(): Boolean;", StringComparison.Ordinal)..];
        Assert.True(initialize.IndexOf("InstallationContextIsUnpackaged()", StringComparison.Ordinal)
            < initialize.IndexOf("ServerIsFree()", StringComparison.Ordinal));
        Assert.Contains("LocalCache\\Local\\RiveTT\\server\\RiveTT.Server.exe", source);
        Assert.Contains("VirtualizedCopies := FindVirtualizedServerCopies()", source);
    }

    private static string Read(string folder, string file) => File.ReadAllText(Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "RiveTT.Tools", folder, file)));
}
