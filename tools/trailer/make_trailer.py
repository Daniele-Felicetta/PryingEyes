#!/usr/bin/env python3
"""The edit of the Prying Eyes trailer: cuts the filmed clips of a take, lays captions and the title card over them, mixes the score
(trailer_score.py) and writes one mp4. Everything is data in SEGMENTS / CAPTIONS below; nothing is hand-timed in a video editor.

    python tools/trailer/make_trailer.py --take unity_logs/trailer/take1 --out unity_logs/trailer/prying_eyes_trailer.mp4
    python tools/trailer/make_trailer.py ... --preview       # 960x540, fast: to check the cuts and the captions
    python tools/trailer/make_trailer.py ... --no-audio      # picture only

Structure: act "a" = slow opening joined by cross-dissolves; act "b" = hard cuts on the beat (100 BPM, 0.6 s); then the title card.
"""
import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))

FPS = 30
BEAT = 0.6                      # 100 BPM
TITLE_DUR = 5.5
WEAPONS = REPO / "unity" / "PryingEyes" / "Assets" / "_PryingEyes" / "Resources" / "Production" / "weapons.json"


@dataclass
class Seg:
    clip: str                   # file name (without .mp4) in the take folder
    ss: float                   # in point in the clip (s)
    dur: float                  # visible length (s)
    xf: float = 0.0             # cross-dissolve from the previous segment (act "a" only)
    fadein: float = 0.0
    act: str = "b"              # a: dissolves, b: cuts, t: title


# Act II+ lengths are multiples of the beat (0.6 s) so the cuts fall on the pulse of the score.
SEGMENTS = [
    # ---- act I: the building, in the order the player would meet it
    Seg("c01_lift", 0.0, 5.4, fadein=0.9, act="a"),            # lift doors open on the church atrium
    Seg("c02_atrio", 0.6, 5.0, xf=0.5, act="a"),
    Seg("c03_navata", 1.0, 5.4, xf=0.5, act="a"),
    Seg("t02_platea", 2.0, 3.2, xf=0.5, act="a"),               # a different floor: theatre
    Seg("s01_operatoria", 1.5, 2.8, xf=0.4, act="a"),           # and another: sanatorium
    Seg("c04_presbiterio", 0.5, 4.8, xf=0.5, act="a"),          # back to the church: the eye window
    # ---- act II: what lives in it (4 beats each, the abbot 5)
    Seg("c05_monaco", 2.6, 4 * BEAT),
    Seg("c06_occhio", 1.2, 4 * BEAT),
    Seg("t03_maschera", 1.8, 4 * BEAT),
    Seg("s04_primario", 1.8, 4 * BEAT),
    Seg("c07_abate", 2.3, 5 * BEAT),
    # ---- act III: the arsenal (first person)
    Seg("f01_cardinale", 0.8, 4 * BEAT),
    Seg("f02_breacher", 0.9, 5 * BEAT),
    Seg("f03_tarantola", 0.5, 4 * BEAT),
    Seg("f04_lance", 0.2, 4 * BEAT),
    Seg("f05_campana", 0.3, 4 * BEAT),
    Seg("f07_pupilla", 0.6, 4 * BEAT),
    Seg("f06_apocalisse", 2.6, 5 * BEAT),
    # ---- act IV: the horde
    Seg("h01_orda", 1.0, 7 * BEAT),
    # ---- title
    Seg("TITLE", 0.0, TITLE_DUR, act="t"),
]

# (kind, text, sub, clip, offset from that segment's start, length). A caption may run on into the next segment.
CAPTIONS = [
    ("center", "L'hotel ti aspetta", None, "c01_lift", 1.9, 3.0),
    ("center", "Ogni piano è diverso", "chiese, teatri, sanatori", "t02_platea", 0.6, 4.2),
    ("center", "E qualcosa ti osserva", None, "c04_presbiterio", 1.3, 3.0),
    ("label", "MONACO", None, "c05_monaco", 0.25, 1.9),
    ("label", "OCCHIO", None, "c06_occhio", 0.25, 1.9),
    ("label", "MASCHERA", None, "t03_maschera", 0.25, 1.9),
    ("label", "PRIMARIO", None, "s04_primario", 0.25, 1.9),
    ("label", "ABATE", None, "c07_abate", 0.25, 2.5),
    ("weapon", "f01_cardinale", None, "f01_cardinale", 0.2, 2.0),
    ("weapon", "f02_breacher", None, "f02_breacher", 0.2, 2.6),
    ("weapon", "f03_tarantola", None, "f03_tarantola", 0.2, 2.0),
    ("weapon", "f04_lance", None, "f04_lance", 0.2, 2.0),
    ("weapon", "f05_campana", None, "f05_campana", 0.2, 2.0),
    ("weapon", "f07_pupilla", None, "f07_pupilla", 0.2, 2.0),
    ("weapon", "f06_apocalisse", None, "f06_apocalisse", 0.2, 2.6),
    ("center", "Ondate senza fine", None, "h01_orda", 0.4, 3.3),
]


