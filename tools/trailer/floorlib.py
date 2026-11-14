#!/usr/bin/env python3
"""Reads the scouting data of one floor (scout_<seed>_<floor>.json written by `-trailer scout`) and answers geometric questions
for the shot composer: where is the floor, how much clearance is there around a point, which straight lane is free of columns.

`walk` holds the navmesh points on a 0.8 m grid (two heights). Columns, pews and walls are holes in it, so clearance on the grid is
a good stand-in for "the camera will not end up inside a prop". Coordinates are world metres, y up.

    python tools/trailer/floorlib.py unity_logs/trailer/scout1 1124 4       # writes map_1124_4.png next to the json
"""
import json
import math
import sys
from pathlib import Path

STEP = 0.8


class Floor:
    def __init__(self, folder, seed, floor):
        self.seed, self.floor = seed, floor
        self.path = Path(folder) / f"scout_{seed}_{floor}.json"
        d = json.loads(self.path.read_text(encoding="utf-8"))
        self.data = d
        self.rooms = {r["id"]: r for r in d["rooms"]}
        self.openings = d["openings"]
        self.minx = min(r["x0"] for r in d["rooms"])
        self.minz = min(r["z0"] for r in d["rooms"])
        self.maxx = max(r["x1"] for r in d["rooms"])
        self.maxz = max(r["z1"] for r in d["rooms"])
        w = d["walk"]
        self.cells = {}
        for i in range(0, len(w), 3):
            x, y, z = w[i], w[i + 1], w[i + 2]
            self.cells[(round((x - self.minx) / STEP), round((z - self.minz) / STEP), 0 if y < 1.5 else 1)] = y

    # -------------------------------------------------------------- cells
    def key(self, x, z, lvl=0):
        return round((x - self.minx) / STEP), round((z - self.minz) / STEP), lvl

    def walkable(self, x, z, lvl=0):
        return self.key(x, z, lvl) in self.cells

    def free(self, x, z, r=1.2, lvl=0):
        """True when every grid cell within r metres of (x, z) is walkable."""
        n = int(math.ceil(r / STEP))
        kx, kz, _ = self.key(x, z, lvl)
        for dx in range(-n, n + 1):
            for dz in range(-n, n + 1):
                if math.hypot(dx * STEP, dz * STEP) <= r + 1e-6 and (kx + dx, kz + dz, lvl) not in self.cells:
                    return False
        return True

    def lane_ok(self, a, b, r=1.2, lvl=0, step=0.4):
        """Straight segment a -> b (x, z pairs) with clearance r all along."""
        n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / step))
        return all(self.free(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n, r, lvl) for i in range(n + 1))

    def free_run(self, x0, x1, z, r=1.2, lvl=0):
        """Longest free part of the lane z = const between x0 and x1: (start, end)."""
        best, cur = (x0, x0), None
        x = x0
        while x <= x1 + 1e-6:
            if self.free(x, z, r, lvl):
                cur = x if cur is None else cur
                if x - cur > best[1] - best[0]:
                    best = (cur, x)
            else:
                cur = None
            x += 0.4
        return best

    def best_lane_x(self, x0, x1, zmin, zmax, r=1.2, lvl=0):
        """z of the lane parallel to x with the longest free run, and that run."""
        best = (None, (0, 0))
        z = zmin
        while z <= zmax + 1e-6:
            run = self.free_run(x0, x1, z, r, lvl)
            if run[1] - run[0] > best[1][1] - best[1][0] + 1e-6:
                best = (z, run)
            z += 0.4
        return best

    def best_lane_z(self, z0, z1, xmin, xmax, r=1.2, lvl=0):
        """x of the lane parallel to z with the longest free run, and that run (in z)."""
        best = (None, (0, 0))
        x = xmin
        while x <= xmax + 1e-6:
            n0 = None
            z = z0
            run = (z0, z0)
            while z <= z1 + 1e-6:
                if self.free(x, z, r, lvl):
                    n0 = z if n0 is None else n0
                    if z - n0 > run[1] - run[0]:
                        run = (n0, z)
                else:
                    n0 = None
                z += 0.4
            if run[1] - run[0] > best[1][1] - best[1][0] + 1e-6:
                best = (x, run)
            x += 0.4
        return best

    def room(self, rid):
        return self.rooms[rid]

    def center(self, rid):
        r = self.rooms[rid]
        return (r["x0"] + r["x1"]) / 2, (r["z0"] + r["z1"]) / 2


def draw_map(fl, scale=8):
    from PIL import Image, ImageDraw, ImageFont
    W = int((fl.maxx - fl.minx + 4) * scale)
    H = int((fl.maxz - fl.minz + 4) * scale)
    im = Image.new("RGB", (W, H), (14, 14, 18))
    d = ImageDraw.Draw(im)

    def px(x, z):
        return (x - fl.minx + 2) * scale, (z - fl.minz + 2) * scale
    for (kx, kz, lvl) in fl.cells:
        x = fl.minx + kx * STEP
        z = fl.minz + kz * STEP
        a = px(x - STEP / 2, z - STEP / 2)
        b = px(x + STEP / 2, z + STEP / 2)
        d.rectangle([a, b], fill=(96, 120, 150) if lvl else (70, 70, 74))
    try:
        f = ImageFont.truetype("arial.ttf", 14)
    except OSError:
        f = ImageFont.load_default()
    for r in fl.data["rooms"]:
        d.rectangle([px(r["x0"], r["z0"]), px(r["x1"], r["z1"])], outline=(230, 190, 80), width=2)
        d.text((px(r["x0"] + 0.6, r["z0"] + 0.4)), f"{r['id']} {r['type'] if r['type'] != 'Normale' else ''} {r['kind']}", fill=(255, 220, 120), font=f)
    for o in fl.openings:
        if o["b"] < 0:
            continue
        if o["alongZ"] if "alongZ" in o else not o["alongX"]:
            a, b = px(o["line"], o["center"] - o["width"] / 2), px(o["line"], o["center"] + o["width"] / 2)
        else:
            a, b = px(o["center"] - o["width"] / 2, o["line"]), px(o["center"] + o["width"] / 2, o["line"])
        d.line([a, b], fill=(60, 230, 120), width=4)
    ls = fl.data.get("liftSpawn")
    if ls:
        x, z = px(ls[0], ls[2])
        d.ellipse([x - 6, z - 6, x + 6, z + 6], outline=(255, 80, 80), width=3)
    # ruler every 10 m
    for gx in range(int(fl.minx // 10) * 10, int(fl.maxx) + 10, 10):
        x, _ = px(gx, 0)
        d.line([(x, 0), (x, 10)], fill=(150, 150, 150))
        d.text((x + 2, 10), str(gx), fill=(150, 150, 150), font=f)
    for gz in range(int(fl.minz // 10) * 10, int(fl.maxz) + 10, 10):
        _, z = px(0, gz)
        d.line([(0, z), (10, z)], fill=(150, 150, 150))
        d.text((12, z - 6), str(gz), fill=(150, 150, 150), font=f)
    out = fl.path.with_name(f"map_{fl.seed}_{fl.floor}.png")
    im.save(out)
    return out


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    print(draw_map(Floor(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]))))
