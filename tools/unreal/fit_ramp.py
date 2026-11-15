"""Fits the Unreal prototype's tone response to Unity's, on the tone ramp.

Unity side: UnrealExport -ramp 1 writes ramp_unity.txt, thirty unlit patches (value 0.0005 .. 8.7, a factor 1.4 apart) seen through the
game's real camera and grade. Unreal side: -pe_scene=ramp photographs the same patches through APEGameMode's grade. This tool

  python tools/unreal/fit_ramp.py show <photo.png>      prints the two tables side by side and the weighted error
  python tools/unreal/fit_ramp.py fit [--rounds N]      coordinate descent on the film curve, exposure, saturation and gamma
                                                       (parallel photo runs; resumes from unity_logs/unreal/fit/best.json)

The error is the mean absolute difference in display levels per patch, weighted by how many pixels of the Unity photo of the room
fall nearest to that patch's level: the game lives in the dark end, so that is where the fit has to be right. The best parameters
are printed; copy them into the constants of APEGameMode::SpawnGrade / SpawnPostProcess (the fit changes nothing by itself).
Needs numpy and Pillow, and the same inputs as the photo runs (the grade material imported, the module built).
"""
import argparse
import json
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
UE = REPO / "tools" / "unreal" / "ue.ps1"
WORK = REPO / "unity_logs" / "unreal" / "fit"
W, H = 960, 540        # the fit photographs at half size: the patches are still 38 px wide

# name, Unreal argument, start, step, low, high
PARAMS = [
    ("ev", "-pe_ev=", 0.0, 0.3, -2.0, 2.0),                # added to the exposure of SpawnGrade
    ("toe", "-pe_toe=", 0.7, 0.1, 0.0, 1.0),                # the others replace the values in SpawnPostProcess; these starts are the ones it has now
    ("slope", "-pe_slope=", 0.6, 0.1, 0.2, 1.5),
    ("shoulder", "-pe_shoulder=", 0.46, 0.1, 0.0, 1.0),
    ("blackclip", "-pe_blackclip=", 0.0, 0.05, 0.0, 0.5),
    ("whiteclip", "-pe_whiteclip=", 0.0, 0.05, 0.0, 0.6),
    ("saturation", "-pe_saturation=", 0.74, 0.08, 0.3, 1.3),
    ("gamma", "-pe_gamma=", 1.0, 0.06, 0.6, 1.5),
]


def load_table(path):
    return np.array([[float(x) for x in line.split()[1:4]] for line in open(path) if line.strip()])


