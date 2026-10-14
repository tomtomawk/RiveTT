using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Exports multiple views/sheets to DWG, DXF, DGN, or image formats.
/// PDF export requires Revit 2023+ PDF export API.
/// </summary>
[ToolSafety(false, false)]
public class BatchExportTool : IRiveTTTool
{
    public string Name => "batch_export";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports multiple views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats. PDF options: rasterQuality, vectorQualityDpi, colorMode, paperFormat, orientation, zoom/zoomPercent, paperPlacement, combine + combinedFileName, fileName template ({number}, {name}), overwriteExisting, hide/mask switches. Each PDF result carries path, exists, sizeBytes and replacedExistingFile.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var format = input["format"]?.Value<string>() ?? "DWG";
        var sheetIds = input["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var outputDir = input["outputDirectory"]?.Value<string>();

        if (sheetIds.Count == 0 && viewIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sheetIds or viewIds required");

        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RevitExport");
        }

        // H25-wave: this tool creates directories and writes export files — restrict the
        // target to user-owned directories; reject traversal/UNC/system paths.
        if (!Utilities.PathSafety.TryResolveSafe(outputDir, out var safeOutputDir, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        outputDir = safeOutputDir;

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        try
        {
            // Collect all view/sheet IDs
            var allIds = new List<ElementId>();
            foreach (var sid in sheetIds)
            {
                allIds.Add(new ElementId(sid));
            }
            foreach (var vid in viewIds)
            {
                allIds.Add(new ElementId(vid));
            }

            var results = new List<object>();

            switch (format.ToUpperInvariant())
            {
                case "DWG":
                {
                    var options = new DWGExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dwg", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "DXF":
                {
                    var options = new DXFExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dxf", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "DGN":
                {
                    var options = new DGNExportOptions();
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var viewSet = new List<ElementId> { id };
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            doc.Export(outputDir, name, viewSet, options);
                            results.Add(new { name = view.Name, file = $"{name}.dgn", success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "IMAGE":
                {
                    foreach (var id in allIds)
                    {
                        var view = doc.GetElement(id) as View;
                        if (view == null) continue;
                        var name = SanitizeFileName(view.Name);
                        try
                        {
                            var imgOptions = new ImageExportOptions
                            {
                                FilePath = Path.Combine(outputDir, $"{name}.png"),
                                ExportRange = ExportRange.SetOfViews,
                                ZoomType = ZoomFitType.FitToPage,
                                ImageResolution = ImageResolution.DPI_300,
                                HLRandWFViewsFileType = ImageFileType.PNG
                            };
                            imgOptions.SetViewsAndSheets(new List<ElementId> { id });
                            doc.ExportImage(imgOptions);
                            // Revit appends the view type and name to FilePath: "{name}.png"
                            // named a file that did not exist. Ask Revit what it produced.
                            results.Add(new
                            {
                                name = view.Name,
                                file = ProducedImageName(doc, id, outputDir, name),
                                success = true,
                                note = "The image is on this machine's disk; capture_view returns it to the agent."
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { name = view.Name, success = false, reason = ex.Message });
                        }
                    }
                    break;
                }
                case "PDF":
                {
                    var pdf = ExportPdf(doc, allIds, outputDir, input);
                    if (pdf.Failure != null) return pdf.Failure;
                    results.AddRange(pdf.Results);
                    break;
                }
                default:
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                        $"Unsupported format: {format}", suggestion: "Use: DWG, DXF, DGN, PDF, IMAGE");
            }

            return RiveTTResult<object>.Ok(new
            {
                format,
                outputDirectory = outputDir,
                exportedCount = results.Count(r => ((dynamic)r).success),
                results
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"batch_export could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private sealed record PdfOutcome(List<object> Results, RiveTTResult<object>? Failure);

    /// <summary>
    /// PDF export with the options Revit's own dialog has. Until 0.6.2 nothing was exposed: the name
    /// was imposed ("Feuille-&lt;name&gt;.pdf"), quality and paper could not be chosen, and nothing said
    /// whether an existing file had been overwritten (field report, notices PC). Options (all optional):
    /// rasterQuality low|medium|high|presentation; vectorQualityDpi; colorMode color|grayscale|blackline;
    /// paperFormat (A3, ISO_A3, ANSI_B, Default...); orientation auto|landscape|portrait; zoom "fit" or
    /// zoomPercent; paperPlacement center|lowerleft|margins; combine + combinedFileName; fileName (template with
    /// {number}, {name}) for separate files; overwriteExisting; and the hide/mask switches.
    /// </summary>
    private static PdfOutcome ExportPdf(Document doc, List<ElementId> ids, string outputDir, JObject input)
    {
        var results = new List<object>();
        var options = new PDFExportOptions();
        var applied = new Dictionary<string, object>();

        RiveTTResult<object>? Bad(string field, string given, IEnumerable<string> allowed) =>
            RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"{field} '{given}' is not recognized.",
                suggestion: $"Allowed: {string.Join(", ", allowed)}");

        var raster = input["rasterQuality"]?.Value<string>();
        if (raster != null)
        {
            if (!Enum.TryParse<RasterQualityType>(raster, true, out var rq))
                return new PdfOutcome(results, Bad("rasterQuality", raster, Enum.GetNames<RasterQualityType>()));
            options.RasterQuality = rq; applied["rasterQuality"] = rq.ToString();
        }

        var dpi = input["vectorQualityDpi"]?.Value<int?>();
        if (dpi != null)
        {
            if (!Enum.TryParse<PDFExportQualityType>("DPI" + dpi, true, out var vq))
                return new PdfOutcome(results, Bad("vectorQualityDpi", dpi.ToString()!, Enum.GetNames<PDFExportQualityType>()));
            options.ExportQuality = vq; applied["vectorQuality"] = vq.ToString();
        }

        var color = input["colorMode"]?.Value<string>();
        if (color != null)
        {
            var mapped = color.ToLowerInvariant() switch
            {
                "color" or "colour" or "couleur" => (ColorDepthType?)ColorDepthType.Color,
                "grayscale" or "greyscale" or "gray" or "gris" => ColorDepthType.GrayScale,
                "blackline" or "blackandwhite" or "bw" or "noirblanc" => ColorDepthType.BlackLine,
                _ => null
            };
            if (mapped == null)
                return new PdfOutcome(results, Bad("colorMode", color, new[] { "color", "grayscale", "blackline" }));
            options.ColorDepth = mapped.Value; applied["colorMode"] = mapped.Value.ToString();
        }

        var paper = input["paperFormat"]?.Value<string>();
        if (paper != null)
        {
            var key = paper.Trim().Replace(" ", "_");
            if (!Enum.TryParse<ExportPaperFormat>(key, true, out var pf)
                && !Enum.TryParse(key.Replace("A", "ISO_A", StringComparison.OrdinalIgnoreCase), true, out pf))
                return new PdfOutcome(results, Bad("paperFormat", paper, Enum.GetNames<ExportPaperFormat>()));
            options.PaperFormat = pf; applied["paperFormat"] = pf.ToString();
        }

        var orientation = input["orientation"]?.Value<string>();
        if (orientation != null)
        {
            if (!Enum.TryParse<PageOrientationType>(orientation, true, out var po))
                return new PdfOutcome(results, Bad("orientation", orientation, Enum.GetNames<PageOrientationType>()));
            options.PaperOrientation = po; applied["orientation"] = po.ToString();
        }

        var placement = input["paperPlacement"]?.Value<string>();
        if (placement != null)
        {
            if (!Enum.TryParse<PaperPlacementType>(placement, true, out var pp))
                return new PdfOutcome(results, Bad("paperPlacement", placement, Enum.GetNames<PaperPlacementType>()));
            options.PaperPlacement = pp; applied["paperPlacement"] = pp.ToString();
        }

        var zoom = input["zoom"]?.Value<string>();
        var zoomPercent = input["zoomPercent"]?.Value<int?>();
        if (zoomPercent != null || (zoom != null && !zoom.Equals("fit", StringComparison.OrdinalIgnoreCase)))
        {
            var percent = zoomPercent ?? (int.TryParse(zoom?.TrimEnd('%'), out var parsed) ? parsed : 0);
            if (percent < 10 || percent > 400)
                return new PdfOutcome(results, Bad("zoomPercent", percent.ToString(), new[] { "10..400, or zoom: \"fit\"" }));
            options.ZoomType = ZoomType.Zoom; options.ZoomPercentage = percent; applied["zoomPercent"] = percent;
        }
        else if (zoom != null)
        {
            options.ZoomType = ZoomType.FitToPage; applied["zoom"] = "fit";
        }

        void Flag(string key, JToken? token, Action<bool> set)
        {
            var value = token?.Value<bool?>();
            if (value == null) return;
            set(value.Value);
            applied[key] = value.Value;
        }
        Flag("hideScopeBoxes", input["hideScopeBoxes"], v => options.HideScopeBoxes = v);
        Flag("hideReferencePlanes", input["hideReferencePlanes"], v => options.HideReferencePlane = v);
        Flag("hideCropBoundaries", input["hideCropBoundaries"], v => options.HideCropBoundaries = v);
        Flag("hideUnreferencedViewTags", input["hideUnreferencedViewTags"], v => options.HideUnreferencedViewTags = v);
        Flag("maskCoincidentLines", input["maskCoincidentLines"], v => options.MaskCoincidentLines = v);
        Flag("viewLinksInBlue", input["viewLinksInBlue"], v => options.ViewLinksInBlue = v);
        Flag("replaceHalftoneWithThinLines", input["replaceHalftoneWithThinLines"], v => options.ReplaceHalftoneWithThinLines = v);
        Flag("alwaysUseRaster", input["alwaysUseRaster"], v => options.AlwaysUseRaster = v);

        var overwrite = input["overwriteExisting"]?.Value<bool>() ?? true;
        var combine = input["combine"]?.Value<bool>() ?? false;
        var template = input["fileName"]?.Value<string>();
        var existingBefore = new HashSet<string>(
            Directory.GetFiles(outputDir, "*.pdf").Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);

        if (combine)
        {
            var combinedName = SanitizeFileName(input["combinedFileName"]?.Value<string>() ?? "Export");
            if (!overwrite && existingBefore.Contains(combinedName + ".pdf"))
            {
                results.Add(new { name = combinedName, success = false, skipped = true,
                    reason = $"{combinedName}.pdf already exists and overwriteExisting is false." });
                return new PdfOutcome(results, null);
            }
            options.Combine = true;
            options.FileName = combinedName;
            applied["combine"] = true;
            try
            {
                doc.Export(outputDir, ids, options);
                results.Add(DescribeFile(outputDir, combinedName + ".pdf", existingBefore, ids.Count, applied));
            }
            catch (Exception ex)
            {
                results.Add(new { name = combinedName, success = false, reason = ex.Message });
            }
            return new PdfOutcome(results, null);
        }

        options.Combine = false;
        foreach (var id in ids)
        {
            var view = doc.GetElement(id) as View;
            if (view == null) continue;
            var sheetNumber = (view as ViewSheet)?.SheetNumber ?? "";
            var wanted = SanitizeFileName(string.IsNullOrWhiteSpace(template)
                ? view.Name
                : template!.Replace("{number}", sheetNumber).Replace("{name}", view.Name));

            if (!overwrite && template != null && existingBefore.Contains(wanted + ".pdf"))
            {
                results.Add(new { name = view.Name, success = false, skipped = true,
                    reason = $"{wanted}.pdf already exists and overwriteExisting is false." });
                continue;
            }

            try
            {
                options.FileName = wanted;
                doc.Export(outputDir, new List<ElementId> { id }, options);
                // Revit decorates a separate file's name with the sheet-set prefix ("Feuille-<name>.pdf" in
                // French). Ask the folder what it wrote, then give it the requested name when one was asked.
                var written = ResolveWrittenFile(outputDir, wanted, ".pdf");
                if (!string.IsNullOrWhiteSpace(template) && !written.Equals(wanted + ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var target = Path.Combine(outputDir, wanted + ".pdf");
                    File.Move(Path.Combine(outputDir, written), target, overwrite: true);
                    written = wanted + ".pdf";
                }
                results.Add(DescribeFile(outputDir, written, existingBefore, 1, applied, view.Name));
            }
            catch (Exception ex)
            {
                results.Add(new { name = view.Name, success = false, reason = ex.Message });
            }
        }
        return new PdfOutcome(results, null);
    }

    /// <summary>The produced file, checked on disk: it exists, how big it is, and whether it replaced one.</summary>
    private static object DescribeFile(string outputDir, string file, HashSet<string> existingBefore, int pages,
        Dictionary<string, object> applied, string? name = null)
    {
        var path = Path.Combine(outputDir, file);
        var info = new FileInfo(path);
        return new
        {
            name = name ?? Path.GetFileNameWithoutExtension(file),
            file,
            path,
            success = info.Exists,
            exists = info.Exists,
            sizeBytes = info.Exists ? info.Length : 0,
            sheetsInFile = pages,
            // A file of that name was already there before this export: Revit wrote over it.
            replacedExistingFile = existingBefore.Contains(file),
            options = applied
        };
    }

    /// <summary>
    /// The file Revit actually created for this view, matched by extension and
    /// recency. Reporting a name nobody can find is worse than reporting none.
    /// </summary>
    private static string ResolveWrittenFile(string outputDir, string expectedName, string extension)
    {
        try
        {
            var candidates = System.IO.Directory
                .GetFiles(outputDir, "*" + extension)
                .Select(path => new System.IO.FileInfo(path))
                .Where(info => info.LastWriteTimeUtc > System.DateTime.UtcNow.AddMinutes(-2))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            var exact = candidates.FirstOrDefault(info =>
                info.Name.Equals(expectedName + extension, System.StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.Name;

            var suffixed = candidates.FirstOrDefault(info =>
                info.Name.EndsWith(expectedName + extension, System.StringComparison.OrdinalIgnoreCase));
            if (suffixed != null) return suffixed.Name;

            // Image export: "<name> - <view type> - <view name>.png".
            var prefixed = candidates.FirstOrDefault(info =>
                info.Name.StartsWith(expectedName + " - ", System.StringComparison.OrdinalIgnoreCase));
            if (prefixed != null) return prefixed.Name;

            return candidates.Count > 0 ? candidates[0].Name : expectedName + extension;
        }
        catch
        {
            return expectedName + extension;
        }
    }

    /// <summary>
    /// The file an image export wrote, from ImageExportOptions.GetFileName when Revit can
    /// say, else from what appeared in the folder.
    /// </summary>
    private static string ProducedImageName(Document doc, ElementId viewId,
        string outputDir, string expectedName)
    {
        try
        {
            // Static: the view-dependent part of the name Revit generates ("Floor Plan - L1").
            var produced = ImageExportOptions.GetFileName(doc, viewId);
            if (!string.IsNullOrWhiteSpace(produced))
            {
                var match = Directory.GetFiles(outputDir, "*.png")
                    .Select(Path.GetFileName)
                    .FirstOrDefault(file => file != null
                        && file.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase)
                        && file.IndexOf(Path.GetFileNameWithoutExtension(produced), StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
        }
        catch
        {
            // Fall through to the folder scan.
        }
        return ResolveWrittenFile(outputDir, expectedName, ".png");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
