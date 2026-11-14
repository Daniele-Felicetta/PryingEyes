#!/usr/bin/env python3
"""Contact sheets of the preview stills (-trailer preview): one row per shot, three tiles (start, middle, end).

    python tools/trailer/preview_sheet.py unity_logs/trailer/prev1 [--per 4] [--tile 640] [--only c0,t0]

Writes sheet_<n>.jpg in the folder and prints the paths.
"""
import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--per", type=int, default=4, help="shots per sheet")
    ap.add_argument("--tile", type=int, default=640)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    folder = Path(a.folder)
    words = [w for w in a.only.split(",") if w]
    names = sorted({p.name[:-6] for p in folder.glob("*_a.jpg")})
    if words:
        names = [n for n in names if any(w in n for w in words)]
    tw = a.tile
    th = tw * 9 // 16
    try:
        f = ImageFont.truetype("arial.ttf", max(14, tw // 28))
    except OSError:
        f = ImageFont.load_default()
    for n in range(0, len(names), a.per):
        chunk = names[n:n + a.per]
        sheet = Image.new("RGB", (3 * tw, len(chunk) * th), (10, 10, 12))
        d = ImageDraw.Draw(sheet)
        for r, name in enumerate(chunk):
            for c, tag in enumerate("abc"):
                p = folder / f"{name}_{tag}.jpg"
                if not p.exists():
                    continue
                sheet.paste(Image.open(p).convert("RGB").resize((tw, th), Image.LANCZOS), (c * tw, r * th))
            d.rectangle([0, r * th, 5 * len(name) + 40, r * th + f.size + 6], fill=(0, 0, 0))
            d.text((4, r * th + 2), name, font=f, fill=(255, 220, 120))
        out = folder / f"sheet_{n // a.per + 1}.jpg"
        sheet.save(out, quality=88)
        print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
