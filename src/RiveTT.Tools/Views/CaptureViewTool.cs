using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Views;

/// <summary>
/// Renders a view to an image and returns the image itself, so the agent can SEE what it
/// built. The MCP server turns imageBase64 into an MCP image content block.
///
/// Field report of 2026-09-24 (03): batch_export wrote the PNG to the local disk and answered
/// "success" — the agent, running elsewhere, could never read it, and send_code_to_revit
/// forbids file I/O. Floating furniture, a duplicated lift, missing balconies and closed
/// entrances were all found by the user on screenshots, never by the agent. The plugin may
/// read the file it has just written; that is the missing link.
///
/// Nothing is left in the model: without options the source view is exported as it is, with
/// no transaction at all. Options (crop, detail level, display style, highlight, isolation)
/// are applied to a temporary duplicate inside a TransactionGroup that is always rolled back.
/// </summary>
[ToolSafety(true, false)]
public sealed class CaptureViewTool : IRiveTTTool, ICommandTimeoutTool
{
    public string Name => "capture_view";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public int CommandTimeoutSeconds => 180;
    public string Description =>
        "Renders a view (plan, section, elevation, 3D, sheet) to a PNG or JPEG and returns the image with the " +
        "pixel-to-model mapping; optional crop, detail level, display style, highlighted ids and isolated categories " +
        "are applied to a temporary copy that is rolled back.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var view = ResolveView(doc, input, out var viewError);
        if (view == null) return viewError!;

        var pixelSize = Math.Max(256, Math.Min(4000, input["pixelSize"]?.Value<int>() ?? 1600));
        var format = (input["format"]?.Value<string>() ?? "png").Trim().ToLowerInvariant();
        if (format is not ("png" or "jpg" or "jpeg"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"format '{format}' is not supported: png | jpg.");
        var returnMode = (input["returnMode"]?.Value<string>() ?? "inline").Trim().ToLowerInvariant();
        if (returnMode is not ("inline" or "file" or "both"))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"returnMode '{returnMode}' is not supported: inline | file | both.");

