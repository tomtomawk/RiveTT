using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Imports a raster/PDF file as an ImageType and places it as an ImageInstance in a
/// view — the missing entry point for a scanned survey or a surveyor's underlay.
/// Verified API: ImageType.Create(Document, ImageTypeOptions), then
/// ImageInstance.Create(Document, View, ElementId, ImagePlacementOptions).
/// </summary>
[ToolSafety(false, false)]
public class ManageImagesTool : IRiveTTTool
{
    public string Name => "manage_images";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Imports a raster/PDF file as an image and places it in a view (survey scan, surveyor underlay). " +
        "action=place|list. place needs filePath (bmp/jpg/jpeg/png/tif/pdf) and viewId; optional position " +
        "({x,y,z} mm, defaults to the view origin) and resolutionDpi (default 300).";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = (input["action"]?.Value<string>() ?? "list").ToLowerInvariant();
        try
        {
            return action switch
            {
                "list" => ListImages(doc),
                "place" => PlaceImage(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unsupported action: {action}", suggestion: "Use: list | place")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static RiveTTResult<object> ListImages(Document doc)
    {
        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(ImageInstance))
            .Cast<ImageInstance>()
            .Select(i => new
            {
                id = ToolHelpers.GetElementIdValue(i.Id),
                ownerViewId = ToolHelpers.GetElementIdValue(i.OwnerViewId)
            })
            .ToList();
        return RiveTTResult<object>.Ok(new { count = instances.Count, images = instances });
    }

    private static RiveTTResult<object> PlaceImage(Document doc, JObject input)
    {
        var filePath = input["filePath"]?.Value<string>();
        var viewIdLong = input["viewId"]?.Value<long?>() ?? 0;
        if (string.IsNullOrWhiteSpace(filePath) || viewIdLong <= 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "filePath and viewId are required");

        var view = doc.GetElement(ToolHelpers.ToElementId(viewIdLong)) as View;
        if (view == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, $"viewId {viewIdLong} is not a View");

        var resolutionDpi = input["resolutionDpi"]?.Value<double?>() ?? 300;
        var positionToken = input["position"];
        var location = positionToken != null
            ? new XYZ(
                (positionToken["x"]?.Value<double>() ?? 0) / MmPerFoot,
                (positionToken["y"]?.Value<double>() ?? 0) / MmPerFoot,
                (positionToken["z"]?.Value<double>() ?? 0) / MmPerFoot)
            : XYZ.Zero;

        using var tx = new Transaction(doc, "RiveTT: Place Image");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        ImageType imageType;
        ImageInstance instance;
        try
        {
            var options = new ImageTypeOptions(filePath!, false, ImageTypeSource.Import) { Resolution = resolutionDpi };
            imageType = ImageType.Create(doc, options);

            var placement = positionToken != null
                ? new ImagePlacementOptions(location, BoxPlacement.Center)
                : new ImagePlacementOptions();
            instance = ImageInstance.Create(doc, view, imageType.Id, placement);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Failed to import/place the image: {ex.Message}",
                suggestion: "Supported formats: bmp, jpg, jpeg, png, tif, and pdf (when PDF support is available). " +
                            "filePath must be reachable from the Revit process.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return RiveTTResult<object>.Ok(new
        {
            imageTypeId = ToolHelpers.GetElementIdValue(imageType.Id),
            imageInstanceId = ToolHelpers.GetElementIdValue(instance.Id),
            viewId = ToolHelpers.GetElementIdValue(view.Id)
        });
    }
}
