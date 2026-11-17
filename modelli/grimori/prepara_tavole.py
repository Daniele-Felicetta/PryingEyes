#!/usr/bin/env python
"""Prepares the two grimoire plates of the secret book room for the game (python -I prepara_tavole.py [--preview DIR]).

The maintainer's two plates ("Potenziamenti delle Armi - I 19 Sigilli dell'Arsenale Maledetto" and "Perk del Dodecaedro Arcano
Maledetto - Le 19 Virtu' della Stregoneria", sources in sorgenti/) are the art of the open book in the game (GrimoireUI). Their
medallions carry baked icons that do not match the nodes of the game, so this script

  1. empties every medallion (12 outer, 6 inner): the icon is erased and the hollow disc is rebuilt dark, with the grain of the plate;
     the game draws the icon of each node there, with its own state (owned, buyable, locked, chosen);
  2. for the Witchcraft plate, which has only three inner medallions (a ram-headed demon hides the other three), erases the baked inner
     ones and paints six identical bronze thorn wreaths on a regular hexagon around the demon;
  3. feathers the border into transparency, so that the plate floats over the (dimmed) game instead of being a pasted rectangle.

  4. cuts the open book out of each plate (ALTAR_CROP) and makes its glow mask (the red of the blood and the runes), for the 3D
     book on the altar (GrimoireAltar).

It writes, into Assets/_PryingEyes/Resources/Grimori/:
  tavola_arsenale.bytes, tavola_stregoneria.bytes   PNG with alpha (a TextAsset: Unity does not touch it, GrimoirePage decodes it)
  altare_<name>.bytes, luce_<name>.bytes            the open book of the plate as a JPEG, and its glow mask as a PNG (same size)
  tavole.json                                       size and slots of each plate, in plate pixels (the only copy of these numbers)
Order of the slots: outer 0..11 clockwise from the medallion just left of the top one; inner 0..5 clockwise from the top; every
inner k sits between outer 2k and 2k+1 (the nodes it needs), as in Grimoire.Neighbors. Needs numpy, opencv-python and Pillow.
"""
import argparse
import json
import math
import os

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "unity", "PryingEyes", "Assets", "_PryingEyes", "Resources", "Grimori"))

# (x, y) of every medallion, in the pixels of the source plate: first estimates, which fit_hollow() refines on the inner edge of the wreath
PLATES = {
    "arsenale": dict(
        src="sorgenti/tavola_arsenale.webp",
        outer=[(541.0, 186.0), (749.9, 168.0), (925.0, 195.3), (1050.4, 319.4), (1083.0, 497.0), (1056.0, 638.8),
               (944.8, 769.0), (754.0, 819.5), (553.1, 769.0), (450.3, 643.7), (413.3, 496.6), (442.4, 304.6)],
        inner=[(749.7, 326.0), (898.3, 408.2), (900.3, 586.1), (752.1, 663.6), (600.1, 585.5), (599.9, 410.1)],
        center=(750.0, 497.0, 104.0),
        fit_inner=True, repaint_inner=False),
    "stregoneria": dict(
        src="sorgenti/tavola_stregoneria.webp",
        outer=[(553.5, 197.5), (722.5, 169.5), (892.5, 197.5), (1022.5, 302.5), (1061.5, 461.5), (1042.5, 608.5),
               (929.5, 710.5), (727.5, 783.5), (523.5, 708.5), (416.5, 607.5), (380.5, 457.5), (424.5, 301.5)],
        # three of the six inner medallions were baked (armillary sphere, lily, snake); the upper three are new, over the pentagram,
        # the smoke and the wing: they are painted by wreath() on a hexagon around the demon
        inner=[(722.9, 300.0), (905.0, 380.0), (909.7, 547.4), (722.9, 663.9), (536.1, 548.6), (540.8, 380.0)],
        baked_inner=[False, False, True, True, True, False],
        center=(722.5, 470.0, 108.0),
        fit_inner=False, repaint_inner=True),
}
WREATH_HOLLOW = 39.0                      # radius of the hollow of the painted wreaths
ALTAR_CROP = {"arsenale": (80, 30, 1300, 920), "stregoneria": (80, 30, 1300, 920)}     # x, y, w, h of the open book, in plate pixels
ALTAR_WIDTH = 1024                        # width of the picture of the 3D book (the height follows the crop)

FEATHER = 36.0