        double[]? bboxMm = null;
        if (input["bboxMm"] is JArray bboxArray)
        {
            if (bboxArray.Count != 4)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "bboxMm must be [xmin, ymin, xmax, ymax] in mm.");
            bboxMm = bboxArray.Select(v => v.Value<double>()).ToArray();
            if (bboxMm[2] <= bboxMm[0] || bboxMm[3] <= bboxMm[1])
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "bboxMm must have xmax > xmin and ymax > ymin.");
            if (view is not ViewPlan)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"bboxMm applies to plan views; '{view.Name}' is a {view.ViewType}.",
                    suggestion: "For a 3D view use a section box (create_section_box_from_selection) on a copy, or omit bboxMm.");
        }

        ViewDetailLevel? detailLevel = null;
        if (input["detailLevel"]?.Value<string>() is { } detailText)
        {
            if (!Enum.TryParse<ViewDetailLevel>(detailText, true, out var parsed) || parsed == ViewDetailLevel.Undefined)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"detailLevel '{detailText}' is not supported: Coarse | Medium | Fine.");
            detailLevel = parsed;
        }

        DisplayStyle? displayStyle = null;
        if (input["displayStyle"]?.Value<string>() is { } styleText)
        {
            if (!TryParseDisplayStyle(styleText, out var parsed))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"displayStyle '{styleText}' is not supported: HLR | Shading | ShadingWithEdges | Realistic | Wireframe.");
            displayStyle = parsed;
        }

        var highlightIds = input["highlightIds"]?.ToObject<List<long>>() ?? new List<long>();
        var isolateCategories = input["isolateCategories"]?.ToObject<List<string>>() ?? new List<string>();
        var unresolvedCategories = new List<string>();
        var isolateIds = new List<ElementId>();
        foreach (var name in isolateCategories)
        {
            var id = CategoryResolver.ResolveToId(doc, name);
            if (id == null) unresolvedCategories.Add(name);
            else isolateIds.Add(id);
        }
        if (unresolvedCategories.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Unknown categories: {string.Join(", ", unresolvedCategories)}.",
                suggestion: "Use OST_* codes, e.g. OST_Furniture, OST_PlumbingFixtures, OST_Walls.");

        var needsTemporaryView = bboxMm != null || detailLevel != null || displayStyle != null
                                 || highlightIds.Count > 0 || isolateIds.Count > 0;
        if (!view.CanBePrinted)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{view.Name}' ({view.ViewType}) cannot be rendered to an image.",
                suggestion: "Pick a plan, section, elevation, 3D view, drafting view, legend or sheet.");
        if (needsTemporaryView && !view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"'{view.Name}' cannot be duplicated, so crop/overrides cannot be applied to a temporary copy.",
                suggestion: "Capture it without options, or capture another view of the same area.");

        var directory = Path.Combine(Path.GetTempPath(), "RiveTT", "captures", Guid.NewGuid().ToString("N"));
        TransactionGroup? group = null;
        var notApplied = new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            var exported = view;
            if (needsTemporaryView)
            {
                group = new TransactionGroup(doc, "RiveTT: capture_view (rolled back)");
                group.Start();
                using var tx = new Transaction(doc, "RiveTT: temporary capture view");
                tx.Start();
                TransactionFailureHandling.SuppressWarnings(tx);
                var copy = (View)doc.GetElement(view.Duplicate(ViewDuplicateOption.WithDetailing));
                // A template would lock the very properties being set on the copy.
                if (copy.ViewTemplateId != ElementId.InvalidElementId)
                    copy.ViewTemplateId = ElementId.InvalidElementId;
                ApplyOptions(doc, copy, bboxMm, detailLevel, displayStyle, highlightIds, isolateIds, notApplied);
                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        "Revit refused the temporary capture view.",
                        suggestion: "Capture without options, or with fewer of them.");
                exported = copy;
            }

            var (fileType, extension) = format == "png" ? (ImageFileType.PNG, "png") : (ImageFileType.JPEGMedium, "jpg");
            var mapping = DescribeMapping(exported);
            var fitDirection = mapping.CropWidthMm >= mapping.CropHeightMm || mapping.CropWidthMm <= 0
                ? FitDirectionType.Horizontal
                : FitDirectionType.Vertical;
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = Path.Combine(directory, "capture"),
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = pixelSize,
                FitDirection = fitDirection,
                ImageResolution = ImageResolution.DPI_150,
                HLRandWFViewsFileType = fileType,
                ShadowViewsFileType = fileType
            };
            options.SetViewsAndSheets(new List<ElementId> { exported.Id });
            doc.ExportImage(options);

            // Revit appends the view type and name to the file name: find what it wrote.
            var file = Directory.GetFiles(directory, "*." + extension).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                       ?? Directory.GetFiles(directory).FirstOrDefault();
            if (file == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                    $"Revit reported the export of '{view.Name}' but wrote no image.",
                    suggestion: "Retry with format png and a smaller pixelSize.");

            var bytes = File.ReadAllBytes(file);
            PngInfo.TryReadSize(bytes, out var width, out var height, out var mimeType);
            var pixelMapping = CaptureMapping.Compute(mapping, width, height);

            string? keptPath = null;
            if (returnMode != "inline")
            {
                var keepDir = Path.Combine(Path.GetTempPath(), "RiveTT", "captures");
                keptPath = Path.Combine(keepDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Sanitize(view.Name)}.{extension}");
                File.Copy(file, keptPath, overwrite: true);
            }

            return RiveTTResult<object>.Ok(new
            {
                message = $"Captured '{view.Name}' ({width} x {height} px).",
                imageBase64 = returnMode == "file" ? null : Convert.ToBase64String(bytes),
                mimeType,
                imageBytes = bytes.Length,
                filePath = keptPath,
                view = new
                {
                    id = ToolHelpers.GetElementIdValue(view.Id),
                    name = view.Name,
                    viewType = view.ViewType.ToString(),
                    scale = SafeInt(() => view.Scale),
                    levelName = (view as ViewPlan)?.GenLevel?.Name
                },
                pixelSize,
                imageSizePx = new[] { width, height },
                mapping = pixelMapping,
                temporaryView = needsTemporaryView,
                optionsNotApplied = notApplied,
                modelChanged = false
            });
        }
        catch (Exception exception)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"capture_view could not render '{view.Name}': {exception.Message}",
                suggestion: needsTemporaryView
                    ? "Retry without crop/overrides: if the plain capture works, the options are what Revit refused."
                    : "Check that the view is not empty and that Revit's graphics are available, then retry.");
        }
        finally
        {
            try
            {
                if (group != null && group.GetStatus() == TransactionStatus.Started)
                    group.RollBack();
            }
            catch
            {
                // Rolling back the capture group cannot be made to fail loudly from here; the
                // copy it created is the only thing that could remain, and it is named RiveTT.
            }
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static View? ResolveView(Document doc, JObject input, out RiveTTResult<object>? error)
    {
        var viewName = input["viewName"]?.Value<string>();
        if (input["viewId"] == null && !string.IsNullOrWhiteSpace(viewName))
        {
            error = null;
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate).ToList();
            var match = views.FirstOrDefault(v => v.Name == viewName)
                ?? views.FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, $"No view named '{viewName}'.",
                suggestion: "Close names: " + string.Join(", ",
                    NameMatching.Suggest(viewName!, views.Select(v => v.Name), 8).Select(n => $"'{n}'")));
            return null;
        }
        return ToolHelpers.ResolveTargetView(doc, input, out error);
    }

    private static void ApplyOptions(Document doc, View copy, double[]? bboxMm, ViewDetailLevel? detailLevel,
        DisplayStyle? displayStyle, List<long> highlightIds, List<ElementId> isolateIds, List<string> notApplied)
    {
        if (bboxMm != null)
        {
            try
            {
                // A non-rectangular crop ignores CropBox assignments ("will have no effect"):
                // back to a rectangle on the copy first.
                var shape = copy.GetCropRegionShapeManager();
                if (shape != null && shape.ShapeSet) shape.RemoveCropRegionShape();

                var crop = copy.CropBox;
                var inverse = crop.Transform.Inverse;
                // The crop box lives in the view's coordinates: express the requested model
                // rectangle there, keeping the current near/far range.
                var a = inverse.OfPoint(new XYZ(bboxMm[0] / MmPerFoot, bboxMm[1] / MmPerFoot, 0));
                var b = inverse.OfPoint(new XYZ(bboxMm[2] / MmPerFoot, bboxMm[3] / MmPerFoot, 0));
                crop.Min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), crop.Min.Z);
                crop.Max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), crop.Max.Z);
                copy.CropBox = crop;
                copy.CropBoxActive = true;
                copy.CropBoxVisible = false;
                // Annotations outside the crop would widen the image and break the mapping. The
                // annotation crop alone is not enough: its default offsets (about an inch of paper,
                // 1.2 m at 1:50) still let a section mark widen the image unevenly (2026-09-27).
                copy.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(1);
                if (shape != null) MinimiseAnnotationCropOffsets(shape);

                // Read back what Revit kept: an assignment it ignored must not pass for applied.
                var applied = copy.CropBox;
                if (Math.Abs(applied.Min.X - crop.Min.X) > 1e-3 || Math.Abs(applied.Max.X - crop.Max.X) > 1e-3 ||
                    Math.Abs(applied.Min.Y - crop.Min.Y) > 1e-3 || Math.Abs(applied.Max.Y - crop.Max.Y) > 1e-3)
                    notApplied.Add("bboxMm: Revit kept another crop region than the one requested; the image is framed " +
                                   "by the view's own crop");
            }
            catch (Exception exception) { notApplied.Add($"bboxMm: {exception.Message}"); }
        }

        if (detailLevel != null)
        {
            try { copy.DetailLevel = detailLevel.Value; }
            catch (Exception exception) { notApplied.Add($"detailLevel: {exception.Message}"); }
        }

        if (displayStyle != null)
        {
            try { copy.DisplayStyle = displayStyle.Value; }
            catch (Exception exception) { notApplied.Add($"displayStyle: {exception.Message}"); }
        }

        if (isolateIds.Count > 0)
        {
            try
            {
                copy.IsolateCategoriesTemporary(isolateIds);
                // Temporary isolation is a UI state; the export honours the permanent one.
                copy.ConvertTemporaryHideIsolateToPermanent();
            }
            catch (Exception exception) { notApplied.Add($"isolateCategories: {exception.Message}"); }
        }

        if (highlightIds.Count > 0)
        {
            var red = new Color(230, 0, 0);
            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(p => SafeBool(() => p.GetFillPattern().IsSolidFill && p.GetFillPattern().Target == FillPatternTarget.Drafting));
            var overrides = new OverrideGraphicSettings()
                .SetProjectionLineColor(red).SetCutLineColor(red)
                .SetProjectionLineWeight(8).SetCutLineWeight(8);
            if (solid != null)
                overrides = overrides.SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(red)
                    .SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(red);
            var missing = new List<long>();
            foreach (var id in highlightIds)
            {
                var elementId = ToolHelpers.ToElementId(id);
                if (doc.GetElement(elementId) == null) { missing.Add(id); continue; }
                try { copy.SetElementOverrides(elementId, overrides); }
                catch { missing.Add(id); }
            }
            if (missing.Count > 0) notApplied.Add($"highlightIds not found or not overridable: {string.Join(", ", missing.Take(20))}");
        }
    }

    /// <summary>
    /// Brings the annotation crop as close to the model crop as Revit allows. Each side is set
    /// on its own: a value Revit refuses leaves that side as it was, never the whole call.
    /// </summary>
    private static void MinimiseAnnotationCropOffsets(ViewCropRegionShapeManager shape)
    {
        // A millimetre rather than 0, which Revit may treat as out of range; either way it is
        // below any visible difference.
        const double offsetFt = 1.0 / MmPerFoot;
        try { shape.LeftAnnotationCropOffset = offsetFt; } catch { }
        try { shape.RightAnnotationCropOffset = offsetFt; } catch { }
        try { shape.TopAnnotationCropOffset = offsetFt; } catch { }
        try { shape.BottomAnnotationCropOffset = offsetFt; } catch { }
    }

    /// <summary>The model rectangle the exported view covers, when it is knowable.</summary>
    private static CaptureMapping.ViewFrame DescribeMapping(View view)
    {
        try
        {
            if (!view.CropBoxActive || view is View3D || view is ViewSheet)
                return new CaptureMapping.ViewFrame(false, 0, 0, 0, 0, view.ViewType.ToString(), UpIsNorth: false);
            var crop = view.CropBox;
            var t = crop.Transform;
            var corners = new[]
            {
                t.OfPoint(new XYZ(crop.Min.X, crop.Min.Y, crop.Min.Z)),
                t.OfPoint(new XYZ(crop.Max.X, crop.Min.Y, crop.Min.Z)),
                t.OfPoint(new XYZ(crop.Min.X, crop.Max.Y, crop.Min.Z)),
                t.OfPoint(new XYZ(crop.Max.X, crop.Max.Y, crop.Min.Z)),
            };
            var up = view.UpDirection;
            var upIsNorth = view is ViewPlan && Math.Abs(up.X) < 1e-6 && up.Y > 0.999999;
            var widthMm = (crop.Max.X - crop.Min.X) * MmPerFoot;
            var heightMm = (crop.Max.Y - crop.Min.Y) * MmPerFoot;
            return new CaptureMapping.ViewFrame(true,
                corners.Min(c => c.X) * MmPerFoot, corners.Max(c => c.Y) * MmPerFoot,
                widthMm, heightMm, view.ViewType.ToString(), upIsNorth);
        }
        catch
        {
            return new CaptureMapping.ViewFrame(false, 0, 0, 0, 0, view.ViewType.ToString(), UpIsNorth: false);
        }
    }

    private static bool TryParseDisplayStyle(string text, out DisplayStyle style)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "hlr": case "hiddenline": style = DisplayStyle.HLR; return true;
            case "shading": style = DisplayStyle.Shading; return true;
            case "shadingwithedges": style = DisplayStyle.ShadingWithEdges; return true;
            case "realistic": style = DisplayStyle.Realistic; return true;
            case "wireframe": style = DisplayStyle.Wireframe; return true;
            default: style = DisplayStyle.Undefined; return false;
        }
    }

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars);
        return safe.Length > 60 ? safe.Substring(0, 60) : safe;
    }

    private static int? SafeInt(Func<int> read)
    {
        try { return read(); } catch { return null; }
    }

    private static bool SafeBool(Func<bool> read)
    {
        try { return read(); } catch { return false; }
    }
}

