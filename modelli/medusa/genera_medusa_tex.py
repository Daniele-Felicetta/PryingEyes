"""Texture della medusa volante (rifacimento 2026-11-17), tutte generate in Python/numpy come quelle della hall e dell'occhio.

Scrive in Assets/_PryingEyes/Textures/Medusa, per ogni materiale  <nome>_base.png (RGBA, alfa = opacita' dove serve),
<nome>_normal.png (OpenGL: verde = su) e <nome>_mask.png (R metallicita', G occlusione, A lucidita'); gel, interno e luce hanno
anche <nome>_emiss.png.

Mappature (devono coincidere con genera_medusa.py):
  gel, cappa, interno   UV a disco: centro = polo della campana, raggio 0.5 = bordo; angolo = angolo attorno all'asse
  carne, luce           ripetibili, 0.2 m per ripetizione (tubi: u attorno, v lungo)
  velo                  nastri e drappi: u = 0 attacco pieno -> 1 bordo libero a merletto; v ripetibile lungo (un tile = la larghezza)
  dente                 u attorno, v dalla radice (0) alla punta (1)
holes.json (scritto da genera_medusa.py -- build) da' i buchi, le costole e gli organi della campana in coordinate UV.

uso: python -I genera_medusa_tex.py [cartella_uscita] [--anteprima file.png]
"""
import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "hotel"))
import genera_texture as T  # noqa: E402  (perlin, fbm, warped, voronoi, blur, normals, smooth)

OUT = HERE.parents[1] / "unity" / "PryingEyes" / "Assets" / "_PryingEyes" / "Textures" / "Medusa"
F = np.float32
LIME = np.array([0.56, 1.00, 0.10], F)


# ---------------------------------------------------------------- utilita'
def smooth(a, b, x):
    return T.smooth(a, b, x)


def rgb(*c):
    return np.array(c, F)


def mix(a, b, t):
    t = np.asarray(t, F)
    return a + (b - a) * (t[..., None] if t.ndim == 2 else t)


def disc(n):
    """Coordinate di un'immagine a disco: px, py in UV centrate (-0.5..0.5, py verso l'alto), rho (0..0.5+), psi."""
    yy, xx = np.mgrid[0:n, 0:n].astype(F)
    px = (xx + 0.5) / n - 0.5
    py = 0.5 - (yy + 0.5) / n
    return px, py, np.hypot(px, py), np.arctan2(py, px)


def draw_line(canvas, pts, width, value=1.0, falloff=1.6):
    """Linea morbida lungo una poligonale in UV (0..1, v verso l'alto), spessore in UV."""
    n = canvas.shape[0]
    for (u0, v0), (u1, v1) in zip(pts, pts[1:]):
        x0, y0, x1, y1 = u0 * n, (1 - v0) * n, u1 * n, (1 - v1) * n
        wp = max(width * n, 0.8)
        xa, xb = int(max(0, min(x0, x1) - wp * 2 - 2)), int(min(n - 1, max(x0, x1) + wp * 2 + 2))
        ya, yb = int(max(0, min(y0, y1) - wp * 2 - 2)), int(min(n - 1, max(y0, y1) + wp * 2 + 2))
        if xb <= xa or yb <= ya:
            continue
        ys, xs = np.mgrid[ya:yb + 1, xa:xb + 1].astype(F)
        dx, dy = x1 - x0, y1 - y0
        l2 = dx * dx + dy * dy + 1e-9
        t = np.clip(((xs - x0) * dx + (ys - y0) * dy) / l2, 0, 1)
        d = np.hypot(xs - (x0 + t * dx), ys - (y0 + t * dy))
        v = np.clip(1 - d / wp, 0, 1) ** falloff * value
        sub = canvas[ya:yb + 1, xa:xb + 1]
        np.maximum(sub, v, out=sub)


def to_uv(rho, psi):
    return (0.5 + rho * math.cos(psi), 0.5 + rho * math.sin(psi))


def save_rgba(path, arr, alpha=None):
    a = np.clip(arr, 0, 1)
    if alpha is not None:
        a = np.concatenate([a, np.clip(alpha, 0, 1)[..., None]], -1)
    img = Image.fromarray((a * 255 + 0.5).astype(np.uint8))
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True, compress_level=9)


