using System;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Reads the pixel size of a PNG or JPEG from its header bytes, without System.Drawing.
/// capture_view needs the size Revit actually produced: FitToPage fixes one side, the other
/// follows the view's aspect, and the pixel-to-millimetre mapping is only as good as that
/// number.
/// </summary>
public static class PngInfo
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static bool TryReadSize(byte[] bytes, out int width, out int height, out string mimeType)
    {
        width = height = 0;
        mimeType = "application/octet-stream";
        if (bytes == null || bytes.Length < 24) return false;

        if (bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            // Signature, then the IHDR chunk: length (4), "IHDR" (4), width (4), height (4).
            if (bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
                return false;
            width = ReadBigEndian(bytes, 16);
            height = ReadBigEndian(bytes, 20);
            mimeType = "image/png";
            return width > 0 && height > 0;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            mimeType = "image/jpeg";
            // Walk the segments to the first start-of-frame marker.
            var i = 2;
            while (i + 9 < bytes.Length)
            {
                if (bytes[i] != 0xFF) { i++; continue; }
                var marker = bytes[i + 1];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    height = (bytes[i + 5] << 8) | bytes[i + 6];
                    width = (bytes[i + 7] << 8) | bytes[i + 8];
                    return width > 0 && height > 0;
                }
                var length = (bytes[i + 2] << 8) | bytes[i + 3];
                if (length < 2) return false;
                i += 2 + length;
            }
        }
        return false;
    }

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
