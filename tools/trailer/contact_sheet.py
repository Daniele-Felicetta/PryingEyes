#!/usr/bin/env python3
"""Contact sheets of the scouting stills (-trailer scout): one sheet per floor, one tile per camera view, labelled with the room.

    python tools/trailer/contact_sheet.py unity_logs/trailer/scout1 [--cols 4] [--tile 480]

Writes sheet_<seed>_<floor>_<n>.jpg next to the stills (at most --rows rows per sheet so that a sheet stays readable).
"""
import argparse
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def font(size):
    for name in ("arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--rows", type=int, default=4)
    ap.add_argument("--tile", type=int, default=480, help="tile width in pixels")
    args = ap.parse_args()
    folder = Path(args.folder)
    tw = args.tile
    th = tw * 9 // 16
    f = font(max(14, tw // 26))
    for js in sorted(folder.glob("scout_*.json")):
        data = json.loads(js.read_text(encoding="utf-8"))
        rooms = {r["id"]: r for r in data["rooms"]}
        tiles = []
        for name in data.get("stills", []):
            p = folder / name
            if not p.exists():
                continue
            m = re.match(r"s(\d+)_f(\d+)_r(\d+)_(\w+?)_(\w+)\.jpg", name)
            if m:
                r = rooms.get(int(m.group(3)), {})
                label = f"r{m.group(3)} {m.group(4)} {r.get('type', '')} {r.get('x1', 0) - r.get('x0', 0):.0f}x{r.get('z1', 0) - r.get('z0', 0):.0f} [{m.group(5)}]"
            else:
                label = name
            tiles.append((label, p))
        per = args.cols * args.rows
        for n in range(0, len(tiles), per):
            chunk = tiles[n:n + per]
            rows = (len(chunk) + args.cols - 1) // args.cols
            sheet = Image.new("RGB", (args.cols * tw, rows * th), (10, 10, 12))
            d = ImageDraw.Draw(sheet)
            for i, (label, p) in enumerate(chunk):
                im = Image.open(p).convert("RGB").resize((tw, th), Image.LANCZOS)
                x, y = (i % args.cols) * tw, (i // args.cols) * th
                sheet.paste(im, (x, y))
                d.rectangle([x, y, x + tw, y + f.size + 6], fill=(0, 0, 0))
                d.text((x + 4, y + 2), label, font=f, fill=(255, 220, 120))
            out = folder / f"sheet_{data['seed']}_{data['floor']}_{n // per + 1}.jpg"
            sheet.save(out, quality=88)
            print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