def resize(arr, n):
    if arr.shape[0] == n:
        return arr
    ch = arr.shape[2]
    out = np.stack([np.asarray(Image.fromarray((np.clip(arr[..., c], 0, 1) * 255).astype(np.uint8)).resize((n, n), Image.LANCZOS), F) / 255.0 for c in range(ch)], -1)
    return out.astype(F)


def write_set(name, base, alpha, height, metal, occ, gloss, bump, emiss=None, size_big=None, size_small=1024, emiss_size=512):
    """Salva base (RGB + alfa), normal map dall'altezza e maschera; le mappe secondarie a risoluzione ridotta."""
    big = size_big or base.shape[0]
    save_rgba(OUT / f"{name}_base.png", resize(base, big), resize(alpha[..., None], big)[..., 0] if alpha is not None else None)
    nrm = T.normals(height, bump)
    save_rgba(OUT / f"{name}_normal.png", resize(nrm, size_small))
    mask = resize(np.stack([metal, occ, np.zeros_like(metal)], -1), size_small)
    save_rgba(OUT / f"{name}_mask.png", mask, resize(gloss[..., None], size_small)[..., 0])
    if emiss is not None:
        save_rgba(OUT / f"{name}_emiss.png", resize(emiss, emiss_size))


def cell_values(ident, cells, rng):
    return rng.random(cells * cells).astype(F)[ident]


def warp_xy(n, rng, amount, base=5):
    """Coordinate pixel deformate da due fbm (domain warp): le pareti delle celle diventano ondulate e organiche."""
    yy, xx = np.mgrid[0:n, 0:n].astype(F)
    wx = (T.fbm(n, base, 4, rng) - 0.5) * 2 * amount * n
    wy = (T.fbm(n, base, 4, rng) - 0.5) * 2 * amount * n
    return xx + wx, yy + wy


def lace(n, cells, rng, hole_p, strand_w, warp=0.03, rnd_r=7, bare=0.85):
    """Merletto a celle di Voronoi con pareti ondulate: ritorna (filo 0..1, vuoto 0..1).
    hole_p: probabilita' di cella vuota (campo 0..1). I fili spariscono dove i vuoti sono fitti (strappi senza rete)."""
    d1, d2, idn = T.voronoi(n, cells, rng)
    cv = cell_values(idn, cells, rng)
    xy = warp_xy(n, rng, warp)
    edge = T.sample(d2 - d1, *xy)
    hole = (T.sample(cv, *xy) < hole_p).astype(F)
    hole = smooth(0.30, 0.70, T.blur(hole, rnd_r))                         # vuoti tondeggianti
    sw = strand_w * (0.7 + 0.6 * T.fbm(n, 6, 3, rng))
    strand = (1 - smooth(sw * 0.55, sw * 1.35, edge)) * (1 - smooth(bare - 0.3, bare, T.blur(hole, rnd_r * 2)))
    return strand, hole


# ---------------------------------------------------------------- carne (funi, ripetibile)
def carne(n=1024):
    rng = np.random.default_rng(31)
    m1, m2, m3 = T.fbm(n, 4, 5, rng), T.fbm(n, 11, 4, rng), T.fbm(n, 24, 3, rng)
    maroon, flesh, cream = rgb(0.26, 0.04, 0.045), rgb(0.52, 0.22, 0.19), rgb(0.80, 0.62, 0.50)
    col = mix(mix(maroon, flesh, smooth(0.15, 0.65, m1)), cream, smooth(0.50, 0.85, m2 * 0.7 + m1 * 0.3) * 0.45)
    # pori di spugna: tre scale di celle di Voronoi con raggio variabile per cella, a chiazze (non a pois regolari)
    patch = smooth(0.40, 0.62, T.fbm(n, 3, 4, rng))
    xy = warp_xy(n, rng, 0.02, 6)
    cav = np.zeros((n, n), F)
    wall = np.zeros((n, n), F)
    for cells, thr, rmax, k in ((7, 0.30, 0.46, 1.0), (17, 0.42, 0.36, 0.9), (43, 0.50, 0.32, 0.7)):
        d1, d2, idn = T.voronoi(n, cells, rng)
        cvv = cell_values(idn, cells, rng)
        rad = rmax * (0.30 + 0.70 * cvv)
        pore = smooth(rad, rad * 0.40, T.sample(d1, *xy)) * (cvv > thr) * (0.25 + 0.75 * patch) * k
        cav = np.maximum(cav, pore)
        wall = np.maximum(wall, smooth(0.0, 0.22, d2 - d1))
    col = mix(col, rgb(0.16, 0.025, 0.03), cav * 0.85)
    # vene sottili in rilievo, piu' scure
    vein = 1 - smooth(0.0, 0.07, np.abs(T.fbm(n, 5, 4, rng) - 0.5) * 2)
    col = mix(col, rgb(0.38, 0.06, 0.08), vein * 0.65 * (1 - cav))
    # bagnato: un velo di muco chiaro sui punti alti
    height = 0.60 + 0.22 * m2 + 0.12 * m3 - 0.55 * cav + 0.10 * wall * (1 - cav) + 0.05 * vein
    gloss = 0.78 - 0.45 * cav + 0.10 * m3
    occ = 1 - 0.75 * cav
    write_set("carne", col.astype(F), None, height.astype(F), np.zeros((n, n), F), occ.astype(F), np.clip(gloss, 0.1, 0.95).astype(F), 3.6)


