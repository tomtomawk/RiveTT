using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Views;

/// <summary>
/// Places a view on a sheet at the specified position.
/// </summary>
[ToolSafety(false, false)]
public class PlaceViewportTool : ICortexTool
{
    public string Name => "place_viewport";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Places a view on a sheet. positionX/positionY are the viewport CENTRE in mm, measured in sheet coordinates; omit both to centre it on the sheet. The response reports the sheet size, the viewport's real outline and fitsOnSheet: an UNCROPPED view produces a viewport far larger than the sheet, and its content then lands outside the frame. Crop the view first — at 1:100 a 16 x 13.5 m crop is 160 x 135 mm on paper.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var posXToken = input["positionX"];
        var posYToken = input["positionY"];
        var posXMm = posXToken?.Value<double>() ?? 0;
        var posYMm = posYToken?.Value<double>() ?? 0;
        var centreOnSheet = posXToken == null && posYToken == null;
        var rotation = input["rotation"]?.Value<string>();
        var viewportTypeId = input["viewportTypeId"]?.Value<long?>() ?? 0;

        if (sheetId <= 0 || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId and viewId are required");

        try
        {
#if REVIT2024_OR_GREATER
            var sheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
            var view = doc.GetElement(new ElementId(viewId)) as View;
            var viewEid = new ElementId(viewId);
            var sheetEid = new ElementId(sheetId);
#else
            var sheet = doc.GetElement(new ElementId((int)sheetId)) as ViewSheet;
            var view = doc.GetElement(new ElementId((int)viewId)) as View;
            var viewEid = new ElementId((int)viewId);
            var sheetEid = new ElementId((int)sheetId);
#endif
            if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Sheet not found");
            if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View not found");

            if (!Viewport.CanAddViewToSheet(doc, sheetEid, viewEid))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "View cannot be added to this sheet (already placed or not placeable)");

            var sheetWidthFt = sheet.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0;
            var sheetHeightFt = sheet.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;

            // The frame reference is the title block INSTANCE's box, not [0, size]:
            // the sheet origin is NOT necessarily the frame corner. The French A1
            // title block has its origin 650 mm inside the frame and vertically
            // centred, so a position computed from the sheet size lands off the paper
            // — which is exactly how two correctly-placed viewports ended up outside
            // the border.
            var titleBlock = new FilteredElementCollector(doc, sheetEid)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();
            BoundingBoxXYZ? frame = null;
            try { frame = titleBlock?.get_BoundingBox(sheet); } catch { }

            var frameMinXMm = frame != null ? frame.Min.X * MmPerFoot : 0;
            var frameMinYMm = frame != null ? frame.Min.Y * MmPerFoot : 0;
            var frameMaxXMm = frame != null ? frame.Max.X * MmPerFoot : sheetWidthFt * MmPerFoot;
            var frameMaxYMm = frame != null ? frame.Max.Y * MmPerFoot : sheetHeightFt * MmPerFoot;

            if (centreOnSheet)
            {
                // No position given: the middle of the frame, wherever the frame is.
                posXMm = (frameMinXMm + frameMaxXMm) / 2;
                posYMm = (frameMinYMm + frameMaxYMm) / 2;
            }

            var position = new XYZ(posXMm / MmPerFoot, posYMm / MmPerFoot, 0);

            using var tx = new Transaction(doc, "RiveTT: Place Viewport");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var viewport = Viewport.Create(doc, sheetEid, viewEid, position);

            // Optional rotation
            if (!string.IsNullOrEmpty(rotation))
            {
                viewport.Rotation = rotation!.ToLowerInvariant() switch
                {
                    "clockwise"        => ViewportRotation.Clockwise,
                    "counterclockwise" => ViewportRotation.Counterclockwise,
                    _                  => ViewportRotation.None,
                };
            }

            // Optional viewport type (controls title/detail-number appearance)
            if (viewportTypeId > 0)
            {
                var vpType = doc.GetElement(ToolHelpers.ToElementId(viewportTypeId));
                if (vpType is ElementType && viewport.IsValidType(vpType.Id))
                    viewport.ChangeTypeId(vpType.Id);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            // What was actually placed, and whether it fits. A viewport whose outline
            // exceeds the sheet draws its content outside the frame, which is only
            // visible by opening the sheet — the caller must be told here.
            double outMinX = 0, outMinY = 0, outMaxX = 0, outMaxY = 0;
            var haveOutline = false;
            try
            {
                var outline = viewport.GetBoxOutline();
                outMinX = outline.MinimumPoint.X * MmPerFoot;
                outMinY = outline.MinimumPoint.Y * MmPerFoot;
                outMaxX = outline.MaximumPoint.X * MmPerFoot;
                outMaxY = outline.MaximumPoint.Y * MmPerFoot;
                haveOutline = true;
            }
            catch
            {
                // Outline is unavailable on some viewport states; the rest still stands.
            }

            var sheetWidthMm = sheetWidthFt * MmPerFoot;
            var sheetHeightMm = sheetHeightFt * MmPerFoot;
            var fits = !haveOutline ||
                       (outMinX >= frameMinXMm - 1 && outMinY >= frameMinYMm - 1 &&
                        outMaxX <= frameMaxXMm + 1 && outMaxY <= frameMaxYMm + 1);

            var warnings = new System.Collections.Generic.List<string>();
            if (haveOutline && !fits)
            {
                warnings.Add(
                    $"The viewport spans {outMaxX - outMinX:F0} x {outMaxY - outMinY:F0} mm at " +
                    $"[{outMinX:F0}..{outMaxX:F0}] x [{outMinY:F0}..{outMaxY:F0}] mm, outside the frame " +
                    $"[{frameMinXMm:F0}..{frameMaxXMm:F0}] x [{frameMinYMm:F0}..{frameMaxYMm:F0}] mm: part " +
                    "of the drawing falls off the sheet. Two independent causes — an UNCROPPED view makes " +
                    "the viewport metres wide (crop it: paper size = crop size / view scale), and the sheet " +
                    "origin is not the frame corner, so compute positions from frameOutlineMm below, not " +
                    "from the sheet size.");
            }

            return CortexResult<object>.Ok(new
            {
                viewportId = ToolHelpers.GetElementIdValue(viewport.Id),
                sheetNumber = sheet.SheetNumber,
                viewName = view.Name,
                rotation = viewport.Rotation.ToString(),
                centredOnSheet = centreOnSheet,
                centreMm = new { x = Math.Round(posXMm, 1), y = Math.Round(posYMm, 1) },
                sheetSizeMm = new { width = Math.Round(sheetWidthMm, 1), height = Math.Round(sheetHeightMm, 1) },
                // Where the printable frame actually is, in sheet coordinates. Use
                // this to compute positions: it is NOT [0,0]..[width,height].
                frameOutlineMm = new
                {
                    minX = Math.Round(frameMinXMm, 1), minY = Math.Round(frameMinYMm, 1),
                    maxX = Math.Round(frameMaxXMm, 1), maxY = Math.Round(frameMaxYMm, 1),
                    fromTitleBlock = frame != null
                },
                viewportOutlineMm = haveOutline
                    ? new
                    {
                        minX = Math.Round(outMinX, 1), minY = Math.Round(outMinY, 1),
                        maxX = Math.Round(outMaxX, 1), maxY = Math.Round(outMaxY, 1),
                        widthMm = Math.Round(outMaxX - outMinX, 1),
                        heightMm = Math.Round(outMaxY - outMinY, 1)
                    }
                    : null,
                viewScale = view.Scale,
                cropActive = view.CropBoxActive,
                fitsOnSheet = fits,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