def load(path):
    return np.asarray(Image.open(os.path.join(HERE, path)).convert("RGB"), dtype=np.float32)


def fit_circle(pts):
    x, y = pts[:, 0], pts[:, 1]
    sol, *_ = np.linalg.lstsq(np.c_[2 * x, 2 * y, np.ones(len(x))], x ** 2 + y ** 2, rcond=None)
    return sol[0], sol[1], math.sqrt(max(0.0, sol[2] + sol[0] ** 2 + sol[1] ** 2))


def fit_hollow(gray, cx, cy, rlo, rhi, nrays=72):
    """The circle of the hollow disc of a medallion: along rays from outside in, the wreath is the bright band and the hollow
    begins where the light falls below the middle between the band and the dark; a robust fit (the icon may touch the wreath at
    some angles) gives centre and radius."""
    h, w = gray.shape
    pts = []
    for j in range(nrays):
        th = 2 * math.pi * j / nrays
        rs = np.arange(rhi, rlo, -0.5)
        xs = np.clip((cx + rs * np.cos(th)).astype(int), 0, w - 1)
        ys = np.clip((cy + rs * np.sin(th)).astype(int), 0, h - 1)
        prof = gray[ys, xs]
        k = int(np.argmax(prof[: int(len(rs) * 0.6)]))
        thr = 0.5 * (prof[k] + 45.0)
        for i in range(k, len(rs)):
            if prof[i] < thr:
                pts.append((cx + rs[i] * math.cos(th), cy + rs[i] * math.sin(th)))
                break
    pts = np.array(pts)
    if len(pts) < 12:
        return cx, cy, 0.5 * (rlo + rhi)
    for _ in range(4):
        c = fit_circle(pts)
        d = np.abs(np.hypot(pts[:, 0] - c[0], pts[:, 1] - c[1]) - c[2])
        pts = pts[d <= np.percentile(d, 70)]
        if len(pts) < 10:
            break
    return fit_circle(pts)


def grain(shape, rng, fine=6.5, coarse=9.0):
    a = cv2.GaussianBlur(rng.normal(0, 1, shape).astype(np.float32), (0, 0), 0.8) * fine
    b = cv2.GaussianBlur(rng.normal(0, 1, shape).astype(np.float32), (0, 0), 3.0) * coarse
    return a + b


def clean_socket(img, cx, cy, R, rng):
    """Erases what is inside the disc (cx, cy, R) and rebuilds it as a dark hollow with a faint red glow at its heart."""
    h, w = img.shape[:2]
    pad = int(R + 8)
    x0, x1, y0, y1 = max(0, int(cx - pad)), min(w, int(cx + pad) + 1), max(0, int(cy - pad)), min(h, int(cy + pad) + 1)
    yy, xx = np.mgrid[y0:y1, x0:x1]
    d = np.hypot(xx - cx, yy - cy)
    patch = img[y0:y1, x0:x1]
    gray = patch.mean(axis=2)
    inside = d < R
    # colour of the dark ground that the icon covered: the darkest third of the disc, kept dark
    core = patch[inside & (gray <= np.percentile(gray[inside], 30))].mean(axis=0)
    core = np.minimum(core, 70.0) * 0.8 + np.array([4.0, 4.0, 14.0], np.float32)         # BGR: a hint of red
    edge = core * 0.45
    t = np.clip(d / R, 0, 1)[..., None] ** 2.2
    fill = core * (1 - t) + edge * t + grain(patch.shape[:2], rng)[..., None] * 0.7
    a = np.clip((R + 1.5 - d) / 3.0, 0, 1)[..., None]
    img[y0:y1, x0:x1] = patch * (1 - a) + fill * a


# ------------------------------------------------------------------ bronze thorn wreath (painted at 4x, then reduced)
BRONZE_DARK = np.array([20, 30, 70], np.float32)       # BGR
BRONZE_LIGHT = np.array([96, 142, 208], np.float32)


