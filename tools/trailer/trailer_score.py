#!/usr/bin/env python3
"""The soundtrack of the trailer, built from the edit (make_trailer.SEGMENTS) and the shot list (events: fire / burst / kill / slow).

The recorder cannot capture audio in batchmode, so every sound is placed here by time: the game's own samples (hotel_build/audio) for the
guns, the lift, the enemies and the rooms; synthesised drone, pulse, bass, risers and hits for the score (trailer_audio.py has the voices).
100 BPM from the first monster on: the cuts of act II-IV fall on the beat, so a kick on every beat lands on every cut.

    python tools/trailer/trailer_score.py OUT.wav          # builds the score alone, to listen / to look at (trailer_check.py audio)
"""
import sys
from pathlib import Path

import numpy as np
from scipy import signal
from scipy.ndimage import minimum_filter1d, uniform_filter1d

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from trailer_audio import SR, Mix, load, pitch, lp, hp, bp, fade, saw_stack, drone, boom, braam, riser, heartbeat, whoosh  # noqa: E402

TARGET_LUFS = -16.0
CEILING = 0.84                                   # -1.5 dBFS
GUN_KIND = {4: "revolver", 13: "shotgun", 3: "smg", 17: "br", 24: "lmg", 23: "shotgun", 27: "beam"}   # weapons.json snd, by equip index


def DB(d):
    return 10.0 ** (d / 20.0)


def pk(x, db):
    """Scale to a peak of db dBFS."""
    return (x * (DB(db) / (float(np.max(np.abs(x))) + 1e-9))).astype("float32")


def rms(x, db):
    """Scale to an RMS of db dBFS."""
    return (x * (DB(db) / (float(np.sqrt(np.mean(x ** 2))) + 1e-9))).astype("float32")


def noise(dur, seed=0):
    return np.random.default_rng(seed).standard_normal(int(dur * SR)).astype("float32")


def stack(*xs):
    """Sum of signals of different lengths (zero padded to the longest)."""
    out = np.zeros(max(len(x) for x in xs), "float32")
    for x in xs:
        out[:len(x)] += x
    return out


def tt(dur):
    return np.arange(int(dur * SR)) / SR


def env_db(n, pts):
    """Gain curve, piecewise linear in dB through [(seconds, dB)]; n samples."""
    ts, ds = zip(*pts)
    return (10.0 ** (np.interp(np.arange(n) / SR, ts, ds) / 20.0)).astype("float32")


# ------------------------------------------------------------------------------------------------ voci
def kick(dur=0.5, f0=135.0, f1=46.0, tau_a=0.17):
    t = tt(dur)
    f = f1 + (f0 - f1) * np.exp(-t / 0.028)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * (1 - np.exp(-t / 0.002)) * np.exp(-t / tau_a)
    y += hp(noise(dur, 1), 1800) * np.exp(-t / 0.006) * 0.12
    y += bp(noise(dur, 11), 130, 420) * np.exp(-t / 0.025) * 0.45       # a knock a phone speaker can reproduce
    return y.astype("float32")


def snare(dur=0.4):
    t = tt(dur)
    y = bp(noise(dur, 2), 900, 7500) * np.exp(-t / 0.085) * 0.9 + np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.05) * 0.5
    return y.astype("float32")


def hat(dur=0.09):
    return (hp(noise(dur, 3), 6500) * np.exp(-tt(dur) / 0.018)).astype("float32")


def bass_note(f, dur=0.5, tau=0.2):
    t = tt(dur)
    y = lp(saw_stack(f, dur, cents=(-6, 0, 6), seed=5), 240) + np.sin(2 * np.pi * f * t) * 0.9
    return (y * (1 - np.exp(-t / 0.006)) * np.exp(-t / tau)).astype("float32")


def bell(f, dur=4.5):
    t = tt(dur)
    y = np.zeros_like(t)
    for r, a, d in ((1.0, 1.0, 2.4), (2.0, 0.5, 1.7), (2.76, 0.32, 1.2), (4.07, 0.18, 0.8), (5.4, 0.1, 0.5)):
        y += a * np.sin(2 * np.pi * f * r * t + r) * np.exp(-t / (d * dur / 4.5))
    return (y * (1 - np.exp(-t / 0.003))).astype("float32")