class Takes:
    """Filmed clips live in one or more take folders (each with its job .json next to it); the first folder that has a clip wins,
    so a re-shoot of a few shots can sit in front of the full take."""

    def __init__(self, folders):
        self.folders = [Path(f) for f in folders]
        self.meta = [{s["name"]: s for s in json.loads(Path(str(f) + ".json").read_text(encoding="utf-8"))["shots"]} for f in self.folders]

    def find(self, clip):
        for f, m in zip(self.folders, self.meta):
            if (f / f"{clip}.mp4").exists():
                return f / f"{clip}.mp4", m.get(clip)
        raise FileNotFoundError(f"clip {clip} is in none of {[str(f) for f in self.folders]}")

    def shots(self, segs):
        """The shot-list entry of every filmed segment, after checking that the segment lies inside the clip it was filmed in."""
        out = {}
        for s in segs:
            if s.act == "t":
                continue
            meta = self.find(s.clip)[1]
            if meta is not None and s.ss + s.dur > float(meta["dur"]) + 1e-6:
                raise ValueError(f"{s.clip}: {s.ss} + {s.dur} s runs past the {meta['dur']} s that were filmed")
            out[s.clip] = meta
        return out


DEFAULT_TAKES = "unity_logs/trailer/take2,unity_logs/trailer/take1"


def layout(segs):
    """Start time of every segment on the timeline (cross-dissolves overlap the previous one) and the total length."""
    pos, t = {}, 0.0
    for s in segs:
        t -= s.xf
        pos[s.clip] = t
        t += s.dur
    return pos, t


def weapon_data():
    ws = json.loads(WEAPONS.read_text(encoding="utf-8"))["weapons"]
    return [w["definition"] for w in ws]


def run(cmd, **kw):
    r = subprocess.run(cmd, capture_output=True, text=True, **kw)
    if r.returncode != 0:
        raise RuntimeError(f"command failed ({r.returncode}): {' '.join(map(str, cmd[:6]))} ...\n{r.stderr[-3000:]}")
    return r


def make_captions(work, W, H, take_shots):
    """Renders every caption to a transparent full-frame PNG; returns [(png path, start time, length)]."""
    import trailer_title as TT
    pos, _ = layout(SEGMENTS)
    wd = weapon_data()
    out = []
    for k, (kind, text, sub, clip, off, length) in enumerate(CAPTIONS):
        if kind == "center":
            img = TT.caption_center(W, H, text, sub)
        elif kind == "label":
            img = TT.caption_label(W, H, text, sub)
        else:
            eq = int(take_shots[text]["equip"])
            d = wd[eq - 1]
            img = TT.caption_weapon(W, H, d["name"], d["cls"], d["grade"])
        p = work / f"cap_{k:02d}.png"
        img.save(p)
        out.append((p, pos[clip] + off, length))
    return out