def patch_positions(w, h):
    """Screen position of each patch: the camera looks down +X from the origin, vertical field of view 90."""
    tan_v = 1.0
    tan_h = tan_v * w / h
    pos = []
    for i in range(30):
        y_cm = ((i % 6) - 2.5) * 200.0
        z_cm = (2.0 - (i // 6)) * 200.0
        pos.append((int(w / 2 + (y_cm / 1000.0) / tan_h * (w / 2)), int(h / 2 - (z_cm / 1000.0) / tan_v * (h / 2))))
    return pos


def table_of(png):
    a = np.asarray(Image.open(png).convert("RGB")).astype(float)
    h, w = a.shape[:2]
    r = max(2, w // 120)
    return np.array([a[y - r:y + r + 1, x - r:x + r + 1].reshape(-1, 3).mean(axis=0) for x, y in patch_positions(w, h)])


def weights(unity, photo):
    """Each pixel of the Unity photo votes for the nearest patch by luma."""
    im = np.asarray(Image.open(photo).convert("RGB")).astype(float)
    luma = 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]
    levels = unity @ np.array([0.2126, 0.7152, 0.0722])
    nearest = np.abs(luma.reshape(-1, 1)[::7] - levels.reshape(1, -1)).argmin(axis=1)
    w = np.bincount(nearest, minlength=len(levels)).astype(float)
    w /= w.sum()
    return 0.92 * w + 0.08 / len(levels)       # a floor, so that the bright end is not free


class Fit:
    def __init__(self, args):
        self.unity = load_table(args.table)
        self.weights = weights(self.unity, args.photo)
        self.scene = args.scene
        self.parallel = args.parallel
        WORK.mkdir(parents=True, exist_ok=True)
        self.cache_file = WORK / "cache.json"
        self.cache = json.loads(self.cache_file.read_text()) if self.cache_file.exists() else {}

    def error(self, table):
        return float((np.abs(np.asarray(table) - self.unity).mean(axis=1) * self.weights).sum())

    @staticmethod
    def key(p):
        return ",".join("%s=%.4f" % (n, p[n]) for n, *_ in PARAMS)

    def run_one(self, p, tag):
        k = self.key(p)
        if k in self.cache:
            return self.error(self.cache[k])
        out = WORK / tag
        out.mkdir(parents=True, exist_ok=True)
        cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(UE), "photo", "-ResX", str(W), "-ResY", str(H),
               "-pe_scene=ramp", "-pe_file=" + self.scene, "-pe_out=" + str(out), "-pe_warm=20", "-pe_measure=3"]
        cmd += ["%s%.4f" % (arg, p[name]) for name, arg, *_ in PARAMS]
        subprocess.run(cmd, capture_output=True, text=True, timeout=400)
        png = out / "pe_ramp.png"
        if not png.exists():
            print("no photo for", k, flush=True)
            return 1e9
        table = table_of(png)
        self.cache[k] = table.tolist()
        self.cache_file.write_text(json.dumps(self.cache))
        png.unlink()
        return self.error(table)

    def run_many(self, plist):
        res = [None] * len(plist)
        stamp = int(time.time())

        def work(i):
            time.sleep(1.3 * (i % self.parallel))        # ue.ps1 names its log by the second
            res[i] = self.run_one(plist[i], "r%d_%d" % (stamp, i))
        with ThreadPoolExecutor(max_workers=self.parallel) as ex:
            list(ex.map(work, range(len(plist))))
        return res

    def descend(self, rounds):
        best_file = WORK / "best.json"
        cur = {n: s for n, _, s, *_ in PARAMS}
        if best_file.exists():
            cur.update(json.loads(best_file.read_text())["params"])
        steps = {n: st for n, _, _, st, *_ in PARAMS}
        bounds = {n: (lo, hi) for n, _, _, _, lo, hi in PARAMS}
        best = self.run_many([cur])[0]
        print("start", self.key(cur), "error %.3f" % best, flush=True)
        for rnd in range(rounds):
            cands = []
            for n, *_ in PARAMS:
                for sign in (1, -1):
                    q = dict(cur)
                    q[n] = min(bounds[n][1], max(bounds[n][0], cur[n] + sign * steps[n]))
                    if q[n] != cur[n]:
                        cands.append((n, sign, q))
            errs = self.run_many([c[2] for c in cands])
            pick = {}
            for c, e in zip(cands, errs):
                if e < best - 1e-6 and (c[0] not in pick or e < pick[c[0]][1]):
                    pick[c[0]] = (c[2][c[0]], e)
            if not pick:
                steps = {n: s * 0.5 for n, s in steps.items()}
                print("round", rnd, "no improvement: steps halved", flush=True)
                if max(steps.values()) < 0.004:
                    break
                continue
            combined = dict(cur)
            for n, (v, _) in pick.items():
                combined[n] = v
            ce = self.run_many([combined])[0]
            single_e, single_p = min(((e, c[2]) for c, e in zip(cands, errs)), key=lambda t: t[0])
            cur, best = (combined, ce) if ce <= single_e else (single_p, single_e)
            print("round", rnd, "accept", self.key(cur), "error %.3f" % best, flush=True)
            best_file.write_text(json.dumps({"params": cur, "error": best}))
        print("FINAL", self.key(cur), "error %.3f" % best, flush=True)


def show(args):
    unity = load_table(args.table)
    ue = table_of(args.photo_png)
    w = weights(unity, args.photo)
    print("patch  value      unreal (r,g,b)         unity (r,g,b)        weight")
    for i in range(len(unity)):
        v = 0.0005 * 1.4 ** i
        print("%5d  %-9.4g  %6.1f %6.1f %6.1f    %6.1f %6.1f %6.1f    %.3f" % (i, v, *ue[i], *unity[i], w[i]))
    print("weighted error %.2f levels, plain mean %.2f" % (float((np.abs(ue - unity).mean(axis=1) * w).sum()), float(np.abs(ue - unity).mean())))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["show", "fit"])
    ap.add_argument("photo_png", nargs="?", help="show: the Unreal ramp photo (pe_ramp.png)")
    ap.add_argument("--table", default=str(REPO / "unity_logs" / "unreal_export" / "exp" / "ramp" / "ramp_unity.txt"))
    ap.add_argument("--photo", default=str(REPO / "unity_logs" / "unreal_export" / "1000_1" / "unity.png"), help="Unity photo that weights the patches")
    ap.add_argument("--scene", default=str(REPO / "unreal" / "PryingEyes" / "Saved" / "PE" / "scene_1000_1_Ristorante.json"))
    ap.add_argument("--rounds", type=int, default=10)
    ap.add_argument("--parallel", type=int, default=4)
    args = ap.parse_args()
    if args.mode == "show":
        if not args.photo_png:
            ap.error("show needs the photo")
        show(args)
    else:
        Fit(args).descend(args.rounds)


if __name__ == "__main__":
    sys.exit(main())
