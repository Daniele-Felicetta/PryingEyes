#!/usr/bin/env python3
"""Contact sheet of finished clips: N frames per clip, evenly spaced, one row per clip (the quality check of a film run).

    python tools/trailer/clip_sheet.py unity_logs/trailer/take1 [--frames 4] [--tile 480] [--per 6] [--only f0,c0]

Writes clips_<n>.jpg in the folder (ffmpeg on the PATH).
"""
import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def duration(p):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(p)], capture_output=True, text=True)
    return float(out.stdout.strip())


def frame(p, t, w, dst):
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t:.3f}", "-i", str(p), "-frames:v", "1", "-vf", f"scale={w}:-2", str(dst)], check=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--frames", type=int, default=4)
    ap.add_argument("--tile", type=int, default=480)
    ap.add_argument("--per", type=int, default=6, help="clips per sheet")
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    folder = Path(a.folder)
    words = [w for w in a.only.split(",") if w]
    clips = sorted(p for p in folder.glob("*.mp4") if not words or any(w in p.name for w in words))
    tw = a.tile
    th = tw * 9 // 16
    try:
        f = ImageFont.truetype("arial.ttf", 16)
    except OSError:
        f = ImageFont.load_default()
    with tempfile.TemporaryDirectory() as tmp:
        for n in range(0, len(clips), a.per):
            chunk = clips[n:n + a.per]
            sheet = Image.new("RGB", (a.frames * tw, len(chunk) * th), (10, 10, 12))
            d = ImageDraw.Draw(sheet)
            for r, p in enumerate(chunk):
                dur = duration(p)
                for c in range(a.frames):
                    t = dur * (c + 0.5) / a.frames
                    dst = Path(tmp) / f"{p.stem}_{c}.jpg"
                    frame(p, t, tw, dst)
                    sheet.paste(Image.open(dst).convert("RGB").resize((tw, th)), (c * tw, r * th))
                    d.text((c * tw + 4, r * th + th - 20), f"{t:.1f}s", font=f, fill=(255, 220, 120))
                d.rectangle([0, r * th, 9 * len(p.stem) + 12, r * th + 20], fill=(0, 0, 0))
                d.text((4, r * th + 2), f"{p.stem}  {dur:.1f}s", font=f, fill=(255, 220, 120))
            out = folder / f"clips_{n // a.per + 1}.jpg"
            sheet.save(out, quality=86)
            print(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
