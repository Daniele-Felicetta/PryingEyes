#!/usr/bin/env python3
"""Shot list (copione) of the trailer, composed from the scouting data of `-trailer scout` (tools/trailer/floorlib.py).

    python tools/trailer/make_shots.py --mode preview --out unity_logs/trailer/prev2 [--only atrio,navata]
    python tools/trailer/make_shots.py --mode film    --out unity_logs/trailer/take1

Writes <out>.json (the job read by `PryingEyes.exe -batchmode -autotest -nofire -nowaves -trailer <job>`) and prints the shot names.
Coordinates are world metres. Every field is written (JsonUtility ignores initialisers inside arrays; zero / null = default).
Cameras are placed on lanes that the navmesh grid says are free of columns, pews and walls (Floor.free).

Weapons (equip = catalogue index + 1): 3 CARDINALE revolver, 12 BREACHER-12 pump shotgun, 2 TARANTOLA SMG, 16 LANCE-7 scoped DMR, 23 CAMPANA-249 LMG.
"""
import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from floorlib import Floor  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SCOUT = ROOT / "unity_logs" / "trailer" / "scout1"

CHURCH = (1124, 4)       # albergo + chiesa: Atrio, Navata, Presbiterio sul solito asse z = 30,4
THEATER = (1127, 4)      # teatro: Foyer, Platea rossa
SANATORIUM = (1205, 4)   # sanatorio: Corsia, Sala Operatoria


def K(t, p, look, fov=62, roll=0):
    return {"t": float(t), "p": [float(v) for v in p], "look": [float(v) for v in look], "fov": float(fov), "roll": float(roll)}


def E(kind, pos, yaw=0, hp=0, speed=0, delay=0, still=False):
    return {"type": kind, "pos": [float(v) for v in pos], "yaw": float(yaw), "hp": float(hp), "speed": float(speed), "delay": float(delay), "still": bool(still)}


def V(t, kind, v=0, i=0):
    return {"t": float(t), "kind": kind, "v": float(v), "i": int(i)}


def yaw_to(a, b):
    """Degrees to face from a towards b (0 = +z, 90 = +x)."""
    return math.degrees(math.atan2(b[0] - a[0], b[2] - a[2]))


def shot(name, where, dur, keys, **kw):
    s = {
        "name": name, "skip": False, "rebuild": False, "seed": where[0], "floor": where[1], "dur": float(dur), "warm": 0.6,
        "fps": False, "ease": 0, "sway": 0.0, "equip": 0, "torch": False, "lift": False, "ev": 0.0, "bloom": 0.0, "fog": 0.0,
        "dof": 0.0, "focus": 0.0, "focal": 0.0,
        "aim": "", "lookEnemy": 0, "lookFrom": 0.0, "lookOff": [0.0, 0.0, 0.0], "keys": keys, "enemies": [], "events": [], "note": "",
    }
    s.update(kw)
    return s


def fps_shot(name, where, dur, pos, look, equip, enemies, events, ev=0.8, **kw):
    """First-person shot: the player stands at pos (eye height is the game's), the view follows the nearest enemy."""
    keys = [K(0, pos, look, 70), K(dur, pos, look, 70)]
    return shot(name, where, dur, keys, fps=True, aim="nearest", equip=equip, ev=ev, enemies=enemies, events=events, **kw)