def pad(notes, dur, cut=1100, gain=1.0):
    y = sum(saw_stack(f, dur, cents=(-11, -5, 0, 6, 12), seed=i + 20) / (1 + 0.3 * i) for i, f in enumerate(notes))
    y = lp(y, cut)
    t = tt(dur)
    y *= (0.8 + 0.2 * np.sin(2 * np.pi * 0.09 * t + 0.7)).astype("float32")
    return (y * gain / (float(np.max(np.abs(y))) + 1e-9)).astype("float32")


def rumble(dur, f=90.0, seed=8):
    y = np.cumsum(noise(dur, seed))
    y = hp(y - np.mean(y), 18)
    y = lp(y, f)
    return (y / (float(np.max(np.abs(y))) + 1e-9)).astype("float32")


def beam(dur=1.3):
    """The PUPILLA: an alien rifle, so it has no recorded sample; a falling zap with a sizzle and a sub thump."""
    t = tt(dur)
    f = 260 + 3200 * np.exp(-t / 0.10)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = (np.sin(ph) + 0.35 * np.sign(np.sin(ph * 0.5))) * np.exp(-t / 0.30) * (1 - np.exp(-t / 0.003))
    z = bp(noise(dur, 6), 1800, 9000) * np.exp(-t / 0.22) * 0.5
    b = boom(dur, 42, 130, 0.22) * 0.9
    return pk(y * 0.8 + z + b, -2.0)


def splat(dur=0.45, seed=9):
    """A wet impact for a kill: a short low-passed burst and a thud."""
    t = tt(dur)
    y = lp(noise(dur, seed), 1500) * np.exp(-t / 0.07) + np.sin(2 * np.pi * (70 + 60 * np.exp(-t / 0.05)) * t) * np.exp(-t / 0.12) * 0.9
    return pk(y, -3.0)


def make_ir(rt, seed):
    """Decorrelated stereo noise-tail reverb impulse (darkened, exponential decay)."""
    out = []
    for c in range(2):
        rng = np.random.default_rng(seed + c * 17)
        n = int(rt * SR)
        t = np.arange(n) / SR
        ir = lp(rng.standard_normal(n).astype("float32") * np.exp(-6.9 * t / rt).astype("float32"), 4200)
        k = int(0.012 * SR)
        ir[:k] *= np.linspace(0, 1, k, dtype="float32")
        out.append(ir / (np.sqrt(np.sum(ir ** 2)) + 1e-9))
    return out


def send(bus, rt, wet, seed):
    irl, irr = make_ir(rt, seed)
    n = bus.n
    return np.stack([signal.fftconvolve(bus.buf[0], irl)[:n], signal.fftconvolve(bus.buf[1], irr)[:n]]).astype("float32") * wet


# ------------------------------------------------------------------------------------------------ tempi
def frame_q(t, fps=30):
    return np.ceil(t * fps - 3e-3) / fps


def shot_times(ev, fps=30):
    """Frame-quantised times of the shots of a fire / burst event (the recorder fires on the first frame at or after the due time)."""
    if ev["kind"] == "fire":
        return [frame_q(ev["t"], fps)]
    rate = max(1, ev["i"])
    out, k = [], 0
    while True:
        tk = ev["t"] + k / rate
        if tk >= ev["t"] + ev["v"]:
            break
        out.append(frame_q(tk, fps))
        k += 1
    return out


# ------------------------------------------------------------------------------------------------ master
def limiter(y, ceiling=CEILING, look=0.003, rel=0.14):
    env = np.max(np.abs(y), axis=1)
    d = np.clip(1.0 - ceiling / (env + 1e-9), 0.0, 1.0)             # wanted reduction
    blk = 48
    nb = len(d) // blk + 1
    dm = np.pad(d, (0, nb * blk - len(d))).reshape(nb, blk).max(axis=1)
    r, acc, a = np.empty(nb), 0.0, np.exp(-blk / (rel * SR))
    for i in range(nb):
        acc = max(dm[i], acc * a)
        r[i] = acc
    red = np.interp(np.arange(len(d)), (np.arange(nb) + 0.5) * blk, r)
    red = np.roll(red, -int(look * SR))
    red = uniform_filter1d(minimum_filter1d(1 - red, size=int(2 * look * SR) + 1, mode="nearest"), size=int(look * SR) + 1, mode="nearest")
    g = red
    out = y * g[:, None]
    print(f"  limiter: max reduction {-20 * np.log10(max(1e-6, float(np.min(g)))):.1f} dB")
    return out.astype("float32")


