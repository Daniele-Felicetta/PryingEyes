"""Thumbnails for the wiki, made from the screenshots and renders the game and the labs already produce.

tools/wiki_images.json lists every image the wiki ships: dest (a path under wiki/), and one of
  from     one source file, relative to the source root, optionally cropped with crop: [left, top, right, bottom] fractions
  montage  several sources in a labelled grid, one tile each (labels: the names shown under the tiles)
  card     a card drawn from catalogue data for an entry the game has no picture of (kicker, title, lines, footer);
           its footer says it is a card, not a screenshot
Each image is written as WebP at most `width` pixels wide (default 720). The sources live in folders that git ignores
(hotel_build/, unity_logs/), so the manifest is the record of where every picture came from, and --source-root says
which checkout holds them.

Usage: python tools/wiki_images.py [--source-root PATH] [--only TEXT]     needs Pillow (tools/requirements-studio.txt)
"""
from __future__ import annotations

import argparse
import json
import sys
import textwrap
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = Path(__file__).resolve().parent / "wiki_images.json"
WIKI = ROOT / "wiki"


def load(path: Path):
    from PIL import Image
    return Image.open(path).convert("RGB")


def single(source: Path, crop, width: int):
    from PIL import Image
    image = load(source)
    if crop:
        w, h = image.size
        left, top, right, bottom = crop
        image = image.crop((round(left * w), round(top * h), round(right * w), round(bottom * h)))
    if image.width > width:
        image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    return image


def montage(sources: list, labels: list, columns: int, tile: int):
    from PIL import Image, ImageDraw, ImageFont
    font = ImageFont.load_default()
    rows = (len(sources) + columns - 1) // columns
    label_h = 22
    sheet = Image.new("RGB", (columns * tile, rows * (tile + label_h)), (21, 19, 15))
    draw = ImageDraw.Draw(sheet)
    for index, (source, label) in enumerate(zip(sources, labels)):
        image = load(source)
        image.thumbnail((tile - 12, tile - 12), Image.LANCZOS)
        x, y = (index % columns) * tile, (index // columns) * (tile + label_h)
        sheet.paste(image, (x + (tile - image.width) // 2, y + (tile - image.height) // 2))
        draw.text((x + 6, y + tile + 4), label, fill=(236, 228, 210), font=font)
    return sheet


CARD_SIZE = (720, 540)
PAPER, INK, GOLD, MUTED = (21, 19, 15), (236, 228, 210), (217, 164, 65), (163, 154, 134)


def font(size: int):
    from PIL import ImageFont
    for name in ("georgia.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default(size)


def card(spec: dict):
    """A card for an entry without a picture of its own: kicker, title and facts, from the catalogue the page is written
    from. The footer says it is a card, so nobody takes it for a screenshot of the game."""
    from PIL import Image, ImageDraw
    width, height = CARD_SIZE
    image = Image.new("RGB", CARD_SIZE, PAPER)
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, width, 8), fill=GOLD)
    draw.text((48, 40), spec.get("kicker", "").upper(), fill=GOLD, font=font(20))
    y = 80
    for line in textwrap.wrap(spec["title"], 24):
        draw.text((48, y), line, fill=INK, font=font(44))
        y += 54
    y += 14
    for fact in spec.get("lines", []):
        for line in textwrap.wrap(fact, 50):
            if y > height - 90:
                break
            draw.text((48, y), line, fill=INK, font=font(24))
            y += 33
        y += 10
    footer = spec.get("footer", "Card from the repository's catalogue, not a screenshot of the game")
    draw.text((48, height - 46), footer, fill=MUTED, font=font(16))
    return image


def build(source_root: Path, only: str = "") -> list[str]:
    entries = json.loads(MANIFEST.read_text(encoding="utf-8"))["images"]
    done = []
    for entry in entries:
        dest = WIKI / entry["dest"]
        if only and only not in entry["dest"]:
            continue
        width = int(entry.get("width", 720))
        if "card" in entry:
            image = card(entry["card"])
        elif "from" in entry:
            image = single(source_root / entry["from"], entry.get("crop"), width)
        else:
            tile = int(entry.get("tile", 360))
            columns = int(entry.get("columns", 3))
            sources = [source_root / s for s in entry["montage"]]
            image = montage(sources, entry.get("labels", [Path(s).stem for s in entry["montage"]]), columns, tile)
        dest.parent.mkdir(parents=True, exist_ok=True)
        image.save(dest, "WEBP", quality=int(entry.get("quality", 74)), method=6)
        done.append(entry["dest"])
    return done


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Make the wiki's images from the manifest.")
    parser.add_argument("--source-root", type=Path, default=ROOT, help="the checkout that holds hotel_build/ and unity_logs/")
    parser.add_argument("--only", default="", help="make only the images whose path contains this text")
    args = parser.parse_args(argv)
    try:
        done = build(args.source_root.resolve(), args.only)
    except FileNotFoundError as error:
        print(f"WIKI IMAGES: missing source {error.filename}", file=sys.stderr)
        return 1
    print(f"WIKI IMAGES: {len(done)} written")
    return 0


if __name__ == "__main__":
    sys.exit(main())