def wreath(R=66, ring=42.0, hollow=39.0, seed=1, ss=4):
    """A medallion like the painted ones: dark hollow, thin bronze ring wrapped by two vines, long thorns, a few drops of blood and a
    soft dark halo. Returns (BGR float, alpha 0..1), both (2R+1) pixels square."""
    rng = np.random.default_rng(seed)
    size = 2 * R + 1
    n = size * ss
    c = R * ss + ss // 2
    col = np.zeros((n, n, 3), np.float32)
    alp = np.zeros((n, n), np.float32)

    def pt(r, th):
        return (c + r * ss * math.cos(th), c + r * ss * math.sin(th))

    # halo (soft, dark) and hollow
    halo = np.zeros((n, n), np.float32)
    cv2.circle(halo, (c, c), int((ring + 6) * ss), 1.0, -1, cv2.LINE_AA)
    halo = cv2.GaussianBlur(halo, (0, 0), 4.0 * ss)
    alp = np.maximum(alp, halo * 0.92)
    col += halo[..., None] * np.array([5, 3, 12], np.float32)
    yy, xx = np.mgrid[0:n, 0:n]
    d = np.hypot(xx - c, yy - c) / ss
    hollow_a = np.clip((hollow + 0.7 - d) / 1.4, 0, 1)
    tt = np.clip(d / hollow, 0, 1) ** 2.0
    hcol = np.array([16, 12, 46], np.float32) * (1 - tt)[..., None] + np.array([6, 4, 16], np.float32) * tt[..., None]
    col = col * (1 - hollow_a[..., None]) + hcol * hollow_a[..., None]
    alp = np.maximum(alp, hollow_a)

    def paint(draw, color, box, strength=1.0):
        """draw(layer) paints 1.0 into a layer cut to box (x0, y0, x1, y1); color is blended where the layer is set."""
        nonlocal col, alp
        x0, y0, x1, y1 = (int(max(0, v)) for v in box)
        x1, y1 = min(n, x1), min(n, y1)
        if x1 <= x0 or y1 <= y0:
            return
        layer = np.zeros((y1 - y0, x1 - x0), np.float32)
        draw(layer, x0, y0)
        m = (layer * strength)[..., None]
        col[y0:y1, x0:x1] = col[y0:y1, x0:x1] * (1 - m) + color * m
        alp[y0:y1, x0:x1] = np.maximum(alp[y0:y1, x0:x1], layer * strength)

    def line(p0, p1, width, extra=3):
        def f(layer, x0, y0):
            cv2.line(layer, (int(p0[0] - x0), int(p0[1] - y0)), (int(p1[0] - x0), int(p1[1] - y0)), 1.0, max(1, int(width * ss)), cv2.LINE_AA)
        pad = width * ss + extra
        return f, (min(p0[0], p1[0]) - pad, min(p0[1], p1[1]) - pad, max(p0[0], p1[0]) + pad, max(p0[1], p1[1]) + pad)

    # ring: short segments, lit from the upper left, each a little different
    seg = 360
    for i in range(seg):
        th0, th1 = i * 2 * math.pi / seg, (i + 1.5) * 2 * math.pi / seg
        light = 0.5 + 0.5 * math.cos(th0 - math.radians(225))
        v = (light ** 1.3) * (0.7 + 0.45 * rng.random())
        color = BRONZE_DARK + (BRONZE_LIGHT - BRONZE_DARK) * min(1.0, v)
        f, box = line(pt(ring, th0), pt(ring, th1), 2.8)
        paint(f, color, box)
    # two vines of different pace crossing around the ring
    for ph, k, amp, w, tone in ((0.7, 9.0, 3.0, 1.5, 0.55), (2.3, 13.0, 2.2, 1.2, 0.4)):
        pts = [pt(ring + amp * math.sin(i * 2 * math.pi / 720 * k + ph), i * 2 * math.pi / 720) for i in range(721)]
        color = BRONZE_DARK + (BRONZE_LIGHT - BRONZE_DARK) * tone

        def vine(layer, x0, y0, pts=pts, w=w):
            cv2.polylines(layer, [np.array([(px - x0, py - y0) for px, py in pts], np.int32)], True, 1.0, max(1, int(w * ss)), cv2.LINE_AA)
        paint(vine, color, (c - (ring + 6) * ss, c - (ring + 6) * ss, c + (ring + 6) * ss, c + (ring + 6) * ss), 0.9)
    # thorns: long and short, tilted, one edge lit
    count = 23
    for i in range(count):
        th = (i + (rng.random() - 0.5) * 0.5) * 2 * math.pi / count
        length = 16.0 if i % 4 == 0 else (11.0 if i % 4 == 2 else 6.0 + rng.random() * 4.0)
        tilt = (rng.random() - 0.5) * 0.7
        half = 0.05 if length > 10 else 0.04
        mid = pt(ring + 0.5, th)
        tip = pt(ring + length, th + tilt)
        light = 0.5 + 0.5 * math.cos(th - math.radians(225))
        for (b, tone) in ((pt(ring + 0.5, th - half), 0.8), (pt(ring + 0.5, th + half), 0.3)):
            def thorn(layer, x0, y0, b=b, mid=mid, tip=tip):
                cv2.fillConvexPoly(layer, np.array([(b[0] - x0, b[1] - y0), (mid[0] - x0, mid[1] - y0), (tip[0] - x0, tip[1] - y0)], np.int32), 1.0, cv2.LINE_AA)
            xs = [b[0], mid[0], tip[0]]
            ys = [b[1], mid[1], tip[1]]
            color = BRONZE_DARK + (BRONZE_LIGHT - BRONZE_DARK) * min(1.0, tone * (0.5 + 0.65 * light))
            paint(thorn, color, (min(xs) - 4, min(ys) - 4, max(xs) + 4, max(ys) + 4))
    # drops of blood on the lower rim
    for k in range(4):
        th = math.radians(55 + k * 28 + rng.random() * 12)
        x, y = pt(ring + 1.0, th)
        ln = (6 + rng.random() * 11) * ss

        def drip(layer, x0, y0, x=x, y=y, ln=ln):
            cv2.line(layer, (int(x - x0), int(y - y0)), (int(x - x0), int(y + ln - y0)), 1.0, int(1.8 * ss), cv2.LINE_AA)
            cv2.circle(layer, (int(x - x0), int(y + ln - y0)), int(1.8 * ss), 1.0, -1, cv2.LINE_AA)
        paint(drip, np.array([8, 10, 120], np.float32), (x - 6 * ss, y - 3 * ss, x + 6 * ss, y + ln + 6 * ss), 0.9)
    col = cv2.resize(col, (size, size), interpolation=cv2.INTER_AREA)
    alp = cv2.resize(alp, (size, size), interpolation=cv2.INTER_AREA)
    col = cv2.GaussianBlur(col, (0, 0), 0.6)
    alp = cv2.GaussianBlur(alp, (0, 0), 0.5)
    return col, np.clip(alp, 0, 1)