def lufs(y):
    import pyloudnorm as pyln
    return pyln.Meter(SR).integrated_loudness(y.astype("float64"))


def master(y, target=TARGET_LUFS):
    y = np.stack([signal.sosfilt(signal.butter(2, 26, "high", fs=SR, output="sos"), y[:, c]) for c in range(2)], axis=1).astype("float32")
    for _ in range(3):
        cur = lufs(y)
        y = limiter(y * DB(target - cur))
    print(f"  master: integrated {lufs(y):.1f} LUFS, peak {20 * np.log10(float(np.max(np.abs(y)))):.1f} dBFS")
    return y


# ------------------------------------------------------------------------------------------------ la colonna sonora
def build(segments, pos, total, shots, beat=0.6):
    N = total + 3.5
    mus, sfx = Mix(N), Mix(N)                    # music bus (ducked by the guns) and effects bus
    wg, wb = Mix(N), Mix(N)                      # reverb sends: guns and hits / bells and pads
    P = {s.clip: (pos[s.clip], s.dur, s.ss) for s in segments}
    T0, T3, T4, TT = P["c05_monaco"][0], P["f01_cardinale"][0], P["h01_orda"][0], P["TITLE"][0]
    rng = np.random.default_rng(2026)

    def at(clip, local):
        st, dur, ss = P[clip]
        t = st + (local - ss)
        return t if st - 1e-6 <= t <= st + dur + 1e-6 else None

    def cut_times(a, b):
        return [pos[s.clip] for s in segments if a - 1e-6 <= pos[s.clip] < b - 1e-6 and s.act == "b"]

    # ---- act I: the building. Ambience under a minor drone, a lift bell, a few glass notes
    d = drone(TT + 0.3, notes=(55.0, 82.41, 110.0, 164.81), lfo=0.06, cut=430, gain=1.0)
    d *= env_db(len(d), [(0, -90), (1.2, -38), (5, -29), (T0 - 4.5, -25), (T0 - 0.2, -20), (T0, -27), (T3, -26), (T4, -22), (TT - 0.04, -22), (TT, -90)])
    mus.add(d, 0.0)
    mus.add_loop(lp(rms(load("vento_loop.wav"), -34), 900), 0.0, T0 - 0.2, 1.0, fin=2.5, fout=0.6)
    org = lp(rms(load("organo_loop.wav"), -31), 2600)
    mus.add_loop(org, 0.3, P["t02_platea"][0] + 0.2, 1.0, fin=2.2, fout=1.2)
    mus.add_loop(org, P["c04_presbiterio"][0] - 0.3, T0 - 0.1, 1.0, fin=1.0, fout=0.1)
    mus.add_loop(rms(load("teatro_orchestra_loop.wav"), -35), P["t02_platea"][0] - 0.1, P["s01_operatoria"][0] + 0.4, 1.0, fin=0.9, fout=0.8)
    mus.add_loop(rms(load("teatro_brusio_loop.wav"), -37), P["t02_platea"][0] - 0.1, P["s01_operatoria"][0] + 0.2, 1.0, fin=0.9, fout=0.7)
    mus.add_loop(rms(load("sanatorio_monitor.wav"), -35), P["s01_operatoria"][0] - 0.2, P["c04_presbiterio"][0] + 0.2, 1.0, fin=0.6, fout=0.6)
    sfx.add(pk(load("ascensore_ding.wav"), -12), 0.02, 1.0, -0.1)
    sfx.add(pk(load("ascensore_porte.wav"), -12), 0.42, 1.0, 0.0)
    for t, f in ((2.9, 659.3), (6.4, 440.0), (9.7, 523.3), (12.5, 659.3), (15.4, 392.0), (18.1, 440.0), (21.2, 523.3)):
        wb.add(pk(bell(f, 4.5), -25), t, 1.0, rng.uniform(-0.5, 0.5))
    wb.add(pk(load("rintocco.wav")[:int(5.5 * SR)], -17), P["c04_presbiterio"][0] + 0.15, 1.0, 0.0)
    # the build to the first monster: a riser, a heartbeat that quickens, then silence before the hit
    mus.add(pk(riser(4.4, 260, 7000), -20), T0 - 4.4, 1.0)
    for dt in (3.6, 2.7, 1.9, 1.2, 0.6):
        mus.add(pk(heartbeat(), -14), T0 - dt, 1.0)
    mus.duck(T0 - 0.14, T0, depth=0.10, attack=0.08, release=0.0)
    hit = stack(boom(2.2, 34, 105, 0.85) * 0.9, braam(2.6) * 0.42)
    crash = hp(noise(1.8, 4), 2200) * np.exp(-tt(1.8) / 0.55).astype("float32") * 0.10
    mus.add(pk(hit, -3.5), T0, 1.0)
    mus.add(crash, T0, 1.0)
    wg.add(pk(hit, -12), T0, 1.0)

    # ---- act II: the residents. A pulse on every beat; each cut brings the sound of whoever appears
    beats2 = [T0 + k * beat for k in range(int(round((T3 - T0) / beat)))]
    for k, tb in enumerate(beats2):
        g = -22 + 10 * k / len(beats2)
        mus.add(pk(kick(0.45, 120, 44), g), tb, 1.0)
        if k >= 4:
            mus.add(pk(hat(), -34), tb + beat / 2, 1.0, 0.3)
    for tcut in cut_times(T0 + 0.1, T3):
        if tcut > T0 + 0.1:
            mus.add(pk(boom(0.6, 40, 90, 0.18), -13), tcut, 1.0)
            sfx.add(pk(whoosh(0.35, rising=False), -25), tcut - 0.05, 1.0)
    sfx.add_loop(rms(load("litania_monaco_loop.wav"), -29), T0 + 0.08, T0 + 2.5, 1.0, fin=0.1, fout=0.5)
    c6 = P["c06_occhio"][0]
    sfx.add(pk(load("occhio_fissa.wav"), -14), c6 + 0.05, 1.0, 0.0)
    sfx.add_loop(rms(load("occhio_loop.wav"), -33), c6 + 0.3, c6 + 2.4, 1.0, fin=0.3, fout=0.3)
    c7 = P["t03_maschera"][0]
    sfx.add(pk(load("teatro_campanello.wav")[:int(2.6 * SR)], -19), c7 + 0.1, 1.0, 0.0)
    sfx.add_loop(rms(load("teatro_brusio_loop.wav"), -34), c7, c7 + 2.4, 1.0, fin=0.2, fout=0.3)
    c8 = P["s04_primario"][0]
    for dt, g in ((0.55, -14), (1.45, -15), (2.05, -16)):
        sfx.add(pk(load("bruto_passo.wav"), g), c8 + dt, 1.0, 0.0)
    sfx.add_loop(rms(load("sanatorio_monitor.wav"), -33), c8, c8 + 2.4, 1.0, fin=0.2, fout=0.3)
    c9 = P["c07_abate"][0]
    sfx.add(pk(load("bastone_a_terra.wav"), -12), c9 + 0.15, 1.0, 0.0)
    for dt, g in ((1.25, -18), (2.0, -19)):
        sfx.add(pk(load("bastone_passo.wav"), g), c9 + dt, 1.0, 0.0)
    p = pad((220.0, 261.63, 329.63), T4 - T0 + 0.3, cut=1000)
    p *= env_db(len(p), [(0, -80), (1.5, -40), (T3 - T0, -33), (T4 - T0, -30), (T4 - T0 + 0.25, -80)])
    mus.add(p, T0 + 0.0)

    # ---- act III: the arsenal. Full pulse, bass on the eighths, snare on 2 and 4; the guns are the real samples
    roots = [55.0, 43.65, 49.0, 55.0]
    beats3 = [T3 + k * beat for k in range(int(round((T4 - T3) / beat)))]
    for k, tb in enumerate(beats3):
        bar, bt = divmod(k, 4)
        mus.add(pk(kick(0.45, 128, 44), -9 if bt == 0 else -11.5), tb, 1.0)
        if bt in (1, 3):
            mus.add(pk(snare(), -17), tb, 1.0, 0.1)
        mus.add(pk(hat(), -33), tb + beat / 2, 1.0, 0.3)
        f = roots[bar % 4]
        mus.add(pk(bass_note(f, 0.5, 0.2), -15), tb, 1.0)
        mus.add(pk(bass_note(f * (1.5 if bt == 3 else 1.0), 0.3, 0.12), -19), tb + beat / 2, 1.0)
    mus.add(pk(braam(2.2, 43.65), -14), T3, 1.0)
    for tcut in cut_times(T3 + 0.1, T4):
        mus.add(pk(boom(0.6, 40, 95, 0.2), -11), tcut, 1.0)
        sfx.add(pk(whoosh(0.3, rising=False), -23), tcut - 0.04, 1.0)
    # guns
    gun_times = []
    for clip in ("f01_cardinale", "f02_breacher", "f03_tarantola", "f04_lance", "f05_campana", "f07_pupilla", "f06_apocalisse"):
        sh = shots[clip]
        kind = GUN_KIND[int(sh["equip"])]
        raw = beam() if kind == "beam" else lp(pk(load(f"armi/sparo_{kind}.wav"), -2.0), 9500, 2)
        for ev in sh["events"]:
            if ev["kind"] in ("fire", "burst"):
                for ts in shot_times(ev):
                    ta = at(clip, ts)
                    if ta is None:
                        continue
                    x = pitch(raw, 1.0 + rng.uniform(-0.035, 0.035)) * rng.uniform(0.82, 1.0)
                    if kind in ("smg", "lmg"):
                        x = x * 0.78
                    sfx.add(x, ta, 1.0, 0.12)
                    wg.add(x, ta, 0.55, 0.0)
                    low = pk(boom(0.5, 38, 70, 0.1), -6) * (0.55 if kind in ("smg", "lmg") else 1.0)
                    sfx.add(low, ta, 1.0, 0.0)
                    gun_times.append(ta)
            elif ev["kind"] == "kill":
                ta = at(clip, ev["t"])
                if ta is None:
                    continue
                typ = sh["enemies"][ev["i"]]["type"]
                sfx.add(pk(load("colpito.wav"), -12), ta, 1.0, 0.0)
                sfx.add(splat(seed=int(ta * 100) % 997) * DB(-14), ta + 0.02, 1.0, rng.uniform(-0.2, 0.2))
                if typ in ("Monaco", "Abate", "Maschera", "Infermiere", "Primario"):
                    sfx.add(pk(load("monaco_cade.wav"), -17), ta + 0.28, 1.0, rng.uniform(-0.2, 0.2))
    # slow motion: an inhale before, a snap after, the world drops out under the shot
    for clip, t0, t1 in (("f02_breacher", 1.3, 2.1), ("f06_apocalisse", 4.4, 5.4)):
        a, b = at(clip, t0), at(clip, t1)
        if a is None:
            continue
        sfx.add(pk(whoosh(0.5, rising=True), -20), a - 0.35, 1.0, 0.0)
        mus.add(pk(boom(1.2, 34, 60, 0.5), -9), a + 0.02, 1.0, 0.0)
        wg.add(pk(pitch(lp(load("armi/sparo_shotgun.wav"), 3000), 0.55), -9), a + 0.03, 1.0, 0.0)
        if b is not None and b <= T4:
            sfx.add(pk(whoosh(0.4, rising=True), -22), b - 0.35, 1.0, 0.0)
            mus.add(pk(kick(0.5, 140, 44), -8), b, 1.0, 0.0)
    # the scope: a thin rising tone while the sight is up, then the shot rings in the dark
    a, b = at("f04_lance", 0.3), at("f04_lance", 1.8)
    if a is not None and b is not None:
        tone = np.sin(2 * np.pi * np.cumsum(700 + 1500 * (tt(b - a) / (b - a)) ** 2) / SR).astype("float32") * (tt(b - a) / (b - a)) ** 2
        sfx.add(pk(tone, -30), a, 1.0, 0.0)
    # ---- act IV: the horde. The same pulse, stabs on the bars, the monks' chant, a riser to the cut
    beats4 = [T4 + k * beat for k in range(int(round((TT - T4) / beat)))]
    for k, tb in enumerate(beats4):
        bar, bt = divmod(k, 4)
        mus.add(pk(kick(0.45, 130, 44), -8 if bt % 2 == 0 else -10.5), tb, 1.0)
        if bt in (1, 3):
            mus.add(pk(snare(), -15), tb, 1.0, 0.1)
        mus.add(pk(hat(), -31), tb + beat / 2, 1.0, 0.3)
        mus.add(pk(bass_note(roots[(bar + 2) % 4], 0.5, 0.22), -13), tb, 1.0)
        if bt == 0:
            mus.add(pk(braam(1.7, roots[(bar + 2) % 4] * 0.8), -13), tb, 1.0)
    mus.add(pk(riser(TT - T4 + 0.0, 300, 8000), -19), T4, 1.0)
    sfx.add_loop(rms(load("litania_monaco_loop.wav"), -26), T4, TT - 0.05, 1.0, fin=0.4, fout=0.05)
    mus.add_loop(rms(rumble(8.0), -26), T4, TT - 0.05, 1.0, fin=1.5, fout=0.05)

    # ---- the title: silence, an inhale while the eye opens, the name on a low boom and a bell, a whisper under the line
    # a hard stop of everything before the title (a few ms of fade); what is added from here on is the title's own
    mus.duck(TT - 0.01, TT + 0.45, depth=0.0, attack=0.01, release=0.0)
    sfx.duck(TT - 0.01, TT + 0.45, depth=0.0, attack=0.01, release=0.0)
    wg.duck(TT - 0.01, TT + 0.45, depth=0.0, attack=0.01, release=0.0)
    inh = hp(noise(1.5, 10), 300) * (tt(1.5) / 1.5) ** 2.2
    mus.add(pk(inh, -34), TT + 0.5, 1.0)
    mus.add(pk(np.sin(2 * np.pi * 55 * tt(2.2)).astype("float32") * (tt(2.2) / 2.2) ** 1.6, -26), TT + 0.1, 1.0)
    a = TT + 1.95
    mus.add(pk(boom(3.4, 34, 85, 1.1), -7), a, 1.0)
    wb.add(pk(load("rintocco.wav"), -13), a, 1.0, 0.0)
    pt = pad((110.0, 164.81, 220.0, 261.63), total - a + 1.5, cut=1500)
    pt *= env_db(len(pt), [(0, -80), (0.15, -27), (2.4, -26), (total - a - 0.1, -34), (total - a + 0.6, -80)])
    mus.add(pt, a)
    for dt, f, g, pan in ((3.35, 659.3, -26, -0.2), (3.85, 523.3, -27, 0.1), (4.4, 440.0, -25, 0.0)):       # E - C - A, the line spoken in bells
        wb.add(pk(bell(f, 5.0), g), TT + dt, 1.0, pan)
    sfx.add(pk(load("sussurro.wav"), -27), TT + 3.4, 1.0, 0.0)
    sfx.add_loop(lp(rms(load("vento_loop.wav"), -40), 900), TT + 0.05, total + 0.1, 1.0, fin=1.2, fout=1.0)       # room tone: never dead silence

    # ---- bus mix: the guns make the music duck, the reverb sends are added, then loudness and limiter
    duck = np.ones(mus.n, "float32")
    for ts in gun_times:
        i0 = int(ts * SR)
        L = int(0.40 * SR)
        if i0 < mus.n:
            dip = 1 - 0.50 * np.exp(-np.arange(min(L, mus.n - i0)) / (0.13 * SR))
            duck[i0:i0 + len(dip)] = np.minimum(duck[i0:i0 + len(dip)], dip.astype("float32"))
    mus.buf *= duck
    rev_g = send(wg, 1.9, 0.45, 31)
    rev_b = send(wb, 4.2, 0.55, 53)
    wbdry = wb.buf
    y = (mus.buf + sfx.buf + rev_g + rev_b + wbdry * 0.55).T
    y = y[: int((total + 0.05) * SR)]
    # fade of the last second (the picture fades too)
    nf = int(0.9 * SR)
    y[-nf:] *= np.linspace(1, 0, nf, dtype="float32")[:, None]
    return master(y)


def main():
    import json
    import make_trailer as MT
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("unity_logs/trailer/work/score.wav")
    shots = MT.Takes(MT.DEFAULT_TAKES.split(",")).shots(MT.SEGMENTS)
    pos, total = MT.layout(MT.SEGMENTS)
    y = build(MT.SEGMENTS, pos, total, shots, MT.BEAT)
    import soundfile as sf
    out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(out), y, SR, subtype="PCM_24")
    print("score", out, f"{len(y) / SR:.2f} s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
