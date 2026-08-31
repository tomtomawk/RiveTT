using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Creates a grid system with specified counts, spacing, and labeling.
/// </summary>
[ToolSafety(false, true, supportsDryRun: true)]
public class CreateGridTool : IRiveTTTool
{
    public string Name => "create_grid";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a grid system, or renames/deletes an existing grid. Actions: create (default), rename, delete. "
        + "Previews by default: the dry run really creates the grids in a transaction, reports the names Revit "
        + "assigned and the label conflicts it resolved, then rolls back. Set dryRun=false to apply.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "create").ToLowerInvariant();
        if (action == "rename") return RenameGrid(doc, input, session);
        if (action == "delete") return DeleteGrid(doc, input, session);
        if (action != "create")
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Unknown action: {action}", suggestion: "Use: create, rename, delete");

        var xCount = input["xCount"]?.Value<int>() ?? 0;
        var yCount = input["yCount"]?.Value<int>() ?? 0;
        var xSpacingMm = input["xSpacing"]?.Value<double>() ?? 5000;
        var ySpacingMm = input["ySpacing"]?.Value<double>() ?? 5000;
        var xStartLabel = input["xStartLabel"]?.Value<string>() ?? "A";
        var yStartLabel = input["yStartLabel"]?.Value<string>() ?? "1";
        var xNaming = input["xNamingStyle"]?.Value<string>() ?? "alphabetic";
        var yNaming = input["yNamingStyle"]?.Value<string>() ?? "numeric";
        var elevationMm = input["elevation"]?.Value<double>() ?? 0;
        var xExtentMinMm = input["xExtentMin"]?.Value<double>() ?? -5000;
        var xExtentMaxMm = input["xExtentMax"]?.Value<double>() ?? (yCount > 0 ? yCount * ySpacingMm + 5000 : 30000);
        var yExtentMinMm = input["yExtentMin"]?.Value<double>() ?? -5000;
        var yExtentMaxMm = input["yExtentMax"]?.Value<double>() ?? (xCount > 0 ? xCount * xSpacingMm + 5000 : 30000);

        if (xCount <= 0 && yCount <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "At least one of xCount or yCount must be > 0");

        var dryRun = ToolHelpers.GetDryRun(input);