# ---------------------------------------------------------------- velo (nastri e drappi a merletto)
def velo(n=1024):
    rng = np.random.default_rng(47)
    yy, xx = np.mgrid[0:n, 0:n].astype(F)
    u = (xx + 0.5) / n                     # 0 = attacco, 1 = bordo libero
    m1 = T.fbm(n, 4, 5, rng)
    hole_p = 0.05 + 0.80 * smooth(0.18, 0.95, u) ** 1.2 + 0.12 * (T.fbm(n, 3, 3, rng) - 0.5)
    strand, hole = lace(n, 9, rng, hole_p, 0.11, warp=0.035)
    membrane = (1 - hole) * (0.30 + 0.12 * m1)
    solid = 1 - smooth(0.07, 0.12, u)                         # fascia di attacco piena (anche le gocce, u = 0.04)
    rag = smooth(0.80, 1.00, u) * (T.fbm(n, 9, 4, rng) > 0.46 - 0.18 * (u - 0.8))   # bordo libero sfilacciato
    alpha = np.maximum(np.maximum(strand * 0.92, membrane), solid)
    alpha *= 1 - 0.95 * rag
    cream, rose, maroon = rgb(0.80, 0.66, 0.52), rgb(0.74, 0.42, 0.36), rgb(0.55, 0.15, 0.14)
    col = mix(mix(cream, rose, smooth(0.2, 0.8, m1)), maroon, np.clip(strand * 0.6 + solid * 0.35, 0, 1))
    col = col * (0.88 + 0.22 * T.fbm(n, 14, 3, rng))[..., None]
    height = 0.5 + 0.35 * strand + 0.12 * m1 - 0.25 * hole
    gloss = 0.60 + 0.25 * strand
    write_set("velo", col.astype(F), alpha.astype(F), height.astype(F), np.zeros((n, n), F), np.ones((n, n), F) * 0.9, gloss.astype(F), 2.4)


# ---------------------------------------------------------------- dente
def dente(n=256, m=512):
    rng = np.random.default_rng(53)
    yy, xx = np.mgrid[0:m, 0:n].astype(F)
    u = (xx + 0.5) / n
    v = 1 - (yy + 0.5) / m                 # 0 = radice, 1 = punta
    stripes = 0.5 + 0.5 * np.sin(u * 2 * np.pi * 14 + 2.5 * np.sin(v * 7))
    grain = T.fbm(max(n, m), 12, 4, rng)[:m, :n]
    ivory, amber, gum = rgb(0.90, 0.85, 0.66), rgb(0.95, 0.74, 0.34), rgb(0.50, 0.14, 0.14)
    col = mix(ivory * (0.90 + 0.12 * stripes[..., None]), amber, smooth(0.62, 1.0, v) * 0.75)
    col = mix(col, gum, smooth(0.20, 0.0, v))
    col *= (0.92 + 0.16 * grain)[..., None]
    height = 0.5 + 0.18 * stripes + 0.15 * grain
    gloss = 0.55 + 0.30 * smooth(0.5, 1.0, v)
    # la mappa e' rettangolare: normali e maschera con lo stesso formato
    base = col.astype(F)
    nrm = T.normals(height.astype(F), 2.4)
    OUT.mkdir(parents=True, exist_ok=True)
    save_rgba(OUT / "dente_base.png", base)
    save_rgba(OUT / "dente_normal.png", nrm)
    save_rgba(OUT / "dente_mask.png", np.stack([np.zeros_like(v), np.ones_like(v) * 0.9, np.zeros_like(v)], -1), gloss.astype(F))


