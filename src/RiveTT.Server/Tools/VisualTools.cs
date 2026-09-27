using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;
using RiveTT.Server.Imaging;

namespace RiveTT.Server.Tools;

/// <summary>
/// The visual loop: tools whose answer is an IMAGE the model can look at, not a file name.
///
/// Field report of 2026-09-24 (03): the agent worked blind for a whole session. batch_export
/// wrote PNGs to a disk the agent could not reach, and every visible defect — furniture one
/// storey up, a duplicated lift, missing balconies — was found by the user. capture_view
/// returns the picture as MCP image content, with the pixel-to-model mapping as text next to
/// it; test_image_relay checks, without Revit, that the client relays images at all.
/// </summary>
[McpServerToolType]
public static class VisualTools
{
    [McpServerTool(Name = "capture_view"), Description(
        "SEE the model: render a Revit view (plan, section, elevation, 3D, sheet) and receive it as an IMAGE, with a " +
        "text block giving the view, the image size and — for a cropped plan — the pixel-to-model mapping in mm " +
        "(model = left + px * mmPerPixel, top - py * mmPerPixel). Use it after every significant write: one plan per " +
        "level changed plus one 3D view. Options are applied to a temporary copy rolled back afterwards: bboxMm " +
        "[xmin,ymin,xmax,ymax] crops a plan to a zone, highlightIds paints elements red, isolateCategories keeps only " +
        "some OST_* categories, detailLevel and displayStyle override the view. Nothing is modified in the model; works " +
        "while RiveTT is locked. About 1 500-2 000 tokens per image at 1600 px. If the image does not reach you, call " +
        "test_image_relay, then use returnMode=file.")]
    public static async Task<CallToolResult> CaptureView(
        RevitConnectionManager revit,
        [Description("View element ID. Omit to use viewName or the active view")] long? viewId = null,
        [Description("View name (exact, then case-insensitive), when the id is not known")] string? viewName = null,
        [Description("Long side of the image in pixels, 256 to 4000. Default: 1600")] int pixelSize = 1600,
        [Description("Plan views only: model rectangle to frame, JSON [xmin, ymin, xmax, ymax] in mm")] System.Text.Json.JsonElement? bboxMm = null,
        [Description("Temporary detail level: Coarse | Medium | Fine")] string? detailLevel = null,
        [Description("Temporary display style: HLR | Shading | ShadingWithEdges | Realistic | Wireframe")] string? displayStyle = null,
        [Description("Element ids to paint red, JSON array, e.g. [10639950, 10640392]")] System.Text.Json.JsonElement? highlightIds = null,
        [Description("Categories to keep, all others hidden, JSON array of OST_* codes, e.g. [\"OST_Furniture\",\"OST_Walls\"]")] System.Text.Json.JsonElement? isolateCategories = null,
        [Description("png (default, sharp lines) | jpg (smaller, for shaded 3D)")] string? format = null,
        [Description("inline (default): image in the answer | file: keep it under %TEMP%\\RiveTT\\captures and return the path only | both")] string? returnMode = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["pixelSize"] = pixelSize };
        if (viewId != null) p["viewId"] = viewId;
        if (viewName != null) p["viewName"] = viewName;
        if (detailLevel != null) p["detailLevel"] = detailLevel;
        if (displayStyle != null) p["displayStyle"] = displayStyle;
        if (format != null) p["format"] = format;
        if (returnMode != null) p["returnMode"] = returnMode;
        if (JsonOptionalParam.IsProvided(bboxMm))
        {
            if (!JsonArrayParam.TryParse(bboxMm, out var bboxArray))
                return Text(JsonArrayParam.InvalidArrayResult("capture_view", "bboxMm", bboxMm));
            p["bboxMm"] = bboxArray;
        }
        if (JsonOptionalParam.IsProvided(highlightIds))
        {
            if (!JsonArrayParam.TryParse(highlightIds, out var highlightArray))
                return Text(JsonArrayParam.InvalidArrayResult("capture_view", "highlightIds", highlightIds));
            p["highlightIds"] = highlightArray;
        }
        if (JsonOptionalParam.IsProvided(isolateCategories))
        {
            if (!JsonArrayParam.TryParse(isolateCategories, out var isolateArray))
                return Text(JsonArrayParam.InvalidArrayResult("capture_view", "isolateCategories", isolateCategories));
            p["isolateCategories"] = isolateArray;
        }

        // A capture of a large shaded 3D view can take a while on a busy workstation.
        var response = await revit.ExecuteAsync("capture_view", p, 240, ct);
        return ImageResponse.From(response);
    }

    [McpServerTool(Name = "test_image_relay"), Description(
        "Diagnostic, no Revit needed: returns a fixed test image (a red square and the text TEST 42). If you can read " +
        "TEST 42 in it, this client relays MCP images and capture_view will work; if not, the connector does not " +
        "relay images — use capture_view with returnMode=file and ask the user to look at the file.")]
    public static CallToolResult TestImageRelay()
    {
        var png = TestPattern.RenderPng();
        return new CallToolResult
        {
            Content =
            {
                ImageContentBlock.FromBytes(png, "image/png"),
                new TextContentBlock
                {
                    Text = new JObject
                    {
                        ["success"] = true,
                        ["expectedText"] = TestPattern.Text,
                        ["imageSizePx"] = new JArray(TestPattern.Width, TestPattern.Height),
                        ["imageBytes"] = png.Length,
                        ["check"] = "Read the text in the image. TEST 42 means images reach you; anything else means they do not.",
                        ["mcpServerVersion"] = ConnectorVersions.McpServer
                    }.ToString(Formatting.None)
                }
            }
        };
    }

    private static CallToolResult Text(string json) => new()
    {
        Content = { new TextContentBlock { Text = json } }
    };
}

/// <summary>
/// Turns a plugin answer carrying imageBase64 into MCP content: the image block first, then
/// the same JSON without the base64 — the metadata the agent needs to read the picture.
/// Public and Revit-free so the split can be tested.
/// </summary>
public static class ImageResponse
{
    public static CallToolResult From(JToken response)
    {
        var result = new CallToolResult();
        if (response is JObject obj && obj["imageBase64"]?.Type == JTokenType.String)
        {
            var base64 = obj["imageBase64"]!.Value<string>()!;
            var mime = obj["mimeType"]?.Value<string>() ?? "image/png";
            obj.Remove("imageBase64");
            try
            {
                var bytes = Convert.FromBase64String(base64);
                result.Content.Add(ImageContentBlock.FromBytes(bytes, mime));
                obj["imageInContent"] = true;
            }
            catch (FormatException)
            {
                obj["imageInContent"] = false;
                obj["imageError"] = "The plugin returned an image that is not valid base64.";
            }
        }
        result.Content.Add(new TextContentBlock { Text = response.ToString(Formatting.None) });
        return result;
    }
}
