using System.IO.Compression;
using System.Text;

namespace RiveTT.Server.Imaging;

/// <summary>
/// Draws the fixed test image of test_image_relay — a red square and the text "TEST 42" —
/// and encodes it as a PNG, with nothing but the base class library (the server targets
/// plain net10.0: no System.Drawing, and no binary is committed to the repository).
///
/// Plan de test 03 §6.1 of the 2026-09-24 field report: before blaming Revit for an image the
/// agent never sees, check that the MCP client relays image content at all. If the agent
/// can read "TEST 42", the chain server → client → model works.
/// </summary>
public static class TestPattern
{
    public const string Text = "TEST 42";
    public const int Width = 360;
    public const int Height = 120;

    // 5 x 7 glyphs, one string per row, '#' = ink.
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
    };

    /// <summary>RGB pixels, row-major, 3 bytes per pixel.</summary>
    public static byte[] RenderRgb()
    {
        var pixels = new byte[Width * Height * 3];
        Fill(pixels, 0, 0, Width, Height, 255, 255, 255);
        Fill(pixels, 12, 12, 96, 96, 220, 0, 0);            // the red square

        var x = TextLeft;
        foreach (var ch in Text)
        {
            var glyph = Glyphs[ch];
            for (var row = 0; row < glyph.Length; row++)
                for (var col = 0; col < glyph[row].Length; col++)
                    if (glyph[row][col] == '#')
                        Fill(pixels, x + col * Scale, 25 + row * Scale, Scale, Scale, 0, 0, 0);
            x += 6 * Scale;
        }
        return pixels;
    }

    public static byte[] RenderPng() => EncodePng(RenderRgb(), Width, Height);

    private const int Scale = 5;
    public const int TextLeft = 124;

    /// <summary>Right edge of the last glyph: must stay inside the image, or the 2 of 42 is cut.</summary>
    public static int TextRight => TextLeft + (Text.Length * 6 - 1) * Scale;

    /// <summary>8-bit RGB, no interlace, filter 0 on every scanline.</summary>
    public static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter: none
                    zlib.Write(rgb, y * width * 3, width * 3);
                }
            }
            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Fill(byte[] pixels, int x0, int y0, int w, int h, byte r, byte g, byte b)
    {
        for (var y = y0; y < Math.Min(Height, y0 + h); y++)
            for (var x = x0; x < Math.Min(Width, x0 + w); x++)
            {
                var i = (y * Width + x) * 3;
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(typeBytes, data));
        output.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>CRC-32 (ISO-HDLC, as PNG requires) over the chunk type then its data.</summary>
    public static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