        try
        {
            var createdGrids = new List<object>();
            var warnings = new List<string>();
            var z = elevationMm / MmPerFoot;

            // Collect existing grid names for conflict detection
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(Grid))
                    .Cast<Grid>().Select(g => g.Name),
                StringComparer.OrdinalIgnoreCase);

            using var tx = new Transaction(doc, "RiveTT: Create Grid");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            // X grids (vertical lines, labeled alphabetically by default)
            for (int i = 0; i < xCount; i++)
            {
                var x = i * xSpacingMm / MmPerFoot;
                var start = new XYZ(x, xExtentMinMm / MmPerFoot, z);
                var end = new XYZ(x, xExtentMaxMm / MmPerFoot, z);
                var line = Line.CreateBound(start, end);
                var grid = Grid.Create(doc, line);
                var label = GenerateLabel(xStartLabel, i, xNaming);
                if (existingNames.Contains(label))
                    warnings.Add($"Grid label '{label}' already exists, auto-assigned '{grid.Name}'.");
                else if (TrySetName(grid, label))
                    existingNames.Add(label);
                createdGrids.Add(dryRun
                    ? (object)new { axis = "X", name = grid.Name, requestedLabel = label, position = i * xSpacingMm }
                    : new { id = ToolHelpers.GetElementIdValue(grid.Id), axis = "X", name = grid.Name, requestedLabel = label, position = i * xSpacingMm });
            }

            // Y grids (horizontal lines, labeled numerically by default)
            for (int i = 0; i < yCount; i++)
            {
                var y = i * ySpacingMm / MmPerFoot;
                var start = new XYZ(yExtentMinMm / MmPerFoot, y, z);
                var end = new XYZ(yExtentMaxMm / MmPerFoot, y, z);
                var line = Line.CreateBound(start, end);
                var grid = Grid.Create(doc, line);
                var label = GenerateLabel(yStartLabel, i, yNaming);
                if (existingNames.Contains(label))
                    warnings.Add($"Grid label '{label}' already exists, auto-assigned '{grid.Name}'.");
                else if (TrySetName(grid, label))
                    existingNames.Add(label);
                createdGrids.Add(dryRun
                    ? (object)new { axis = "Y", name = grid.Name, requestedLabel = label, position = i * ySpacingMm }
                    : new { id = ToolHelpers.GetElementIdValue(grid.Id), axis = "Y", name = grid.Name, requestedLabel = label, position = i * ySpacingMm });
            }

            // The grids exist at this point, named and de-conflicted by Revit itself. The
            // rollback is what makes it a preview: the caller learns the labels it will
            // really get -- including the ones Revit renamed -- without keeping them.
            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    $"DryRun: would create {createdGrids.Count} grid(s)"
                    + (warnings.Count > 0 ? $", with {warnings.Count} label conflict(s) resolved by Revit." : "."),
                    new { createdCount = createdGrids.Count, grids = createdGrids, warnings });
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return RiveTTResult<object>.Ok(new
            {
                createdCount = createdGrids.Count,
                grids = createdGrids,
                warnings
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed to create grid: {ex.Message}");
        }
    }

    private static string GenerateLabel(string start, int index, string style)
    {
        if (style == "alphabetic")
        {
            // A..Z, AA..AZ, BA..
            int charIndex = 0;
            if (start.Length == 1 && char.IsLetter(start[0]))
                charIndex = char.ToUpper(start[0]) - 'A';
            int total = charIndex + index;
            if (total < 26) return ((char)('A' + total)).ToString();
            return ((char)('A' + total / 26 - 1)).ToString() + ((char)('A' + total % 26)).ToString();
        }
        // numeric
        if (int.TryParse(start, out var startNum))
            return (startNum + index).ToString();
        return (index + 1).ToString();
    }

    private static bool TrySetName(Grid grid, string name)
    {
        try { grid.Name = name; return true; }
        catch { return false; }
    }

    private static RiveTTResult<object> RenameGrid(Document doc, JObject input, RiveTTSession session)
    {
        var (grid, error) = ResolveGrid(doc, input);
        if (error != null) return error;

        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(newName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "newName is required for rename");

        var clash = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
            .FirstOrDefault(g => g.Id != grid!.Id && g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"A grid named '{newName}' already exists");

        var oldName = grid!.Name;

        if (ToolHelpers.GetDryRun(input))
            return ChangePreview.Declared(
                $"DryRun: would rename the grid '{oldName}' to '{newName}'.",
                new { action = "rename", gridId = ToolHelpers.GetElementIdValue(grid.Id), oldName, newName });
        using var tx = new Transaction(doc, "RiveTT: Rename Grid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        grid.Name = newName;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "rename", gridId = ToolHelpers.GetElementIdValue(grid.Id), oldName, newName });
    }

    private static RiveTTResult<object> DeleteGrid(Document doc, JObject input, RiveTTSession session)
    {
        var (grid, error) = ResolveGrid(doc, input);
        if (error != null) return error;

        var name = grid!.Name;

        // Deleting a grid drags its dimensions and constraints with it; DeletionPreview
        // probes the real cascade rather than naming the grid alone.
        if (ToolHelpers.GetDryRun(input))
            return DeletionPreview.Build(doc, grid.Id, $"Grid '{name}'",
                new { action = "delete", gridId = ToolHelpers.GetElementIdValue(grid.Id), deletedGrid = name });

        using var tx = new Transaction(doc, "RiveTT: Delete Grid");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        doc.Delete(grid.Id);
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new { action = "delete", deletedGrid = name });
    }

    /// <summary>Resolves a grid by gridId or name from the input.</summary>
    private static (Grid?, RiveTTResult<object>?) ResolveGrid(Document doc, JObject input)
    {
        var gridIdLong = input["gridId"]?.Value<long?>() ?? 0;
        var name = input["name"]?.Value<string>();

        Grid? grid = null;
        if (gridIdLong > 0)
            grid = doc.GetElement(ToolHelpers.ToElementId(gridIdLong)) as Grid;
        if (grid == null && !string.IsNullOrEmpty(name))
            grid = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (grid == null)
            return (null, RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                "Grid not found", suggestion: "Provide a valid gridId or name"));

        return (grid, null);
    }
}