/// <summary>
/// Pixel-to-model mapping of a captured plan, in millimetres, with an honest verdict on
/// whether it can be trusted. Revit-free so it can be tested.
/// </summary>
public static class CaptureMapping
{
    /// <param name="Known">A crop region is active: the image frames a known model rectangle.</param>
    /// <param name="LeftMm">Model X of the left edge of the crop.</param>
    /// <param name="TopMm">Model Y of the top edge of the crop.</param>
    public sealed record ViewFrame(bool Known, double LeftMm, double TopMm, double CropWidthMm, double CropHeightMm,
        string ViewType, bool UpIsNorth);

    /// <summary>
    /// The mapping, or a reason why there is none. When the image's aspect differs from the
    /// crop's by more than 2 %, annotations outside the crop (or a margin) widened the image:
    /// the scale is then reported as approximate instead of being asserted. Even when the
    /// aspects match, the mapping is reported unverified (see aspectMatchesCrop).
    /// </summary>
    public static object Compute(ViewFrame frame, int widthPx, int heightPx)
    {
        if (!frame.Known || widthPx <= 0 || heightPx <= 0 || frame.CropWidthMm <= 0 || frame.CropHeightMm <= 0)
            return new
            {
                available = false,
                reason = frame.ViewType is "ThreeD" or "DrawingSheet"
                    ? "3D views and sheets have no plan mapping; read positions from a plan capture."
                    : "The view has no active crop region: pass bboxMm to frame a known rectangle."
            };

