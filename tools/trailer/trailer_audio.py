#!/usr/bin/env python3
"""Sound for the trailer: the game's own samples (hotel_build/audio/*.wav) plus a few synthesised pieces (drone, riser, boom, braam),
placed on a timeline and mixed to stereo. numpy / scipy / soundfile only. Nothing here knows the edit: make_trailer.py calls it.

The player cannot capture audio in batchmode, so every sound of the film is placed here, from the times in the shot list.
"""
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

SR = 48000
AUDIO = Path(__file__).resolve().parents[2] / "hotel_build" / "audio"
_cache = {}


def load(name):
    """A game sample as mono float32 at SR (cached)."""
    if name not in _cache:
        x, sr = sf.read(str(AUDIO / name), dtype="float32", always_2d=True)
        x = x.mean(axis=1)
        if sr != SR:
            g = int(np.gcd(sr, SR))
            x = signal.resample_poly(x, SR // g, sr // g).astype("float32")
        _cache[name] = x
    return _cache[name]


def pitch(x, f):
    """Speed and pitch by f (> 1 higher / faster), linear interpolation: good enough for effects."""
    if abs(f - 1.0) < 1e-3:
        return x
    n = max(2, int(len(x) / f))
    return np.interp(np.linspace(0, len(x) - 1, n), np.arange(len(x)), x).astype("float32")


def lp(x, fc, order=4):
    return signal.sosfilt(signal.butter(order, fc, "low", fs=SR, output="sos"), x).astype("float32")


def hp(x, fc, order=4):
    return signal.sosfilt(signal.butter(order, fc, "high", fs=SR, output="sos"), x).astype("float32")


def bp(x, lo, hi, order=3):
    return signal.sosfilt(signal.butter(order, [lo, hi], "band", fs=SR, output="sos"), x).astype("float32")


def fade(x, fin=0.0, fout=0.0):
    x = x.copy()
    a, b = int(fin * SR), int(fout * SR)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a, dtype="float32")
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b, dtype="float32")
    return x


def reverb(x, rt=2.4, wet=0.35, seed=3, tail=True):
    """Noise-tail reverb (exponential decay, darkened): returns dry + wet, longer by the tail when tail=True."""
    rng = np.random.default_rng(seed)
    n = int(rt * SR)
    t = np.arange(n) / SR
    ir = lp(rng.standard_normal(n).astype("float32") * np.exp(-6.9 * t / rt).astype("float32"), 4500)
    k = int(0.015 * SR)
    ir[:k] *= np.linspace(0, 1, k, dtype="float32")
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    w = signal.fftconvolve(x, ir).astype("float32")
    out = np.zeros(len(w) if tail else len(x), "float32")
    out[:len(x)] += x
    m = min(len(out), len(w))
    out[:m] += wet * w[:m]
    return out


class Mix:
    """Stereo bus of fixed length. Everything is placed by time in seconds."""

    def __init__(self, dur):
        self.n = int(dur * SR)
        self.buf = np.zeros((2, self.n), "float32")

    def add(self, x, t, gain=1.0, pan=0.0):
        i0 = int(round(t * SR))
        if i0 >= self.n or i0 + len(x) <= 0:
            return
        if i0 < 0:
            x = x[-i0:]
            i0 = 0
        n = min(len(x), self.n - i0)
        th = (pan + 1.0) * np.pi / 4.0
        self.buf[0, i0:i0 + n] += x[:n] * gain * np.cos(th)
        self.buf[1, i0:i0 + n] += x[:n] * gain * np.sin(th)

    def add_loop(self, x, t0, t1, gain=1.0, fin=0.5, fout=0.5, pan=0.0):
        n = int((t1 - t0) * SR)
        if n <= 0:
            return
        y = np.tile(x, int(np.ceil(n / len(x))))[:n]
        self.add(fade(y, fin, fout), t0, gain, pan)

    def add_stereo(self, left, right, t, gain=1.0):
        i0 = int(round(t * SR))
        n = min(len(left), self.n - i0)
        if n > 0 and i0 >= 0:
            self.buf[0, i0:i0 + n] += left[:n] * gain
            self.buf[1, i0:i0 + n] += right[:n] * gain

    def duck(self, t0, t1, depth=0.5, attack=0.05, release=0.4):
        """Lowers everything already placed between t0 and t1 (the silence before a hit)."""
        env = np.ones(self.n, "float32")
        a, b = int(t0 * SR), int(t1 * SR)
        a0 = max(0, a - int(attack * SR))
        env[a0:a] = np.linspace(1, depth, a - a0, dtype="float32")
        env[a:b] = depth
        b1 = min(self.n, b + int(release * SR))
        env[b:b1] = np.linspace(depth, 1, b1 - b, dtype="float32")
        self.buf *= env

    def finish(self, peak=0.89):
        """Soft clip and scale to a peak; returns (n, 2) float32."""
        y = np.tanh(self.buf * 0.9) / np.tanh(0.9)
        m = float(np.max(np.abs(y))) + 1e-9
        return (y * min(1.0, peak / m)).T.astype("float32")