def build_graph(segs, pos, caps, takes, title, W, H):
    """Returns (ffmpeg input args, filter graph text, number of video inputs)."""
    inputs, lines = [], []
    idx = 0
    vlabels = {"a": [], "b": [], "t": []}
    for s in segs:
        n = int(round(s.dur * FPS))
        if s.act == "t":
            inputs += ["-i", str(title)]
            f = f"[{idx}:v]fps={FPS},trim=start_frame=0:end_frame={n},setpts=PTS-STARTPTS,format=yuv420p,setsar=1[v{idx}]"
        else:
            inputs += ["-ss", f"{s.ss:.3f}", "-t", f"{s.dur + 0.3:.3f}", "-i", str(takes.find(s.clip)[0])]
            f = (f"[{idx}:v]fps={FPS},trim=start_frame=0:end_frame={n},setpts=PTS-STARTPTS,"
                 f"scale={W}:{H}:flags=lanczos,format=yuv420p,setsar=1")
            if s.fadein > 0:
                f += f",fade=t=in:st=0:d={s.fadein:.3f}"
            f += f"[v{idx}]"
        lines.append(f)
        vlabels[s.act].append((idx, s))
        idx += 1
    # act a: chain of cross-dissolves
    a = vlabels["a"]
    cur = f"v{a[0][0]}"
    start = pos[a[0][1].clip]
    for i, (j, s) in enumerate(a[1:], 1):
        off = pos[s.clip] - start
        lines.append(f"[{cur}][v{j}]xfade=transition=fade:duration={s.xf:.3f}:offset={off:.3f}[xa{i}]")
        cur = f"xa{i}"
    lines.append(f"[{cur}]null[A]")
    # act b: cuts
    b = vlabels["b"]
    lines.append("".join(f"[v{j}]" for j, _ in b) + f"concat=n={len(b)}:v=1:a=0[B]")
    t = vlabels["t"]
    lines.append(f"[v{t[0][0]}]null[T]")
    lines.append("[A][B][T]concat=n=3:v=1:a=0[base]")
    # captions
    prev = "base"
    for k, (png, t0, length) in enumerate(caps):
        inputs += ["-loop", "1", "-framerate", str(FPS), "-t", f"{length:.3f}", "-i", str(png)]
        fi = 0.45
        lines.append(f"[{idx}:v]format=rgba,fade=t=in:st=0:d={fi}:alpha=1,fade=t=out:st={length - fi:.3f}:d={fi}:alpha=1,"
                     f"setpts=PTS+{t0:.3f}/TB[cap{k}]")
        lines.append(f"[{prev}][cap{k}]overlay=0:0:eof_action=pass:format=auto[ov{k}]")
        prev = f"ov{k}"
        idx += 1
    lines.append(f"[{prev}]format=yuv420p[vout]")
    return inputs, ";\n".join(lines), idx


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--take", default=DEFAULT_TAKES, help="take folders, comma separated, first match wins")
    ap.add_argument("--out", default="unity_logs/trailer/prying_eyes_trailer.mp4")
    ap.add_argument("--work", default="unity_logs/trailer/work")
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--no-audio", action="store_true")
    ap.add_argument("--reuse-title", action="store_true")
    ap.add_argument("--reuse-audio", action="store_true")
    ap.add_argument("--crf", type=int, default=0)
    a = ap.parse_args()
    takes, out, work = Takes(a.take.split(",")), Path(a.out), Path(a.work)
    work.mkdir(parents=True, exist_ok=True)
    W, H = (960, 540) if a.preview else (1920, 1080)
    crf = a.crf or (28 if a.preview else 16)
    shots = takes.shots(SEGMENTS)
    pos, total = layout(SEGMENTS)
    print(f"timeline: {total:.2f} s, {len(SEGMENTS)} segments, {len(CAPTIONS)} captions, {W}x{H}")

    title = work / ("title_preview.mp4" if a.preview else "title.mp4")
    if not (a.reuse_title and title.exists()):
        import trailer_title as TT
        print("rendering the title card ...")
        TT.write_title(title, W, H, FPS, TITLE_DUR)
    caps = make_captions(work, W, H, shots)

    wav = work / ("audio_preview.wav" if a.preview else "audio.wav")
    if not a.no_audio:
        if not (a.reuse_audio and wav.exists()):
            import trailer_score as SC
            import soundfile as sf
            print("building the score ...")
            y = SC.build(SEGMENTS, pos, total, shots, BEAT)
            sf.write(str(wav), y, SC.SR, subtype="PCM_24")

    inputs, graph, n_in = build_graph(SEGMENTS, pos, caps, takes, title, W, H)
    gfile = work / "graph.txt"
    gfile.write_text(graph, encoding="utf-8")
    cmd = ["ffmpeg", "-v", "error", "-stats", "-y"] + inputs
    if not a.no_audio:
        cmd += ["-i", str(wav)]
    cmd += ["-/filter_complex", str(gfile), "-map", "[vout]"]
    if not a.no_audio:
        cmd += ["-map", f"{n_in}:a", "-c:a", "aac", "-b:a", "192k"]
    cmd += ["-r", str(FPS), "-c:v", "libx264", "-preset", "veryfast" if a.preview else "slow", "-crf", str(crf), "-pix_fmt", "yuv420p",
            "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
            "-t", f"{total:.3f}", "-movflags", "+faststart", str(out)]
    print("encoding ...")
    r = subprocess.run(cmd)
    if r.returncode != 0:
        print("ffmpeg failed", r.returncode)
        return r.returncode
    print("written", out, f"{out.stat().st_size / 1e6:.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
