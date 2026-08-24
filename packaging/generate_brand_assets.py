#!/usr/bin/env python3
"""Generate dependency-free PNG package logos for the Windows MSIX."""

from __future__ import annotations

import argparse
import binascii
import struct
import zlib
from pathlib import Path


ASSETS = {
    "StoreLogo.png": 50,
    "Square44x44Logo.png": 44,
    "Square150x150Logo.png": 150,
}


def chunk(kind: bytes, data: bytes) -> bytes:
    payload = kind + data
    return struct.pack(">I", len(data)) + payload + struct.pack(">I", binascii.crc32(payload) & 0xFFFFFFFF)


def is_r_pixel(x: int, y: int, size: int) -> bool:
    left = size * 28 // 100
    top = size * 20 // 100
    right = size * 68 // 100
    middle = size * 50 // 100
    stroke = max(2, size * 9 // 100)
    stem = left <= x < left + stroke and top <= y < size * 80 // 100
    upper = top <= y < top + stroke and left <= x < right
    bowl = right - stroke <= x < right and top <= y < middle
    bar = middle - stroke <= y < middle and left <= x < right
    diagonal_center = left + stroke + (y - middle) * 3 // 5
    diagonal = middle <= y < size * 80 // 100 and abs(x - diagonal_center) <= stroke // 2
    return stem or upper or bowl or bar or diagonal


def png_bytes(size: int) -> bytes:
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            if is_r_pixel(x, y, size):
                row.extend((255, 255, 255, 255))
            else:
                row.extend((28, 78, 121, 255))
        rows.append(bytes(row))

    header = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return header + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for name, size in ASSETS.items():
        (args.output / name).write_bytes(png_bytes(size))


if __name__ == "__main__":
    main()