# ------------------------------------------------------------------------------------------------ sintesi
def saw_stack(f, dur, cents=(-9, -4, 0, 5, 10), seed=0):
    t = np.arange(int(dur * SR)) / SR
    rng = np.random.default_rng(seed)
    y = np.zeros_like(t)
    for c in cents:
        ff = f * 2 ** (c / 1200.0)
        y += 2 * ((t * ff + rng.random()) % 1.0) - 1
    return (y / len(cents)).astype("float32")


def drone(dur, notes=(55.0, 82.41, 130.81), lfo=0.11, cut=520, gain=1.0):
    """Dark pad: low detuned saws (A minor-ish), slow swell."""
    y = sum(saw_stack(f, dur, seed=i) * (1.0 / (1 + i * 0.5)) for i, f in enumerate(notes))
    y = lp(y, cut)
    t = np.arange(len(y)) / SR
    y *= (0.65 + 0.35 * np.sin(2 * np.pi * lfo * t + 1.3)).astype("float32")
    return (y * gain / max(1e-6, float(np.max(np.abs(y))))).astype("float32")


def boom(dur=1.5, f0=34.0, f1=95.0, tau=0.5):
    t = np.arange(int(dur * SR)) / SR
    f = f0 + (f1 - f0) * np.exp(-t / 0.11)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) * (1 - np.exp(-t / 0.004)) * np.exp(-t / tau)
    rng = np.random.default_rng(7)
    y += lp(rng.standard_normal(len(t)).astype("float32"), 220) * np.exp(-t / 0.12) * 0.5
    return y.astype("float32")


def braam(dur=2.3, f=43.65):
    """Low brass-like blast: stacked saws, filter closing from bright to dark, saturated."""
    base = saw_stack(f, dur, seed=1) + 0.7 * saw_stack(f * 2, dur, seed=2) + 0.5 * saw_stack(f * 3.0, dur, seed=3)
    t = np.arange(len(base)) / SR
    w = np.clip(1 - t / 0.9, 0, 1).astype("float32")
    y = lp(base, 240) * (1 - w) + lp(base, 2200) * w
    env = ((1 - np.exp(-t / 0.02)) * np.exp(-t / 1.0)).astype("float32")
    y = np.tanh(2.4 * y * env)
    return (y / (float(np.max(np.abs(y))) + 1e-9)).astype("float32")


def riser(dur, f0=300.0, f1=7500.0):
    rng = np.random.default_rng(11)
    n = int(dur * SR)
    t = np.arange(n) / SR
    noise = rng.standard_normal(n).astype("float32")
    chunks = 24
    y = np.zeros(n, "float32")
    edges = np.linspace(0, n, chunks + 1, dtype=int)
    for i in range(chunks):
        fc = f0 * (f1 / f0) ** ((i + 0.5) / chunks)
        seg = noise[edges[i]:edges[i + 1]]
        y[edges[i]:edges[i + 1]] = bp(seg, fc * 0.6, min(fc * 1.6, SR / 2 - 100), 2) if len(seg) > 64 else seg
    env = (t / dur) ** 2.2
    tone = np.sin(2 * np.pi * np.cumsum(90 * (6.0 ** (t / dur))) / SR).astype("float32") * 0.25
    out = (y * 4.0 + tone) * env
    return (out / (float(np.max(np.abs(out))) + 1e-9)).astype("float32")


def heartbeat(gain=1.0):
    """Lub-dub: two short sub thumps."""
    a = boom(0.4, 38.0, 70.0, 0.12)
    b = boom(0.4, 36.0, 62.0, 0.10) * 0.7
    y = np.zeros(int(0.6 * SR), "float32")
    y[:len(a)] += a
    k = int(0.27 * SR)
    y[k:k + len(b)] += b[:len(y) - k]
    return y * gain


def whoosh(dur=0.7, rising=True):
    rng = np.random.default_rng(5)
    n = int(dur * SR)
    t = np.arange(n) / SR
    y = bp(rng.standard_normal(n).astype("float32"), 400, 5000, 2)
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 2
    if not rising:
        env = env[::-1]
    return (y * env * 1.5).astype("float32")