# ---------------------------------------------------------------- luce (organi e noduli)
def luce(n=512):
    rng = np.random.default_rng(61)
    m1, m2 = T.fbm(n, 5, 4, rng), T.fbm(n, 13, 3, rng)
    d1, d2, idn = T.voronoi(n, 9, rng)
    cells = smooth(0.55, 0.05, d1)
    core = np.clip(0.55 + 0.55 * m1 - 0.30 * (1 - cells), 0, 1.2)
    glow = np.clip(core * (0.75 + 0.5 * m2), 0, 1)
    base = mix(rgb(0.04, 0.10, 0.015), rgb(0.35, 0.60, 0.08), glow)
    emiss = LIME * (0.30 + 0.95 * glow)[..., None] + rgb(0.35, 0.20, 0.0) * (smooth(0.8, 1.0, m2) * 0.4)[..., None]
    height = 0.5 + 0.25 * m2 - 0.15 * cells
    write_set("luce", base.astype(F), None, height.astype(F), np.zeros((n, n), F), np.ones((n, n), F), (0.55 + 0.25 * glow).astype(F), 1.6,
              emiss=np.clip(emiss, 0, 1).astype(F), size_small=512, emiss_size=512)


# ---------------------------------------------------------------- campana: gel, cappa, interno (UV a disco)
def load_holes():
    p = HERE / "holes.json"
    if p.exists():
        return json.loads(p.read_text())
    return {"holes": [], "ribs": [], "organs": [], "ring": [], "meters_per_uv": 1.6}


def organ_glow(n, data, spread, strength=1.0):
    """Bagliore degli organi (lobi, anello, bulbo) proiettato sul disco, sfocato: la luce che traspare attraverso il gel."""
    canvas = np.zeros((n, n), F)
    for lobe in data.get("organs", []):
        pts = [to_uv(r, p) for r, p in lobe]
        draw_line(canvas, pts, 0.030 * n / n, 1.0, 1.2)
    ring = [to_uv(r, p) for r, p in data.get("ring", [])]
    if ring:
        draw_line(canvas, ring, 0.010, 0.85, 1.2)
    px, py, rho, psi = disc(n)
    canvas = np.maximum(canvas, np.clip(1 - rho / (data.get("bulb", 0.03) * 1.8), 0, 1) ** 1.3)          # bulbo al polo
    return np.clip(T.blur(canvas, spread) * strength, 0, 1)