        var mmPerPixelX = frame.CropWidthMm / widthPx;
        var mmPerPixelY = frame.CropHeightMm / heightPx;
        var aspectGap = Math.Abs(mmPerPixelX - mmPerPixelY) / Math.Max(mmPerPixelX, mmPerPixelY);
        var aspectMatches = aspectGap <= 0.02;

        // Fit-to-page scales the crop to fill the image along one axis and centres it along the
        // other: the scale is the LARGER of the two ratios, and the rest is an equal margin on
        // both sides. Averaging the two ratios, as 0.6.0 did, was wrong by 21 to 27 % whenever
        // the aspects differed (measured on the plan of 2026-09-27).
        var scale = Math.Max(mmPerPixelX, mmPerPixelY);
        var marginX = (widthPx - frame.CropWidthMm / scale) / 2;
        var marginY = (heightPx - frame.CropHeightMm / scale) / 2;
        return new
        {
            available = true,
            // Matching aspects do not prove the frame: a margin added on every side (annotation
            // crop offsets) keeps the aspect of a square crop and shifts both scale and origin.
            // Nothing measured here can rule that out, so the mapping is never called exact.
            aspectMatchesCrop = aspectMatches,
            verified = false,
            calibrate = "To trust a position read on the image, capture once with highlightIds on an element whose " +
                        "coordinates are known and compare.",
            mmPerPixel = Math.Round(scale, 3),
            // The top-left corner of the IMAGE, margins included — what px, py count from.
            originTopLeftMm = new[]
            {
                Math.Round(frame.LeftMm - marginX * scale, 1),
                Math.Round(frame.TopMm + marginY * scale, 1)
            },
            // Where the crop itself lies in the image: [left, top, right, bottom] in pixels.
            cropPx = new[]
            {
                Math.Round(marginX, 1), Math.Round(marginY, 1),
                Math.Round(widthPx - marginX, 1), Math.Round(heightPx - marginY, 1)
            },
            axes = frame.UpIsNorth
                ? "pixel x -> model +X (east), pixel y -> model -Y: model = (left + px * mmPerPixel, top - py * mmPerPixel)"
                : "the view is rotated in plan: pixel axes follow the view's right/up directions, not model X/Y",
            cropMm = new[] { Math.Round(frame.CropWidthMm, 1), Math.Round(frame.CropHeightMm, 1) },
            note = aspectMatches
                ? "Image aspect matches the crop region; the mapping assumes the image is exactly the crop."
                : $"Image aspect differs from the crop by {aspectGap:P0}: the crop fills the image along one axis and is " +
                  "assumed centred along the other (cropPx) — approximate until calibrated with highlightIds."
        };
    }
}
