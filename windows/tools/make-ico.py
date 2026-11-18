#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
"""Altyazı Köprüsü simgesinden (design/altyazi-koprusu/icon-1024.png) PNG sıkıştırmalı .ico üretir.

Yalnız standart kütüphane; yeniden boyutlandırma macOS'ta `sips` ile yapılır:
    python3 windows/tools/make-ico.py
Çıktı: windows/src/GameTranslator.App/Assets/AltyaziKoprusu.ico (16, 24, 32, 48, 64, 128, 256).
"""
import struct, subprocess, sys, tempfile, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / "design/altyazi-koprusu/icon-1024.png"
OUT = ROOT / "windows/src/GameTranslator.App/Assets/AltyaziKoprusu.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]

def main():
    with tempfile.TemporaryDirectory() as tmp:
        images = []
        for size in SIZES:
            png = pathlib.Path(tmp) / f"{size}.png"
            subprocess.run(["sips", "-z", str(size), str(size), str(SRC), "--out", str(png)], check=True, capture_output=True)
            images.append((size, png.read_bytes()))
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset + len(blobs))
        blobs += data
    OUT.write_bytes(header + entries + blobs)
    print(f"{OUT} ({OUT.stat().st_size} bayt, {len(images)} boyut)")

if __name__ == "__main__":
    sys.exit(main())