def paste(img, patch_col, patch_alpha, cx, cy):
    r = patch_col.shape[0] // 2
    x0, y0 = int(round(cx)) - r, int(round(cy)) - r
    sx0, sy0 = max(0, -x0), max(0, -y0)
    ex, ey = min(img.shape[1] - x0, patch_col.shape[1]), min(img.shape[0] - y0, patch_col.shape[0])
    a = patch_alpha[sy0:ey, sx0:ex, None]
    dst = img[y0 + sy0:y0 + ey, x0 + sx0:x0 + ex]
    img[y0 + sy0:y0 + ey, x0 + sx0:x0 + ex] = dst * (1 - a) + patch_col[sy0:ey, sx0:ex] * a


def erase_for_repaint(img, cx, cy, R=54):
    """Wipes a baked medallion (and what overflows it, like the lily's petals) with a smooth fill and the grain of the smoke."""
    h, w = img.shape[:2]
    mask = np.zeros((h, w), np.uint8)
    cv2.circle(mask, (int(round(cx)), int(round(cy))), int(R), 255, -1)
    filled = cv2.inpaint(np.clip(img, 0, 255).astype(np.uint8), mask, 7, cv2.INPAINT_NS).astype(np.float32)
    rng = np.random.default_rng(int(cx * 7 + cy))
    filled += grain(filled.shape[:2], rng, 4.0, 7.0)[..., None] * (mask[..., None] > 0)
    soft = cv2.GaussianBlur(mask.astype(np.float32) / 255.0, (0, 0), 2.0)[..., None]
    img[:] = img * (1 - soft) + filled * soft


def feather(img, rng):
    h, w = img.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    dist = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)).astype(np.float32)
    noise = cv2.GaussianBlur(rng.normal(0, 1, (h, w)).astype(np.float32), (0, 0), 7.0) * 26.0
    d = dist + noise
    t = np.clip(d / FEATHER, 0, 1)
    a = t * t * (3 - 2 * t)
    return np.dstack([np.clip(img, 0, 255), a * 255.0])