def gel(n=2048):
    rng = np.random.default_rng(71)
    data = load_holes()
    px, py, rho, psi = disc(n)
    rn = np.clip(rho / 0.5, 0, 1.2)
    big = 1024
    f1 = T.fbm(big, 4, 5, rng)
    f2 = T.fbm(big, 9, 4, rng)
    f3 = T.fbm(big, 22, 3, rng)
    f1, f2, f3 = (resize(a[..., None], n)[..., 0] for a in (f1, f2, f3))
    amber, cream, rose, maroon = rgb(0.76, 0.64, 0.44), rgb(0.82, 0.74, 0.58), rgb(0.76, 0.50, 0.42), rgb(0.36, 0.05, 0.07)
    col = mix(mix(amber, cream, smooth(0.3, 0.8, f1)), rose, smooth(0.45, 1.0, rn) * 0.55 + smooth(0.6, 0.9, f2) * 0.25)
    # membrana: sottile, piu' densa dove il gel e' spesso (vicino al polo) e nei grumi
    alpha = 0.09 + 0.06 * (f2 - 0.5) + 0.12 * smooth(0.55, 0.0, rn) + 0.08 * smooth(0.60, 0.92, f1) - 0.04 * smooth(0.6, 1.0, rn)
    # rete di capillari (due scale) e costole lungo i meridiani
    web = np.zeros((n, n), F)
    for cells, w, k in ((13, 0.075, 0.9), (34, 0.10, 0.45)):
        d1, d2, idn = T.voronoi(n, cells, rng)
        web = np.maximum(web, (1 - smooth(w * 0.5, w * 1.5, T.sample(d2 - d1, *warp_xy(n, rng, 0.025)))) * k)
    web *= 0.6 + 0.4 * smooth(0.25, 0.9, f2)
    ribs = np.zeros((n, n), F)
    for line in data.get("ribs", []):
        draw_line(ribs, [to_uv(r, p) for r, p in line], 0.0085, 1.0, 1.1)
    ribs_soft = np.zeros((n, n), F)
    for line in data.get("ribs", []):
        draw_line(ribs_soft, [to_uv(r, p) for r, p in line], 0.022, 0.55, 1.4)
    # rime dei buchi: ispessimento carnoso e sanguigno attorno ad ogni foro
    rim = np.zeros((n, n), F)
    for h in data.get("holes", []):
        d = np.hypot(px + 0.5 - h["u"], py + 0.5 - h["v"]) * (0.75 + 0.5 * f2)
        ring = smooth(h["r"] * 2.6, h["r"] * 1.0, d) * smooth(h["r"] * 0.3, h["r"] * 0.9, d)
        rim = np.maximum(rim, ring)
    # macchie di sangue e lividi
    blot = smooth(0.70, 0.86, T.fbm(big, 7, 4, rng))
    blot = resize(blot[..., None], n)[..., 0] * (0.5 + 0.5 * smooth(0.0, 0.8, np.cos(psi - 2.6) * 0.5 + 0.5))
    dark = np.clip(web * 0.8 + ribs * 0.95 + rim * 0.65 + blot * 0.60, 0, 1)
    col = mix(col, maroon, dark)
    alpha = np.clip(alpha + web * 0.40 + ribs_soft * 0.20 + ribs * 0.65 + rim * 0.55 + blot * 0.28, 0, 1)
    # bordo: piu' spesso e arricciato, rosato
    edge = smooth(0.86, 1.0, rn)
    col = mix(col, rose * 0.85, edge * 0.5)
    alpha = np.clip(alpha + edge * 0.35, 0, 1)
    height = 0.55 + 0.18 * f2 + 0.10 * f3 + 0.30 * ribs + 0.12 * web + 0.22 * rim
    gloss = np.clip(0.88 - 0.10 * f3 - 0.12 * dark, 0.2, 0.95)
    glow = organ_glow(n, data, 24, 1.15) * smooth(0.0, 0.6, 1 - rn * 0.35)
    emiss = LIME[None, None, :] * glow[..., None] * (0.55 + 0.45 * f2[..., None])
    write_set("gel", col.astype(F), alpha.astype(F), resize(height[..., None], 1024)[..., 0].astype(F), np.zeros((1024, 1024), F),
              resize((1 - 0.4 * dark)[..., None], 1024)[..., 0].astype(F), resize(gloss[..., None], 1024)[..., 0].astype(F), 3.0,
              emiss=np.clip(emiss, 0, 1).astype(F), size_big=2048, size_small=1024, emiss_size=512)


def cappa(n=2048):
    rng = np.random.default_rng(83)
    data = load_holes()
    px, py, rho, psi = disc(n)
    rn = np.clip(rho / 0.5, 0, 1.2)
    f1, f2, f3 = T.fbm(n, 3, 5, rng), T.fbm(n, 8, 4, rng), T.fbm(n, 3, 3, rng)
    # celle di merletto grandi (circa 6 cm), ondulate; piu' vuote verso il bordo e sul lato sinistro
    side = 0.5 + 0.5 * np.cos(psi - 2.7)
    hole_p = 0.10 + 0.50 * smooth(0.25, 1.0, rn) ** 1.2 + 0.18 * side + 0.35 * (f3 - 0.5)
    strand, hole = lace(n, 18, rng, hole_p, 0.09, warp=0.03, rnd_r=9)
    membrane = (1 - hole) * (0.22 + 0.12 * f2)
    alpha = np.maximum(strand * 0.78, membrane)
    # strappi grandi e bordo sfilacciato
    tear = smooth(0.60, 0.70, f1 + 0.25 * (f2 - 0.5))
    alpha *= 1 - tear * 0.95 * (0.35 + 0.65 * smooth(0.25, 0.9, rn))
    rag = smooth(0.86, 1.0, rn) * (T.fbm(n, 12, 4, rng) > 0.44)
    alpha *= 1 - 0.97 * rag
    alpha *= smooth(0.02, 0.10, rn)                                   # attorno al polo il velo non c'e'
    cream, rose, maroon = rgb(0.78, 0.66, 0.52), rgb(0.74, 0.44, 0.38), rgb(0.52, 0.14, 0.13)
    col = mix(mix(cream, rose, smooth(0.3, 0.9, f1)), maroon, np.clip(strand * 0.6, 0, 1))
    col = col * (0.90 + 0.20 * f2)[..., None]
    height = 0.5 + 0.40 * strand + 0.10 * f2 - 0.2 * hole
    gloss = 0.62 + 0.22 * strand
    write_set("cappa", col.astype(F), alpha.astype(F), resize(height[..., None], 1024)[..., 0].astype(F), np.zeros((1024, 1024), F),
              np.ones((1024, 1024), F) * 0.9, resize(gloss[..., None], 1024)[..., 0].astype(F), 2.6, size_big=2048, size_small=1024)


