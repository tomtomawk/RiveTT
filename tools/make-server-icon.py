"""Builds src/RiveTT.Server/rivett-server.ico from the ribbon's rivet images.

Small sizes are written as classic 32-bit BMP (DIB + AND mask) entries and only 256 px as
PNG. The 0.6.0 icon stored every size as PNG: Windows' own extraction copes, but PNG is only
guaranteed for 256 px, and Task Manager, the taskbar and older shell paths read the small
entries. 16 and 32 px reuse the hand-drawn ribbon images, which are sharper than a
downscale of the 128 px source.

    python tools/make-server-icon.py
"""
import io
import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RESOURCES = ROOT / "src" / "RiveTT.Plugin" / "Resources"
SOURCE = RESOURCES / "source" / "rivet-pose.png"
HAND_DRAWN = {16: RESOURCES / "rivet-pose-16.png", 32: RESOURCES / "rivet-pose-32.png"}
TARGET = ROOT / "src" / "RiveTT.Server" / "rivett-server.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]


def frame(size: int) -> Image.Image:
    path = HAND_DRAWN.get(size)
    image = Image.open(path if path else SOURCE).convert("RGBA")
    return image if image.size == (size, size) else image.resize((size, size), Image.Resampling.LANCZOS)


def bmp_entry(image: Image.Image) -> bytes:
    """BITMAPINFOHEADER + bottom-up BGRA rows + 1-bit AND mask, as .ico expects."""
    width, height = image.size
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = image.load()
    rows = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            rows += bytes((b, g, r, a))
    mask_stride = ((width + 31) // 32) * 4
    mask = bytearray()
    for y in range(height - 1, -1, -1):
        row = bytearray(mask_stride)
        for x in range(width):
            if pixels[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row
    return header + bytes(rows) + bytes(mask)


def png_entry(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def main() -> None:
    entries = [(size, png_entry(frame(size)) if size >= 256 else bmp_entry(frame(size))) for size in SIZES]
    directory = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    body = b""
    for size, data in entries:
        directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset + len(body))
        body += data
    TARGET.write_bytes(directory + body)
    print(f"{TARGET.relative_to(ROOT)}: {', '.join(str(s) for s, _ in entries)} px, {len(directory + body)} bytes")


if __name__ == "__main__":
    main()