def altar_book(name, img):
    """The open book of the plate (before the feather) for the 3D book on the altar, and the mask of what glows on it."""
    x0, y0, cw, ch = ALTAR_CROP[name]
    crop = np.clip(img[y0:y0 + ch, x0:x0 + cw], 0, 255).astype(np.uint8)
    pg = cv2.resize(crop, (ALTAR_WIDTH, int(round(ALTAR_WIDTH * ch / cw))), interpolation=cv2.INTER_AREA)
    Image.fromarray(pg[..., ::-1]).save(os.path.join(OUT, "altare_%s.bytes" % name), format="JPEG", quality=88, optimize=True)
    b, g, r = [pg[..., i].astype(np.float32) for i in range(3)]
    mask = np.clip((r - 0.5 * (g + b) - 88.0) / 60.0, 0, 1) * np.clip((r - 120.0) / 50.0, 0, 1)
    mask = cv2.GaussianBlur(mask, (0, 0), 1.2)
    lum = np.clip(mask * 255.0, 0, 255).astype(np.uint8)
    Image.fromarray(np.dstack([lum, lum, lum])).save(os.path.join(OUT, "luce_%s.bytes" % name), format="PNG", optimize=True)


def process(name, spec, preview_dir):
    img = load(spec["src"])[..., ::-1].copy()          # BGR for OpenCV
    h, w = img.shape[:2]
    rng = np.random.default_rng(1000 + len(name))
    before = img.copy()
    gray = cv2.GaussianBlur(cv2.cvtColor(np.clip(img, 0, 255).astype(np.uint8), cv2.COLOR_BGR2GRAY).astype(np.float32), (0, 0), 1.2)
    outer = [fit_hollow(gray, x, y, 30, 62) for (x, y) in spec["outer"]]
    if spec["fit_inner"]:
        inner = [fit_hollow(gray, x, y, 26, 54) for (x, y) in spec["inner"]]
    else:
        inner = [(x, y, WREATH_HOLLOW) for (x, y) in spec["inner"]]
    for (x, y, r) in outer:
        clean_socket(img, x, y, r - 1.0, rng)
    if spec["repaint_inner"]:
        for (x, y, r), baked in zip(inner, spec["baked_inner"]):
            if baked:
                erase_for_repaint(img, x, y, 54)
        for k, (x, y, r) in enumerate(inner):
            col, alp = wreath(seed=11 + k)
            paste(img, col, alp, x, y)
    else:
        for (x, y, r) in inner:
            clean_socket(img, x, y, r - 1.0, rng)
    altar_book(name, img)
    out = feather(img, rng)
    rgba = np.dstack([out[..., 2], out[..., 1], out[..., 0], out[..., 3]]).astype(np.uint8)
    os.makedirs(OUT, exist_ok=True)
    png = os.path.join(OUT, "tavola_%s.bytes" % name)
    Image.fromarray(rgba).save(png, format="PNG", optimize=True)
    print("%s: %dx%d, %.2f MB" % (name, w, h, os.path.getsize(png) / 1e6))
    for label, group in (("outer", outer), ("inner", inner)):
        print("  " + label, " ".join("(%.0f,%.0f r%.0f)" % g for g in group))
    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        Image.fromarray(rgba).convert("RGB").save(os.path.join(preview_dir, "tavola_%s.png" % name))
        tiles = []
        for (x, y, r) in outer + inner:
            for src in (before, np.clip(img, 0, 255)):
                c = src[int(y) - 64:int(y) + 64, int(x) - 64:int(x) + 64]
                tiles.append(cv2.resize(c.astype(np.uint8), None, fx=1.5, fy=1.5, interpolation=cv2.INTER_CUBIC))
        cols = 6
        rows = [np.hstack(tiles[i:i + cols]) for i in range(0, len(tiles) - len(tiles) % cols, cols)]
        cv2.imwrite(os.path.join(preview_dir, "sockets_%s.png" % name), np.vstack(rows))
    return dict(book=name, w=w, h=h,
                outer=[round(float(v), 1) for g in outer for v in g],
                inner=[round(float(v), 1) for g in inner for v in g],
                center=[round(v, 1) for v in spec["center"]])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", default="", help="folder for the previews (plates and a sheet of the medallions before and after)")
    args = ap.parse_args()
    data = [process(n, s, args.preview) for n, s in PLATES.items()]
    with open(os.path.join(OUT, "tavole.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(dict(plates=data), f, indent=1)
        f.write("\n")
    print("written to", os.path.relpath(OUT, os.path.join(HERE, "..", "..")))


if __name__ == "__main__":
    main()
