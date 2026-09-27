using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Imaging;
using RiveTT.Server.Tools;
using RiveTT.Tools.Utilities;
using RiveTT.Tools.Views;
using Xunit;

namespace RiveTT.Tests.Server;

/// <summary>
/// The visual loop of the 2026-09-24 field report (03): an image the agent can actually see,
/// the metadata that lets it convert pixels back to model millimetres, and a Revit-free check
/// that the MCP client relays images at all.
/// </summary>
public class VisualToolsTests
{
    [Fact]
    public void TestPatternIsAValidPngOfTheAnnouncedSize()
    {
        var png = TestPattern.RenderPng();

        Assert.True(PngInfo.TryReadSize(png, out var width, out var height, out var mime));
        Assert.Equal("image/png", mime);
        Assert.Equal(TestPattern.Width, width);
        Assert.Equal(TestPattern.Height, height);

        // Every chunk's CRC must verify, or strict decoders refuse the image.
        var offset = 8;
        var chunks = 0;
        while (offset < png.Length)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            var type = png.AsSpan(offset + 4, 4).ToArray();
            var data = png.AsSpan(offset + 8, length).ToArray();
            var crc = (uint)((png[offset + 8 + length] << 24) | (png[offset + 9 + length] << 16)
                             | (png[offset + 10 + length] << 8) | png[offset + 11 + length]);
            Assert.Equal(TestPattern.Crc32(type, data), crc);
            chunks++;
            offset += 12 + length;
        }
        Assert.Equal(3, chunks); // IHDR, IDAT, IEND
    }

    [Fact]
    public void TestPatternPixelsDecodeBackToTheRedSquareAndTheText()
    {
        var png = TestPattern.RenderPng();
        // IDAT starts after the signature (8) and IHDR (12 + 13).
        var idatOffset = 8 + 25;
        var length = (png[idatOffset] << 24) | (png[idatOffset + 1] << 16) | (png[idatOffset + 2] << 8) | png[idatOffset + 3];
        Assert.Equal("IDAT", Encoding.ASCII.GetString(png, idatOffset + 4, 4));
        using var zlib = new ZLibStream(new MemoryStream(png, idatOffset + 8, length), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var scanlines = raw.ToArray();
        Assert.Equal(TestPattern.Height * (1 + TestPattern.Width * 3), scanlines.Length);

        byte[] Pixel(int x, int y)
        {
            var i = y * (1 + TestPattern.Width * 3) + 1 + x * 3;
            return new[] { scanlines[i], scanlines[i + 1], scanlines[i + 2] };
        }
        Assert.Equal(new byte[] { 220, 0, 0 }, Pixel(50, 50));       // inside the red square
        Assert.Equal(new byte[] { 255, 255, 255 }, Pixel(5, 5));     // background
        Assert.Equal(new byte[] { 0, 0, 0 }, Pixel(124 + 2, 25 + 1)); // top bar of the T
        // The whole text fits: the first version cut the 2 of "42" at the right edge.
        Assert.True(TestPattern.TextRight < TestPattern.Width - 4, $"text ends at {TestPattern.TextRight} px");
        Assert.Equal(new byte[] { 0, 0, 0 }, Pixel(TestPattern.TextRight - 2, 25 + 6 * 5 + 1)); // bottom bar of the 2
    }

    [Fact]
    public void TestImageRelayAnswersWithAnImageAndItsExpectedText()
    {
        var result = VisualTools.TestImageRelay();
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        var text = Assert.IsType<TextContentBlock>(result.Content[1]);
        Assert.Equal(TestPattern.Text, JObject.Parse(text.Text)["expectedText"]!.Value<string>());
    }

    [Fact]
    public void ACaptureAnswerIsSplitIntoAnImageAndMetadataWithoutTheBase64()
    {
        var png = TestPattern.RenderPng();
        var response = new JObject
        {
            ["imageBase64"] = Convert.ToBase64String(png),
            ["mimeType"] = "image/png",
            ["imageSizePx"] = new JArray(320, 120),
            ["execution"] = new JObject { ["toolReadOnly"] = true }
        };

        var result = ImageResponse.From(response);

        Assert.Equal(2, result.Content.Count);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(png, image.DecodedData.ToArray());
        var metadata = JObject.Parse(Assert.IsType<TextContentBlock>(result.Content[1]).Text);
        Assert.Null(metadata["imageBase64"]);
        Assert.True(metadata["imageInContent"]!.Value<bool>());
        Assert.True(metadata["execution"]!["toolReadOnly"]!.Value<bool>());
    }

    [Fact]
    public void AFailureStaysText()
    {
        var failure = JObject.Parse("{\"success\":false,\"error\":{\"code\":\"InvalidInput\",\"message\":\"x\"}}");
        var result = ImageResponse.From(failure);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("InvalidInput", text.Text);
    }

    [Fact]
    public void JpegSizeIsReadFromItsFrameHeader()
    {
        // SOI, APP0 (length 16), SOF0 with height 1043, width 1600.
        var jpeg = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x04, 0x13, 0x06, 0x40, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01
        };
        Assert.True(PngInfo.TryReadSize(jpeg, out var width, out var height, out var mime));
        Assert.Equal("image/jpeg", mime);
        Assert.Equal(1600, width);
        Assert.Equal(1043, height);
    }

    [Fact]
    public void ACroppedNorthUpPlanGivesAMappingThatIsNeverClaimedVerified()
    {
        // Crop 38 m x 18 m from (-1000, 14000) at the top left, rendered 1600 px wide.
        var frame = new CaptureMapping.ViewFrame(true, -1000, 14000, 38000, 18000, "FloorPlan", UpIsNorth: true);
        var mapping = JObject.FromObject(CaptureMapping.Compute(frame, 1600, 758));

        Assert.True(mapping["available"]!.Value<bool>());
        Assert.True(mapping["aspectMatchesCrop"]!.Value<bool>());
        // A symmetric margin keeps the aspect: matching aspects prove nothing (review of 0.6.0).
        Assert.False(mapping["verified"]!.Value<bool>());
        Assert.Contains("highlightIds", mapping["calibrate"]!.Value<string>());
        Assert.Equal(23.75, mapping["mmPerPixel"]!.Value<double>(), 2);
        // 18 000 mm at 23.75 mm/px is 757.9 px of a 758 px image: a 0.05 px margin, 1.2 mm.
        var origin = mapping["originTopLeftMm"]!.Values<double>().ToArray();
        Assert.Equal(-1000.0, origin[0], 1);
        Assert.InRange(origin[1], 14000.0, 14002.0);
    }

    [Fact]
    public void AnImageWiderThanItsCropIsReportedAsApproximate()
    {
        var frame = new CaptureMapping.ViewFrame(true, 0, 10000, 10000, 10000, "FloorPlan", UpIsNorth: true);
        var mapping = JObject.FromObject(CaptureMapping.Compute(frame, 1600, 1200));
        Assert.False(mapping["aspectMatchesCrop"]!.Value<bool>());
        Assert.Contains("approximate", mapping["note"]!.Value<string>());
    }

    [Fact]
    public void AnUncroppedViewHasNoMappingAndSaysWhy()
    {
        var plan = JObject.FromObject(CaptureMapping.Compute(
            new CaptureMapping.ViewFrame(false, 0, 0, 0, 0, "FloorPlan", false), 1600, 900));
        Assert.False(plan["available"]!.Value<bool>());
        Assert.Contains("bboxMm", plan["reason"]!.Value<string>());

        var threeD = JObject.FromObject(CaptureMapping.Compute(
            new CaptureMapping.ViewFrame(false, 0, 0, 0, 0, "ThreeD", false), 1600, 900));
        Assert.Contains("3D", threeD["reason"]!.Value<string>());
    }
}
