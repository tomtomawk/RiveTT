using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Adds curtain grid lines and mullions to an EXISTING curtain wall/system/roof — the
/// wall itself is already creatable via create_line_based_element with a curtain wall
/// type, but nothing could subdivide or re-mullion it afterwards. CurtainGrid is
/// obtained from the host element (Wall.CurtainGrid, CurtainSystem.CurtainGrid), not
/// created directly.
/// </summary>
[ToolSafety(false, false)]
public class ManageCurtainGridTool : ICortexTool
{
    public string Name => "manage_curtain_grid";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Adds curtain grid lines and mullions to an existing curtain wall/system/roof. " +
        "action=add_grid_line|add_mullions|get_grid_info. hostElementId is required for every action. " +
        "add_grid_line needs direction (u|v) and offsetMm (distance along the host's base curve/height from " +
        "its start, in mm). add_mullions needs mullionTypeId and applies to every ungridded segment unless " +
        "gridLineIds narrows it.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var hostIdLong = input["hostElementId"]?.Value<long?>() ?? 0;
        if (hostIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostElementId is required");

        var host = doc.GetElement(ToolHelpers.ToElementId(hostIdLong));
        var grid = GetCurtainGrid(host);
        if (grid == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"hostElementId {hostIdLong} has no curtain grid (not a curtain wall/system/roof, or its type has no automatic grid)");

        var action = (input["action"]?.Value<string>() ?? "get_grid_info").ToLowerInvariant();
        try
        {
            return action switch
            {
                "get_grid_info" => GetGridInfo(grid),
                "add_grid_line" => AddGridLine(doc, grid, input),
                "add_mullions" => AddMullions(doc, grid, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unsupported action: {action}",
                    suggestion: "Use: get_grid_info | add_grid_line | add_mullions")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Curtain grids are read from the host, not looked up by id. A Wall has ONE grid
    /// (`CurtainGrid`); a CurtainSystem has one PER FACE (`CurtainGrids`, a
    /// CurtainGridSet that is null when the system carries none), so the singular
    /// property does not exist there. Only the first face is addressed here — a
    /// multi-face system needs a face selector, which the tool does not publish.
    /// </summary>
    private static CurtainGrid? GetCurtainGrid(Element? host)
    {
        return host switch
        {
            Wall wall => wall.CurtainGrid,
            CurtainSystem system => system.CurtainGrids?.Cast<CurtainGrid>().FirstOrDefault(),
            _ => null
        };
    }

    private static CortexResult<object> GetGridInfo(CurtainGrid grid)
    {
        var uLines = grid.GetUGridLineIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var vLines = grid.GetVGridLineIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var panelIds = grid.GetPanelIds().Select(ToolHelpers.GetElementIdValue).ToList();
        var mullionIds = grid.GetMullionIds().Select(ToolHelpers.GetElementIdValue).ToList();

        return CortexResult<object>.Ok(new
        {
            uGridLineIds = uLines,
            vGridLineIds = vLines,
            panelIds,
            mullionIds
        });
    }

    private static CortexResult<object> AddGridLine(Document doc, CurtainGrid grid, JObject input)
    {
        var direction = (input["direction"]?.Value<string>() ?? "").ToLowerInvariant();
        var offsetMm = input["offsetMm"]?.Value<double?>();
        if ((direction != "u" && direction != "v") || offsetMm == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "direction (u|v) and offsetMm are required");

        // The API takes a full XYZ, but only the component along the grid's own axis
        // matters — Revit resolves the line against the host's own geometry from that.
        var offsetFt = offsetMm.Value / MmPerFoot;
        var point = direction == "u"
            ? new XYZ(0, 0, offsetFt)
            : new XYZ(offsetFt, 0, 0);

        using var tx = new Transaction(doc, "RiveTT: Add Curtain Grid Line");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            grid.AddGridLine(direction == "u", point, false);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"AddGridLine failed: {ex.Message}",
                suggestion: "Revit's own AddGridLine cannot add a line exactly at the host's start or end edge.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return GetGridInfo(grid);
    }

    /// <summary>
    /// AddMullions lives on CurtainGridLine, not CurtainGrid, and works one segment
    /// curve at a time (verified signature: AddMullions(Curve, MullionType, bool
    /// oneSegmentOnly)) — there is no bulk "add to this whole grid" call.
    /// </summary>
    private static CortexResult<object> AddMullions(Document doc, CurtainGrid grid, JObject input)
    {
        var mullionTypeIdLong = input["mullionTypeId"]?.Value<long?>() ?? 0;
        if (mullionTypeIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "mullionTypeId is required",
                suggestion: "List OST_CurtainWallMullions types with list_system_types(category: \"OST_CurtainWallMullions\").");

        var mullionType = doc.GetElement(ToolHelpers.ToElementId(mullionTypeIdLong)) as MullionType;
        if (mullionType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{mullionTypeIdLong} is not a MullionType");

        var explicitIds = input["gridLineIds"]?.ToObject<List<long>>();
        var targetLineIds = explicitIds != null && explicitIds.Count > 0
            ? explicitIds.Select(ToolHelpers.ToElementId)
            : grid.GetUGridLineIds().Concat(grid.GetVGridLineIds());

        using var tx = new Transaction(doc, "RiveTT: Add Curtain Mullions");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var addedCount = 0;
        var warnings = new List<string>();
        foreach (var lineId in targetLineIds)
        {
            if (doc.GetElement(lineId) is not CurtainGridLine gridLine)
            {
                warnings.Add($"{ToolHelpers.GetElementIdValue(lineId)} is not a curtain grid line");
                continue;
            }

            foreach (Curve segment in gridLine.AllSegmentCurves)
            {
                try
                {
                    // oneSegmentOnly=true: only THIS curve, never every segment that
                    // happens to share the same shape elsewhere on the grid.
                    gridLine.AddMullions(segment, mullionType, true);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    // A segment that already has a mullion is a no-op for Revit, not a
                    // failure — but a genuine geometry rejection should still surface.
                    warnings.Add($"Segment on grid line {ToolHelpers.GetElementIdValue(lineId)}: {ex.Message}");
                }
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        var info = GetGridInfo(grid);
        return CortexResult<object>.Ok(new { addedSegmentCount = addedCount, warnings, gridInfo = info.Data });
    }
}