def interno(n=1024):
    rng = np.random.default_rng(97)
    data = load_holes()
    px, py, rho, psi = disc(n)
    rn = np.clip(rho / 0.5, 0, 1.2)
    f1, f2, f3 = T.fbm(n, 5, 5, rng), T.fbm(n, 14, 4, rng), T.fbm(n, 36, 3, rng)
    # muscolatura radiale (fasci lungo i meridiani) e anelli
    bands = 0.5 + 0.5 * np.sin(psi * 36 + 3.5 * (f1 - 0.5) + rn * 4)
    rings = 0.5 + 0.5 * np.sin(rn * 70 + 5 * (f2 - 0.5))
    maroon, flesh, pink = rgb(0.25, 0.035, 0.05), rgb(0.50, 0.10, 0.12), rgb(0.72, 0.36, 0.34)
    col = mix(mix(maroon, flesh, 0.45 * bands + 0.35 * f1), pink, smooth(0.65, 1.0, rings * bands) * 0.45)
    d1, d2, idn = T.voronoi(n, 26, rng)
    web = 1 - smooth(0.04, 0.15, d2 - d1)
    col = mix(col, rgb(0.55, 0.06, 0.07), web * 0.55)
    # canali radiali e anello: luminosi, in verde acido
    canal = np.zeros((n, n), F)
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.2
        pts = [to_uv(0.04 + 0.45 * t / 20, a + 0.10 * math.sin(t * 0.7 + k)) for t in range(21)]
        draw_line(canal, pts, 0.0065, 0.9, 1.3)
    ring_pts = [to_uv(0.40 + 0.01 * math.sin(t * 0.6), 2 * math.pi * t / 60) for t in range(61)]
    draw_line(canal, ring_pts, 0.008, 1.0, 1.3)
    col = mix(col, rgb(0.55, 0.78, 0.20), canal * 0.55)
    height = 0.55 + 0.30 * bands + 0.10 * f2 + 0.15 * web - 0.12 * canal
    gloss = np.clip(0.72 - 0.15 * f3 + 0.15 * web, 0.2, 0.95)
    emiss = LIME[None, None, :] * (np.clip(canal * 0.9 + T.blur(canal, 5) * 0.8, 0, 1))[..., None] * 0.9
    write_set("interno", col.astype(F), None, height.astype(F), np.zeros((n, n), F), (1 - 0.35 * web).astype(F), gloss.astype(F), 3.0,
              emiss=np.clip(emiss, 0, 1).astype(F), size_small=1024, emiss_size=512)


# ---------------------------------------------------------------- anteprima
def anteprima(path):
    names = ["gel", "cappa", "interno", "carne", "velo", "luce"]
    tiles = []
    for nm in names:
        img = Image.open(OUT / f"{nm}_base.png").convert("RGBA").resize((384, 384))
        bg = Image.new("RGBA", img.size, (30, 36, 60, 255))
        bg.alpha_composite(img)
        tiles.append(bg.convert("RGB"))
    sheet = Image.new("RGB", (384 * 3, 384 * 2))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 3) * 384, (i // 3) * 384))
    sheet.save(path)


def main():
    global OUT
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("uscita", nargs="?", help="cartella di uscita (predefinita: Assets/_PryingEyes/Textures/Medusa)")
    ap.add_argument("--solo", default="", help="elenco separato da virgole, per esempio gel,cappa")
    ap.add_argument("--anteprima", help="salva un foglio di anteprima in questo file PNG")
    a = ap.parse_args()
    if a.uscita:
        OUT = Path(a.uscita)
    which = [w for w in a.solo.split(",") if w] or ["carne", "velo", "dente", "luce", "gel", "cappa", "interno"]
    for nm in which:
        globals()[nm]()
        print("fatto", nm)
    if a.anteprima:
        anteprima(a.anteprima)


if __name__ == "__main__":
    main()