# ------------------------------------------------------------------------------------------------ riprese
def church_shots():
    W = CHURCH
    return [
        # l'ascensore si apre sull'Atrio a scacchi
        shot("c01_lift", W, 5.5, [K(0, (15.85, 1.55, 36.8), (30, 1.5, 36.8), 56), K(5.5, (20.5, 1.5, 36.8), (40, 2.3, 36.8), 64)],
             lift=True, ease=1, ev=0.7, warm=0.5, note="ascensore -> Atrio"),
        # carrellata bassa verso il lampadario al centro dell'Atrio, poi in alto sulle travi
        shot("c02_atrio", W, 7.0, [K(0, (21, 1.0, 40), (36, 1.5, 40), 58), K(3.5, (30, 1.15, 40), (42, 3.2, 40), 60), K(7.0, (38, 1.3, 40), (40.5, 7.0, 41), 66)],
             ease=1, ev=0.7, sway=0.3, note="Atrio, lampadario"),
        # dalla balconata, sul pavimento a scacchi dall'alto
        shot("c08_balconata", W, 8.0, [K(0, (27, 5.6, 22.4), (40, 0.5, 41), 60), K(8.0, (52, 6.6, 22.4), (36, 0.5, 42), 60)],
             ease=1, ev=0.8, sway=0.2, note="Atrio dalla balconata"),
        # lungo l'asse: Atrio -> arco -> Navata -> arco
        shot("c03_navata", W, 8.0, [K(0, (50, 1.5, 30.4), (68, 1.8, 30.4), 62), K(2.6, (62.5, 1.45, 30.4), (78, 2.0, 29), 62),
                                    K(5.6, (76, 1.45, 30.4), (92, 2.6, 31.5), 60), K(8.0, (92, 1.5, 30.4), (110, 3.0, 30.4), 58)],
             ev=0.8, sway=0.25, note="Navata sull'asse"),
        # il Presbiterio con le vetrate a occhio
        shot("c04_presbiterio", W, 6.5, [K(0, (101.5, 1.2, 30.4), (122, 2.6, 30.4), 64), K(3.2, (110, 1.9, 30.4), (128, 3.4, 30.4), 60), K(6.5, (119, 3.0, 30.4), (136, 4.6, 28), 54)],
             ease=1, ev=0.8, sway=0.2, note="Presbiterio"),
        # il monaco che avanza nella Navata (teleobiettivo, sfondo sfocato)
        shot("c05_monaco", W, 6.0, [K(0, (73, 1.1, 30.4), (84, 1.7, 30.4), 40), K(6.0, (71, 1.1, 30.4), (80, 1.7, 30.4), 36)],
             ev=0.9, lookEnemy=1, lookFrom=0.0, lookOff=[0, 0.2, 0], sway=0.15, dof=2.8, focal=85,
             enemies=[E("Monaco", (84, 0, 30.4), yaw=270, speed=1.2)], note="Monaco"),
        # l'occhio che fissa, in alto sotto le travi
        shot("c06_occhio", W, 4.5, [K(0, (31, 1.0, 40), (37, 2.6, 40), 38), K(4.5, (30.5, 1.0, 40), (35, 2.6, 40), 34)],
             ev=0.8, lookEnemy=1, lookFrom=0.0, sway=0.2, dof=2.8, focal=85,
             enemies=[E("Occhio", (37, 2.6, 40), yaw=270, speed=0.5)], note="Occhio"),
        # l'Abate nel Presbiterio
        shot("c07_abate", W, 5.5, [K(0, (102, 1.0, 30.4), (114, 2.1, 30.4), 46), K(5.5, (103, 1.0, 30.4), (112, 2.3, 30.4), 42)],
             ev=0.9, lookEnemy=1, lookFrom=0.0, lookOff=[0, 0.3, 0], sway=0.15, dof=2.8, focal=70,
             enemies=[E("Abate", (114, 0, 30.4), yaw=270, speed=0.9)], note="Abate"),
        # l'orda nell'Atrio
        # take 2: the horde charges the camera (12 monks, 14-21 m away at the start, 2.2-2.5 m/s, a rank every few steps)
        shot("h01_orda", W, 6.0, [K(0, (19.5, 1.15, 36), (38, 1.5, 35), 58), K(6.0, (19.0, 1.15, 36), (38, 1.5, 35), 56)],
             ev=0.8, sway=0.2,
             enemies=[E("Monaco", (33 + (i % 4) * 2.4, 0, 29 + (i // 4) * 5 + (i % 2) * 1.0), yaw=270, speed=2.2 + 0.15 * (i % 3), delay=0.15 * i) for i in range(12)],
             note="orda"),
    ]


def theater_shots():
    W = THEATER
    return [
        shot("t01_foyer_lift", W, 5.5, [K(0, (95.85, 1.55, 12), (110, 1.5, 12), 56), K(5.5, (100.5, 1.5, 12), (122, 2.0, 12), 64)],
             lift=True, ease=1, ev=0.7, warm=0.5, note="ascensore -> Foyer"),
        # la Platea rossa lungo la navata centrale
        shot("t02_platea", W, 7.0, [K(0, (119, 1.4, 86), (119, 2.4, 66), 62), K(3.5, (119, 1.4, 76), (119, 3.0, 56), 62), K(7.0, (119, 1.5, 64), (119, 3.4, 46), 58)],
             ev=0.7, sway=0.25, note="Platea r3 lungo l'asse z"),
        shot("t03_maschera", W, 5.0, [K(0, (119, 1.0, 82), (119, 1.7, 70), 36), K(5.0, (119, 1.0, 81), (119, 1.7, 70), 32)],
             ev=0.8, lookEnemy=1, lookOff=[0, 0.2, 0], sway=0.15, dof=2.8, focal=85,
             enemies=[E("Maschera", (119, 0, 70), yaw=0, speed=1.0)], note="Maschera"),
    ]


def sanatorium_shots():
    W = SANATORIUM
    return [
        shot("s01_operatoria", W, 6.0, [K(0, (116, 1.25, 31), (131, 1.8, 31), 60), K(6.0, (127, 1.25, 31), (142, 2.2, 31), 58)],
             ev=1.2, sway=0.2, note="Sala Operatoria"),
        shot("s03_infermiere", W, 5.0, [K(0, (71, 1.2, 30.2), (84, 1.7, 30.2), 40), K(5.0, (69, 1.2, 30.2), (82, 1.7, 30.2), 36)],
             ev=1.2, lookEnemy=1, lookOff=[0, 0.2, 0], sway=0.15, dof=2.8, focal=85,
             enemies=[E("Infermiere", (84, 0, 30.2), yaw=270, speed=1.0)], note="Infermiere"),
        shot("s04_primario", W, 5.0, [K(0, (116, 1.1, 31), (130, 1.9, 31), 44), K(5.0, (115, 1.1, 31), (128, 2.1, 31), 40)],
             ev=1.2, lookEnemy=1, lookOff=[0, 0.3, 0], sway=0.15, dof=2.8, focal=70,
             enemies=[E("Primario", (131, 0, 31), yaw=270, speed=0.9)], note="Primario"),
    ]


def fps_shots():
    return [
        # CARDINALE .357 nella Navata: due colpi, due monaci
        fps_shot("f01_cardinale", CHURCH, 4.5, (66, 1.62, 30.4), (90, 1.6, 30.4), 4,
                 [E("Monaco", (74, 0, 29.8), yaw=270, speed=1.4), E("Monaco", (79, 0, 31.2), yaw=270, speed=1.3, delay=0.4)],
                 [V(0.2, "ads", 1), V(1.3, "fire"), V(1.35, "kill", i=0), V(2.5, "fire"), V(2.55, "kill", i=1), V(3.3, "ads", 0)]),
        # BREACHER-12 nell'Atrio, rallentatore sul primo colpo
        fps_shot("f02_breacher", CHURCH, 5.0, (26, 1.62, 40), (44, 1.6, 40), 13,
                 [E("Monaco", (33, 0, 38.8), yaw=270, speed=2.0), E("Monaco", (36, 0, 41.2), yaw=270, speed=2.0, delay=0.9),
                  E("Monaco", (37, 0, 40), yaw=270, speed=2.0, delay=1.8)],
                 [V(1.3, "fire"), V(1.32, "kill", i=0), V(1.3, "slow", 0.25), V(2.1, "slow", 1.0), V(3.0, "fire"), V(3.02, "kill", i=1), V(4.0, "fire"), V(4.02, "kill", i=2)]),
        # TARANTOLA nella Platea rossa: raffica sulla navata centrale
        fps_shot("f03_tarantola", THEATER, 4.0, (119, 1.62, 82), (119, 1.6, 64), 3,
                 [E("Maschera", (119, 0, 74), yaw=0, speed=1.6), E("Maschera", (119, 0, 71.6), yaw=0, speed=1.6),
                  E("Maschera", (119, 0, 69.2), yaw=0, speed=1.6), E("Maschera", (119, 0, 66.8), yaw=0, speed=1.6)],
                 [V(0.8, "burst", 2.0, 14), V(1.3, "kill", i=0), V(1.8, "kill", i=1), V(2.3, "kill", i=2), V(2.8, "kill", i=3)], ev=0.7),
        # LANCE-7 con il cannocchiale nella Corsia: un colpo a 30 metri
        fps_shot("f04_lance", SANATORIUM, 4.0, (66, 1.62, 30.2), (100, 1.6, 30.2), 17,
                 [E("Infermiere", (98, 0, 30.2), yaw=270, speed=0.9)],
                 [V(0.3, "ads", 1), V(1.8, "fire"), V(1.85, "kill", i=0), V(3.0, "ads", 0)], ev=1.2),
        # CAMPANA-249 nella Sala Operatoria: raffica lunga
        fps_shot("f05_campana", SANATORIUM, 4.0, (116, 1.62, 31), (136, 1.6, 31), 24,
                 [E("Infermiere", (122, 0, 29.8), yaw=270, speed=1.5), E("Infermiere", (124.5, 0, 31.2), yaw=270, speed=1.5),
                  E("Infermiere", (127, 0, 32.4), yaw=270, speed=1.5)],
                 [V(0.6, "burst", 2.4, 11), V(1.4, "kill", i=0), V(1.9, "kill", i=1), V(2.4, "kill", i=2)], ev=1.2),
        # APOCALISSE-12 contro l'Abate nel Presbiterio: tre colpi e l'ultimo al rallentatore
        fps_shot("f06_apocalisse", CHURCH, 6.0, (102, 1.62, 30.4), (122, 1.6, 30.4), 23,
                 [E("Abate", (112, 0, 30.4), yaw=270, speed=0.9)],
                 [V(1.0, "fire"), V(2.3, "fire"), V(3.6, "fire"), V(4.4, "slow", 0.2), V(4.5, "fire"), V(4.55, "kill", i=0), V(5.4, "slow", 1.0)], ev=0.9),
        # PUPILLA (arma aliena) nel Presbiterio
        fps_shot("f07_pupilla", CHURCH, 5.0, (104, 1.62, 30.4), (122, 1.6, 30.4), 27,
                 [E("Monaco", (113, 0, 30.4), yaw=270, speed=1.2), E("Monaco", (116, 0, 31.8), yaw=270, speed=1.2), E("Monaco", (118, 0, 29), yaw=270, speed=1.2)],
                 [V(0.8, "fire"), V(1.0, "kill", i=0), V(1.8, "fire"), V(2.0, "kill", i=1), V(2.8, "fire"), V(3.0, "kill", i=2)], ev=0.9),
    ]


ALL = {"church": church_shots, "theater": theater_shots, "sanatorium": sanatorium_shots, "fps": fps_shots}


def validate(shots):
    """Warn when a ground-level camera segment is closer than 0.9 m to something that is not floor."""
    floors = {}
    for s in shots:
        key = (s["seed"], s["floor"])
        if key not in floors:
            floors[key] = Floor(SCOUT, *key)
        fl = floors[key]
        if s["fps"] or s["lift"]:
            continue
        k = s["keys"]
        for i in range(len(k) - 1):
            a, b = k[i]["p"], k[i + 1]["p"]
            if a[1] > 3.0 or b[1] > 3.0:
                continue
            if not fl.lane_ok((a[0], a[2]), (b[0], b[2]), r=0.9):
                print(f"  ! {s['name']}: camera segment {i}->{i + 1} passes close to an obstacle")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="preview", choices=["preview", "film"])
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    ap.add_argument("--groups", default="church,theater,sanatorium,fps")
    ap.add_argument("--width", type=int, default=1920)
    ap.add_argument("--height", type=int, default=1080)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--crf", type=int, default=14)
    ap.add_argument("--msaa", type=int, default=4)
    a = ap.parse_args()
    out = Path(a.out)
    if not out.is_absolute():
        out = ROOT / out
    shots = []
    for g in a.groups.split(","):
        shots += ALL[g.strip()]()
    validate(shots)
    job = {"mode": a.mode, "outDir": out.as_posix(), "fps": a.fps, "width": a.width, "height": a.height, "crf": a.crf, "msaa": a.msaa,
           "only": a.only, "seedFrom": 0, "seedTo": 0, "floors": [], "shots": shots}
    path = out.with_suffix(".json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(job, indent=1), encoding="utf-8")
    print(path)
    print(", ".join(s["name"] for s in shots))


if __name__ == "__main__":
    main()
