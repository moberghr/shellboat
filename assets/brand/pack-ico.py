#!/usr/bin/env python3
"""Pack PNGs into a Windows .ico with PNG-compressed entries (Windows Vista+).

Usage: pack-ico.py <out.ico> <png> [<png> ...]   (each PNG square, <=256px)
"""
import struct
import sys


def png_size(data: bytes) -> int:
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit("not a PNG")
    width, height = struct.unpack(">II", data[16:24])
    if width != height or width > 256:
        raise SystemExit(f"need a square PNG <=256px, got {width}x{height}")
    return width


def main() -> None:
    out, *paths = sys.argv[1:]
    images = [open(p, "rb").read() for p in paths]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, payload = b"", b""
    for data in images:
        size = png_size(data)
        dim = 0 if size == 256 else size  # 0 means 256 in the ICO directory
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset + len(payload))
        payload += data
    with open(out, "wb") as f:
        f.write(header + entries + payload)


if __name__ == "__main__":
    main()
