"""Texture PBR procedurali per la hall dell'hotel (fase 3): tutte ripetibili senza cuciture per costruzione
(rumore e Voronoi su griglie periodiche, normali con differenze finite che si richiudono sui bordi).

Per ogni materiale scrive in Assets/_PryingEyes/Textures/Hotel:
  <nome>_base.png    colore (sRGB)
  <nome>_normal.png  normal map (convenzione OpenGL/Unity: verde = su)
  <nome>_mask.png    R metallicita', G occlusione, A lucidita' (va su _MetallicGlossMap e _OcclusionMap)
Piu' le forme (solo alfa) per pozzanghere e macchie.

uso: python -I genera_texture.py [cartella_uscita] [--anteprima file.png]
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import texlib as tl  # noqa: E402  (realistic periodic materials: see texlib.py)

OUT = Path(__file__).resolve().parents[2] / "unity" / "PryingEyes" / "Assets" / "_PryingEyes" / "Textures" / "Hotel"
F = np.float32


# ---------------------------------------------------------------- rumore periodico
def fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def perlin(n, cells, rng, cells_y=None):
    """Rumore a gradiente periodico n x n con 'cells' celle per lato (cells_y diverso: rumore allungato,
    per esempio le fibre del legno). Valori circa -0.7..0.7."""
    cx_, cy_ = cells, (cells_y or cells)
    ang = rng.random((cy_, cx_)) * 2 * np.pi
    gx, gy = np.cos(ang).astype(F), np.sin(ang).astype(F)

    def axis(cn):
        c = np.arange(n, dtype=F) * cn / n
        i0 = np.floor(c).astype(int)
        return i0, (i0 + 1) % cn, (c - i0).astype(F)

    x0, x1, fxv = axis(cx_)
    y0, y1, fyv = axis(cy_)
    fx, fy = fxv[None, :], fyv[:, None]

    def corner(iy, ix, dx, dy):
        return gx[np.ix_(iy, ix)] * dx + gy[np.ix_(iy, ix)] * dy

    n00 = corner(y0, x0, fx, fy)
    n10 = corner(y0, x1, fx - 1, fy)
    n01 = corner(y1, x0, fx, fy - 1)
    n11 = corner(y1, x1, fx - 1, fy - 1)
    u, v = fade(fx), fade(fy)
    a = n00 + (n10 - n00) * u
    b = n01 + (n11 - n01) * u
    return a + (b - a) * v


def sample(img, xs, ys):
    """Lettura bilineare periodica di img in coordinate pixel (anche frazionarie)."""
    n = img.shape[0]
    x0 = np.floor(xs).astype(int)
    y0 = np.floor(ys).astype(int)
    fx, fy = (xs - x0).astype(F), (ys - y0).astype(F)
    x0 %= n; y0 %= n
    x1, y1 = (x0 + 1) % n, (y0 + 1) % n
    a = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
    b = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
    return a * (1 - fy) + b * fy


def warped(n, rng, base, octaves, amount):
    """fbm deformato da altri due fbm (domain warping): forme organiche, ancora periodiche."""
    f = fbm(n, base, octaves, rng)
    wx, wy = fbm(n, base, 4, rng), fbm(n, base, 4, rng)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    return sample(f, x + (wx - 0.5) * amount * n, y + (wy - 0.5) * amount * n)


def fbm(n, base, octaves, rng, gain=0.5):
    """Somma di ottave periodiche, normalizzata a 0..1."""
    acc = np.zeros((n, n), F)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        acc += perlin(n, base * 2 ** o, rng) * amp
        tot += amp
        amp *= gain
    acc /= tot
    return np.clip(acc * 0.9 + 0.5, 0, 1)


def voronoi(n, cells, rng):
    """Voronoi periodico a griglia perturbata: distanze al primo e al secondo punto (in celle) e id del piu' vicino."""
    pts = rng.random((cells, cells, 2)).astype(F) * 0.85 + 0.075
    c = (np.arange(n, dtype=F) + 0.5) * cells / n
    cy, cx = np.meshgrid(c, c, indexing="ij")
    ix, iy = np.floor(cx).astype(int), np.floor(cy).astype(int)
    d1 = np.full((n, n), 9, F)
    d2 = np.full((n, n), 9, F)
    ident = np.zeros((n, n), int)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            jx, jy = ix + ox, iy + oy
            p = pts[jy % cells, jx % cells]
            d = np.hypot(cx - (jx + p[..., 0]), cy - (jy + p[..., 1]))
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d))
            ident = np.where(closer, (jy % cells) * cells + (jx % cells), ident)
            d1 = np.minimum(d1, d)
    return d1, d2, ident


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def blur(a, r):
    """Sfocatura periodica (box ripetuto) di raggio r pixel."""
    out = a.astype(F)
    for _ in range(3):
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=1)
        out = acc / (2 * r + 1)
        acc = np.zeros_like(out)
        for k in range(-r, r + 1):
            acc += np.roll(out, k, axis=0)
        out = acc / (2 * r + 1)
    return out


def normals(h, strength):
    """Normal map da altezza (0..1), differenze finite periodiche. Riga 0 = alto della texture (v = 1)."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


def lerp(a, b, t):
    t = t[..., None] if np.ndim(t) == 2 and np.ndim(a) == 3 else t
    return a + (b - a) * t


def col(rgb, n):
    return np.broadcast_to(np.array(rgb, F), (n, n, 3)).astype(F)


def scratches(n, count, rng, length=(0.02, 0.12), width=1.0):
    """Graffi sottili e dritti (periodici): maschera 0..1."""
    m = np.zeros((n, n), F)
    for _ in range(count):
        x0, y0 = rng.random(2) * n
        a = rng.random() * np.pi
        ln = rng.uniform(*length) * n
        steps = int(ln)
        xs = (x0 + np.cos(a) * np.arange(steps)).astype(int) % n
        ys = (y0 + np.sin(a) * np.arange(steps)).astype(int) % n
        m[ys, xs] = np.maximum(m[ys, xs], rng.uniform(0.3, 1.0))
    return np.clip(blur(m, int(max(1, width))) * 3.0, 0, 1)


def save(name, base, nrm, metal, ao, smooth_):
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray((np.clip(base, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_base.png")
    Image.fromarray((np.clip(nrm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_normal.png")
    n = base.shape[0]
    mask = np.zeros((n, n, 4), F)
    mask[..., 0] = metal
    mask[..., 1] = ao
    mask[..., 2] = 0
    mask[..., 3] = smooth_
    Image.fromarray((np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_mask.png")
    print(f"{name}: {n}px")


# ---------------------------------------------------------------- materiali
def save_mm(name, base, h_mm, smooth_, ao, mm_per_px, relief=1.0, metal=None):
    """Same three maps as save(), from a height map in millimetres (texlib.write_set): relief means the same at any resolution."""
    tl.write_set(OUT, name, base, h_mm, smooth_, ao, mm_per_px, relief, metal)


def match_luminance(img, target):
    """Scales a colour image so that its mean luminance is target. The game's light was tuned on the old textures: the new ones have
    to look better, not brighter or darker."""
    lum = float((img @ np.array([0.2126, 0.7152, 0.0722], F)).mean())
    return np.clip(img * (target / lum), 0.0, 1.0).astype(F)


# ---------------------------------------------------------------- marmo
def marble_veins(n, rng, base, amount, width, soft):
    """Venature tipo marmo: creste (livello 0.5) di un fbm deformato, ramificate e organiche.
    width = spessore della cresta, soft = alone sfumato attorno (pixel di sfocatura)."""
    w = warped(n, rng, base, 6, amount)
    ridge = 1 - smooth(0.0, width, np.abs(w - 0.5))
    halo = blur(ridge, soft) if soft > 0 else ridge
    return np.clip(ridge * 0.75 + halo * 0.6, 0, 1)


def marmo_scacchiera(n=2048):
    """Pavimento: 2x2 piastrelle (bianco Carrara e nero Marquina) da 60 cm, fughe da 4 mm, graffi, sporco."""
    rng = np.random.default_rng(31)
    cell = n // 2
    y, x = np.mgrid[0:n, 0:n]
    cx, cy = x // cell, y // cell
    black = ((cx + cy) % 2 == 1)
    # venature diverse per ogni piastrella: quattro campi e si sceglie per cella
    fields = [(marble_veins(n, rng, 2, 0.35, 0.018, 6), marble_veins(n, rng, 4, 0.25, 0.006, 2)) for _ in range(4)]
    idx = cy * 2 + cx
    v_big = np.zeros((n, n), F)
    v_fine = np.zeros((n, n), F)
    for k in range(4):
        sel = idx == k
        v_big[sel] = fields[k][0][sel]
        v_fine[sel] = fields[k][1][sel]
    cloud = warped(n, rng, 3, 6, 0.3)
    white = col((0.87, 0.85, 0.81), n)
    white = lerp(white, col((0.72, 0.71, 0.70), n), smooth(0.35, 0.75, cloud) * 0.55)
    white = lerp(white, col((0.46, 0.46, 0.48), n), np.clip(v_big * 0.55 + v_fine * 0.45, 0, 1))
    blk = col((0.03, 0.03, 0.035), n)
    blk = lerp(blk, col((0.10, 0.095, 0.09), n), smooth(0.4, 0.8, cloud) * 0.6)
    blk = lerp(blk, col((0.82, 0.81, 0.77), n), np.clip(v_big * 0.7 + v_fine * 0.6, 0, 1))
    base = np.where(black[..., None], blk, white)

    # fughe: 4 mm su 60 cm di piastrella
    gw = max(2, int(n / 1.2 * 0.004))
    lx, ly = x % cell, y % cell
    edge = np.minimum(np.minimum(lx, cell - 1 - lx), np.minimum(ly, cell - 1 - ly)).astype(F)
    grout = 1 - smooth(gw * 0.5, gw * 1.6, edge)
    bevel = smooth(0, gw * 4, edge)                      # spigolo appena smussato
    # sporco: piu' vicino alle fughe e a chiazze grandi
    dirt_big = smooth(0.55, 0.85, fbm(n, 3, 5, rng))
    dirt_edge = (1 - smooth(gw, gw * 22, edge)) * 0.5
    dirt = np.clip(dirt_big * 0.45 + dirt_edge, 0, 1)
    scr = scratches(n, 350, rng, (0.01, 0.05), 1)
    scuff = smooth(0.6, 0.9, fbm(n, 8, 4, rng)) * 0.6      # zone opacizzate dal passaggio
    base = lerp(base, col((0.20, 0.18, 0.15), n), dirt * 0.35)
    base = lerp(base, col((0.62, 0.60, 0.57), n), scr * np.where(black, 0.25, 0.04))
    base = lerp(base, col((0.13, 0.12, 0.11), n), grout)

    h = bevel * (1 - grout) - scr * 0.02 + (fbm(n, 16, 3, rng) - 0.5) * 0.01
    nrm = normals(h, 9.0)
    smooth_ = np.where(black, 0.88, 0.84) - scuff * 0.35 - dirt * 0.25 - scr * 0.35
    smooth_ = smooth_ * (1 - grout) + 0.15 * grout
    ao = 1 - grout * 0.45 - dirt_edge * 0.15
    save("marmo_pavimento", base, nrm, np.zeros((n, n), F), ao, smooth_)


# the veined floor and the slab below are built from these helpers
VEIN_K = (0.8, 1.7)        # veins per tile across a free-standing tile: one or two long ones, not a fingerprint
VEIN_TURB = 0.20           # how far a vein wanders from its straight stripe, in stripe periods (more makes closed contour rings)
_PERIODIC_K = [(3, 2), (2, 3), (4, 1), (1, 4), (3, -2), (2, -3), (4, -1), (1, -4), (5, 2), (2, 5), (5, -2), (2, -5)]


def _kvec(rng, periodic, kscale):
    """Stripes per tile along x and y for one family of veins. Integers when the texture itself must repeat."""
    if periodic:
        k = _PERIODIC_K[int(rng.integers(0, len(_PERIODIC_K)))]
        return (k[0] * kscale, k[1] * kscale)
    big_k, th = rng.uniform(*VEIN_K) * kscale, rng.uniform(0.0, np.pi)
    return (big_k * np.cos(th), big_k * np.sin(th))


def _vein_family(ts, rng, kvec, turb_amp, turb_band, jitter_amp, jitter_band):
    """One family of veins: parallel stripes bent by slow turbulence (turb_amp in stripe periods) and roughened by fine jitter.
    The zero crossings of the returned sine field are the vein centres."""
    v, u = np.mgrid[0:ts, 0:ts].astype(F) / ts
    turb = tl.gauss_field(ts, rng, turb_band[0], turb_band[1], 1.4) * turb_amp
    jit = tl.gauss_field(ts, rng, jitter_band[0], jitter_band[1], 0.6) * jitter_amp
    return np.sin(2 * np.pi * (kvec[0] * u + kvec[1] * v + turb + jit + rng.random())).astype(F)


def _banded(ts, rng, kvec, s, periodic=False):
    """Soft cloud that runs along the veins: a random profile across the stripe direction. Wide bands (half the vein frequency) for a
    free-standing tile; with ``periodic`` the bands have the veins' own period, because half of an odd number of stripes per tile
    does not come back to where it started and leaves a seam."""
    v, u = np.mgrid[0:ts, 0:ts].astype(F) / ts
    turb = tl.gauss_field(ts, rng, 1 * s, 3 * s, 1.4)
    ph = (kvec[0] * u + kvec[1] * v) * (1.0 if periodic else 0.5) + 0.10 * turb
    prof = np.zeros((ts, ts), F)
    for j, amp in ((1, 1.0), (2, 0.7), (3, 0.45)):
        prof += amp * np.sin(2 * np.pi * (j * ph + rng.random()))
    return tl.zs(prof)


def _carrara(ts, rng, mm_per_px, kscale=1, periodic=False, vein_width=1.3):
    """Polished white marble (Carrara): soft grey bands that run along long crisp veins, hairline veinlets branching off them.
    ts px square, mm_per_px the size of a pixel; kscale multiplies the vein frequency for a bigger slab. Returns colour, smoothness."""
    px = 1.0 / mm_per_px
    g, s = tl.gauss_field, kscale
    k = _kvec(rng, periodic, s)
    bands = _banded(ts, rng, k, s, periodic)
    clouds = tl.zs(0.75 * bands + 0.45 * g(ts, rng, 1 * s, 6 * s, 1.2))
    mottle = g(ts, rng, 14 * s, ts // 3, 0.4)
    base = np.array([0.855, 0.845, 0.815], F) * (1 + 0.030 * clouds + 0.012 * mottle)[..., None]
    f1 = _vein_family(ts, rng, k, VEIN_TURB, (1 * s, 3 * s), 0.008, (8 * s, 30 * s))
    width = (2.0 + 2.2 * tl.smooth(-0.2, 1.6, g(ts, rng, 2 * s, 8 * s, 1.0))) * px * vein_width
    gate = tl.smooth(-0.7, 0.35, g(ts, rng, 1 * s, 3 * s, 1.4))
    faint = 0.45 + 0.55 * tl.smooth(-1.0, 1.0, g(ts, rng, 2 * s, 8 * s, 1.0))
    main = tl.contour(f1, width) * gate * faint
    k2 = _kvec(rng, periodic, s)
    f2 = _vein_family(ts, rng, k2, 0.9 * VEIN_TURB, (1 * s, 4 * s), 0.012, (10 * s, 40 * s))
    near = tl.smooth(0.02, 0.15, tl.blur(main, 13 * px))
    gate2 = tl.smooth(-0.5, 0.5, g(ts, rng, 2 * s, 8 * s, 1.0))
    fine = tl.contour(f2, (1.4 + 1.0 * tl.smooth(-1, 1, g(ts, rng, 4 * s, 20 * s, 1.0))) * px) * near * gate2
    tone = tl.smooth(-1.0, 1.0, g(ts, rng, 2 * s, 8 * s, 1.0))
    vein_col = tl.mix((0.46, 0.48, 0.53), (0.70, 0.70, 0.72), tone)
    a = np.clip(main * 0.85 + fine * 0.55, 0, 1)
    img = tl.mix(base, vein_col, a)
    band = np.clip(tl.blur(main, 8 * px) * 3.0, 0, 1)
    img = tl.mix(img, (0.72, 0.72, 0.73), band * 0.30)
    sm = 0.87 - 0.07 * a - 0.04 * tl.smooth(0.3, 1.8, mottle)
    return img, sm.astype(F)


def _marquina(ts, rng, mm_per_px, kscale=1, periodic=False):
    """Polished black marble (Nero Marquina): near-black with long white calcite veins and a network of healed fractures that
    breaks off and starts again. Returns colour, smoothness."""
    px = 1.0 / mm_per_px
    g, s = tl.gauss_field, kscale
    clouds = g(ts, rng, 1 * s, 6 * s, 1.3)
    base = tl.mix((0.024, 0.025, 0.029), (0.050, 0.051, 0.056), tl.smooth(0.3, 2.0, clouds))
    white = np.zeros((ts, ts), F)
    # one long family
    k = _kvec(rng, periodic, s)
    f = _vein_family(ts, rng, k, VEIN_TURB, (1 * s, 3 * s), 0.008, (8 * s, 30 * s))
    width = (1.6 + 2.2 * tl.smooth(0.0, 1.8, g(ts, rng, 2 * s, 10 * s, 1.0))) * px
    gate = tl.smooth(-0.3, 0.6, g(ts, rng, 1 * s, 3 * s, 1.4))
    white = np.maximum(white, tl.contour(f, width) * gate * 0.95)
    # the network of fractures healed with calcite: borders of a perturbed Voronoi diagram, only some of them visible
    cells = max(3, int(round(ts * mm_per_px / 95.0)))
    d1, d2, _, _, _ = tl.voronoi(ts, cells, rng, 0.9)
    y, x = np.mgrid[0:ts, 0:ts].astype(F)
    wx, wy = g(ts, rng, 6 * s, 24 * s, 0.8) * 3.0 * px / 1.0 * 1.2, g(ts, rng, 6 * s, 24 * s, 0.8) * 3.0 * px * 1.2
    edge = tl.sample(d2 - d1, x + wx, y + wy) * (ts / cells) * 0.5          # about the distance to the border in px
    w_net = (1.2 + 1.4 * tl.smooth(0.0, 1.8, g(ts, rng, 2 * s, 10 * s, 1.0))) * px
    net = 1 - tl.smooth(w_net * 0.35, w_net * 0.85, edge)
    gate_n = tl.smooth(0.0, 0.9, g(ts, rng, 1 * s, 3 * s, 1.4) + 0.1) * tl.smooth(-0.2, 1.2, g(ts, rng, 3 * s, 14 * s, 0.8) + 0.3)
    white = np.maximum(white, net * gate_n * 0.9)
    chalk = tl.smooth(3.0, 3.6, g(ts, rng, 10 * s, 80 * s, 0.3)) * 0.6
    white = np.clip(white + chalk, 0, 1)
    img = tl.mix(base, (0.84, 0.83, 0.79), white)
    img = tl.mix(img, (0.26, 0.26, 0.27), np.clip(tl.blur(white, 2.5 * px), 0, 1) * 0.30 * (1 - white))
    sm = 0.91 - 0.06 * white - 0.04 * tl.smooth(0.3, 1.8, clouds)
    return img, sm.astype(F)


def marmo_scacchiera_venato(n=2048):
    """Veined hotel floor (the other marble floor, alternating with the classic one room by room): 4 x 4 polished marble tiles of 60 cm (white Carrara, black Marquina) on a 2.4 m repetition, each tile cut from
    its own stone, tight joints with a little grime, and a dull haze where feet and wheels wore the polish."""
    rng = np.random.default_rng(31)
    cells = 4
    ts = n // cells
    mm = 2400.0 / n
    img = np.zeros((n, n, 3), F)
    sm = np.zeros((n, n), F)
    for ty in range(cells):
        for tx in range(cells):
            black_tile = (tx + ty) % 2 == 1
            t_img, t_sm = (_marquina if black_tile else _carrara)(ts, rng, mm)
            sl = (slice(ty * ts, (ty + 1) * ts), slice(tx * ts, (tx + 1) * ts))
            img[sl], sm[sl] = t_img, t_sm
    cx, cy, lx, ly, edge = tl.grid_cells(n, cells)
    black = (cx + cy) % 2 == 1
    gw = 2.4 / mm                                                 # 2.4 mm joints
    grout = 1 - tl.smooth(gw * 0.35, gw * 0.9, edge)
    bevel = 1 - tl.smooth(0.0, gw * 1.2, edge)
    grime = 1 - tl.smooth(gw * 0.6, gw * 5.0, edge)               # dirt creeping out of the joints
    dirt = tl.smooth(1.0, 2.4, tl.gauss_field(n, rng, 2, 8, 1.3))
    haze = tl.smooth(0.2, 2.2, tl.gauss_field(n, rng, 6, n // 8, 0.8))      # polish worn dull in small patches
    scratch = tl.strokes(n, rng, 500, (30, 140), 0.9)
    img = tl.mix(img, (0.28, 0.25, 0.20), np.clip(grime * 0.22 + np.where(black, 0.02, 0.10) * dirt, 0, 0.5))
    img = tl.mix(img, (0.50, 0.50, 0.50), scratch * np.where(black, 0.10, 0.02))
    img = tl.mix(img, (0.10, 0.095, 0.085), grout)
    sm = sm * (1 - 0.10 * haze) - 0.20 * scratch
    sm = sm * (1 - grout) + 0.12 * grout
    h = -1.0 * grout - 0.35 * bevel
    ao = 1 - 0.5 * grout - 0.10 * grime
    save_mm("marmo_pavimento_venato", match_luminance(img, 0.455), h, sm, ao, mm)


def marmo_bianco(n=1024):
    """Solid white marble for columns, stairs and the desk: a 2 m slab of Carrara, soft grey bands with crisp veins along them, the stone
    of the veined floor."""
    rng = np.random.default_rng(37)
    mm = 2000.0 / n
    img, sm = _carrara(n, rng, mm, kscale=1, periodic=True, vein_width=1.8)
    dirt = tl.smooth(1.0, 2.4, tl.gauss_field(n, rng, 2, 8, 1.3))
    haze = tl.smooth(0.2, 2.2, tl.gauss_field(n, rng, 6, n // 8, 0.8))
    scratch = tl.strokes(n, rng, 160, (20, 90), 0.8)
    img = tl.mix(img, (0.30, 0.27, 0.22), dirt * 0.14)
    sm = sm * (1 - 0.12 * haze) - 0.2 * scratch
    h = 0.04 * tl.gauss_field(n, rng, 60, n // 2, 0.3)
    save_mm("marmo_bianco", match_luminance(img, 0.783), h, sm, 1 - 0.08 * dirt, mm)


def damasco(n=1024):
    """Carta da parati damascata bordeaux (ripetizione di 64 cm): motivo opaco su fondo satinato, carta
    leggermente sbiadita e consumata. Le macchie grandi le fanno le decalcomanie (non si ripetono)."""
    rng = np.random.default_rng(41)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n

    def local(cx, cy):
        u = np.abs(((x - cx + 0.5) % 1.0) - 0.5)       # simmetria a specchio
        v = -(((y - cy + 0.5) % 1.0) - 0.5)            # riga 0 = alto: v positivo verso l'alto
        return u, v

    def ell(u, v, px, py, rx, ry, rot=0.0):
        du, dv = u - px, v - py
        c, s = np.cos(rot), np.sin(rot)
        a, b = du * c - dv * s, du * s + dv * c
        return (a / rx) ** 2 + (b / ry) ** 2 < 1.0

    def arc(u, v, px, py, r, w, a0, a1):
        """Voluta: arco di cerchio aperto da a0 ad a1 (gradi), spessore w."""
        d = np.hypot(u - px, v - py)
        ang = (np.degrees(np.arctan2(v - py, u - px)) + 360) % 360
        inside_ang = ((ang - a0) % 360) <= ((a1 - a0) % 360)
        return (np.abs(d - r) < w) & inside_ang

    def palmette(cx, cy):
        u, v = local(cx, cy)
        t = (v - 0.0) / 0.17
        rx = 0.085 * np.sqrt(np.clip(1 - t * t, 0, 1)) * (1 - 0.6 * smooth(0.0, 1.0, t))
        outer = (u < rx) & (np.abs(t) < 1)
        inner = (u < rx * 0.55) & (np.abs(t + 0.12) < 0.78)
        leaf_in = ell(u, v, 0.0, -0.02, 0.018, 0.07)                       # foglia al centro del bulbo
        m = (outer & ~inner) | leaf_in
        m |= arc(u, v, 0.14, -0.07, 0.06, 0.011, 300, 200)                 # voluta grande
        m |= arc(u, v, 0.14, -0.07, 0.028, 0.008, 120, 20)                 # ricciolo interno
        m |= ell(u, v, 0.075, -0.15, 0.02, 0.06, 0.9)                      # foglie alla base
        m |= (u < 0.009) & (v < -0.15) & (v > -0.23)                       # stelo
        m |= ell(u, v, 0.0, -0.235, 0.022, 0.016)
        m |= ell(u, v, 0.0, 0.205, 0.012, 0.03)                            # punta
        return m

    def flower(cx, cy):
        u, v = local(cx, cy)
        r = np.hypot(u, v)
        th = np.arctan2(v, u)
        return (r < 0.04 * (0.55 + 0.45 * np.abs(np.cos(2 * th)))) & (r > 0.008)

    m = palmette(0.25, 0.25) | palmette(0.75, 0.75) | flower(0.25, 0.75) | flower(0.75, 0.25)
    # reticolo a ogiva: linee sinuose che separano i motivi
    lx = (x + 0.055 * np.sin(2 * np.pi * 2 * y)) % 0.5
    m |= np.minimum(lx, 0.5 - lx) < 0.004
    m = blur(m.astype(F), 1)                           # bordo antialias

    paper = fbm(n, 32, 3, rng)
    fade_ = fbm(n, 2, 4, rng)
    # bordeaux tendente al marrone: con la luce calda delle applique un rosso puro diventava rosso fuoco
    ground = col((0.35, 0.13, 0.10), n)
    figure = col((0.23, 0.07, 0.055), n)
    base = lerp(ground, figure, m)
    base = lerp(base, col((0.42, 0.20, 0.18), n), smooth(0.45, 0.8, fade_) * 0.35)   # sbiadito
    base *= (0.92 + 0.16 * paper)[..., None]
    wear = scratches(n, 120, rng, (0.02, 0.08), 1)
    base = lerp(base, col((0.55, 0.45, 0.35), n), wear * 0.25)
    h = m * 0.6 + (paper - 0.5) * 0.15
    smooth_ = np.where(m > 0.5, 0.18, 0.42) - (paper - 0.5) * 0.1      # fondo satinato, motivo vellutato
    save("damasco", base, normals(blur(h, 1), 3.0), np.zeros((n, n), F), 1 - m * 0.08, smooth_)


def noce(n=1024):
    """Legno di noce scuro verniciato per boiserie, soppalco, travi, porte, banco."""
    rng = np.random.default_rng(43)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    # venatura lunga e dritta lungo x: rumore allungato (2 celle in x, 40 in y) che sposta gli anelli
    wob = perlin(n, 2, rng, cells_y=40) + perlin(n, 4, rng, cells_y=80) * 0.5
    rings = np.sin(2 * np.pi * (y * 22 + wob * 1.2)) * 0.5 + 0.5
    rings = rings ** 2.2                                           # linee scure sottili, legno chiaro largo
    fibers = perlin(n, 3, rng, cells_y=300) * 0.5 + 0.5            # pori in fila
    tone = fbm(n, 2, 4, rng)                                       # tavole un po' diverse
    g = np.clip(0.55 - rings * 0.35 + (fibers - 0.5) * 0.25 + (tone - 0.5) * 0.3, 0, 1)
    base = lerp(col((0.075, 0.042, 0.022), n), col((0.20, 0.115, 0.06), n), g)
    scr = scratches(n, 250, rng, (0.02, 0.1), 1)
    base = lerp(base, col((0.30, 0.20, 0.12), n), scr * 0.4)
    h = g * 0.3 - scr * 0.1
    smooth_ = 0.58 - scr * 0.3 - (1 - g) * 0.08
    save("noce", base, normals(h, 4.0), np.zeros((n, n), F), np.ones((n, n), F), smooth_)


def intonaco(n=1024):
    """Plaster, 2 m per repetition: slow drift of tone, hand-applied mottling, sand grain, an irregular water stain with a partial
    darker rim where it dried, a few hairline cracks that branch, ragged patches where the paint has flaked. Neutral warm grey: the
    material tints it."""
    rng = np.random.default_rng(47)
    g = tl.gauss_field
    mm = 2000.0 / n
    px = 1.0 / mm
    cloud = g(n, rng, 1, 4, 1.3)
    trowel = g(n, rng, 5, 22, 1.0)
    grain = g(n, rng, 70, n // 2, 0.1)
    base = tl.rgb((0.585, 0.560, 0.505), n) * (1 + 0.035 * cloud + 0.022 * trowel + 0.035 * grain)[..., None]
    wet = tl.zs(g(n, rng, 1, 3, 1.5) + 0.45 * g(n, rng, 4, 14, 1.0))
    stain = tl.smooth(1.0, 1.8, wet)
    rim = np.exp(-(((wet - 1.0) / 0.12) ** 2)) * tl.smooth(-0.2, 0.8, g(n, rng, 2, 8, 1.0))
    base = tl.mix(base, (0.47, 0.40, 0.31), stain * 0.28)
    base = tl.mix(base, (0.38, 0.31, 0.23), rim * 0.22)
    # cracks: level lines of a slow field, kept only where another field says the wall is stressed, with fine branches near them
    main = tl.contour(g(n, rng, 2, 8, 1.4), (3.5 + 2.5 * tl.smooth(-1, 1, g(n, rng, 4, 20, 1.0))) * px)
    main *= tl.smooth(0.7, 1.6, g(n, rng, 1, 3, 1.4))
    near = tl.smooth(0.03, 0.2, tl.blur(main, 10.0))
    branch = tl.contour(g(n, rng, 6, 26, 1.0), 3.0 * px) * near * tl.smooth(-0.3, 0.8, g(n, rng, 3, 12, 1.0))
    crack = np.clip(main + branch * 0.8, 0, 1)
    base = tl.mix(base, (0.25, 0.21, 0.17), crack * 0.65)
    # flaked paint: ragged patches of the older layer, with a pale lip where the paint broke
    pf = tl.zs(g(n, rng, 2, 7, 1.2) + 0.7 * g(n, rng, 10, 60, 0.5))
    flake = tl.smooth(2.0, 2.45, pf)
    lip = np.exp(-(((pf - 2.0) / 0.10) ** 2))
    base = tl.mix(base, (0.42, 0.37, 0.30), flake * 0.65)
    base = tl.mix(base, (0.70, 0.68, 0.62), lip * 0.30)
    h = 0.30 * trowel + 0.22 * cloud + 0.28 * grain - 1.2 * crack - 0.9 * flake + 0.25 * lip
    sm = 0.11 + 0.02 * grain - 0.03 * stain - 0.05 * flake
    ao = 1 - 0.40 * crack - 0.12 * flake - 0.04 * stain
    save_mm("intonaco", base, h, sm, ao, mm, relief=1.0)


def moquette(n=1024):
    """Moquette rossa a medaglioni color oro spento (ripetizione 1 m), pelo e passaggi consumati."""
    rng = np.random.default_rng(53)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    u, v = (x * 2) % 1.0 - 0.5, (y * 2) % 1.0 - 0.5          # 4 medaglioni per tessera
    r = np.hypot(u, v)
    ang = np.arctan2(v, u)
    petals = 0.16 + 0.05 * np.cos(4 * ang)
    med = 1 - smooth(petals - 0.006, petals + 0.006, r)
    med_in = 1 - smooth(0.075, 0.085, r)
    ringm = np.exp(-((r - 0.24) / 0.008) ** 2)
    diag = np.exp(-(((np.abs(u) - np.abs(v))) / 0.01) ** 2) * smooth(0.27, 0.29, r) * (1 - smooth(0.47, 0.5, np.maximum(np.abs(u), np.abs(v))))
    pattern = np.clip(med - med_in * 0.7 + ringm + diag * 0.6, 0, 1)
    pile = perlin(n, 256, rng) * 0.5 + 0.5
    wear = smooth(0.45, 0.8, fbm(n, 2, 5, rng))
    base = lerp(col((0.32, 0.035, 0.05), n), col((0.42, 0.28, 0.10), n), pattern * 0.85)
    base = lerp(base, col((0.40, 0.22, 0.20), n), wear * 0.35)
    base *= (0.85 + 0.3 * pile)[..., None]
    h = pile * 0.5 + pattern * 0.15 - wear * 0.2
    save("moquette", base, normals(h, 2.5), np.zeros((n, n), F), np.full((n, n), 0.95, F), 0.06 + wear * 0.08)


def ottone(n=512):
    """Ottone ossidato: metallo graffiato con chiazze di patina verde (non metallica) negli incavi."""
    rng = np.random.default_rng(59)
    pat = smooth(0.68, 0.82, fbm(n, 4, 6, rng)) * 0.8
    scr = scratches(n, 150, rng, (0.03, 0.15), 1)
    base = lerp(col((0.62, 0.47, 0.22), n), col((0.26, 0.31, 0.25), n), pat)
    base = lerp(base, col((0.80, 0.66, 0.40), n), scr * 0.3 * (1 - pat))
    metal = 1 - pat * 0.9
    smooth_ = 0.62 - pat * 0.45 - scr * 0.2 + (fbm(n, 16, 3, rng) - 0.5) * 0.1
    h = pat * 0.3 - scr * 0.1
    save("ottone", base, normals(h, 3.0), metal, 1 - pat * 0.2, smooth_)


def acciaio(n=512):
    """Acciaio spazzolato delle porte dell'ascensore: righe orizzontali, graffi, ditate opache."""
    rng = np.random.default_rng(61)
    streak = blur(np.repeat(rng.random((n, 1)).astype(F), n, axis=1), 1) * 0.6 + fbm(n, 8, 4, rng) * 0.4
    smudge = smooth(0.55, 0.8, fbm(n, 3, 5, rng))
    scr = scratches(n, 200, rng, (0.05, 0.2), 1)
    base = lerp(col((0.48, 0.49, 0.51), n), col((0.62, 0.63, 0.65), n), streak)
    base = lerp(base, col((0.36, 0.36, 0.37), n), smudge * 0.4)
    smooth_ = 0.58 + (streak - 0.5) * 0.15 - smudge * 0.3 - scr * 0.2
    save("acciaio", base, normals(streak * 0.2 - scr * 0.1, 2.0), np.ones((n, n), F) * 0.95, np.ones((n, n), F), smooth_)


def velluto(n=512):
    """Velluto dei divani: trama e pelo, grigio chiaro (il colore lo da' il materiale: rosso o verde)."""
    rng = np.random.default_rng(67)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    weave = (np.sin(2 * np.pi * x * 96) * np.sin(2 * np.pi * y * 96)) * 0.5 + 0.5
    pile = perlin(n, 128, rng) * 0.5 + 0.5
    crush = smooth(0.5, 0.8, fbm(n, 3, 5, rng))
    g = 0.78 + (pile - 0.5) * 0.2 - crush * 0.15
    base = np.repeat(g[..., None], 3, -1)
    h = weave * 0.3 + pile * 0.3
    save("velluto", base, normals(h, 2.0), np.zeros((n, n), F), np.ones((n, n), F), 0.12 + crush * 0.12)


def cuoio(n=512):
    """Cuoio di valigie e rivestimenti: grana a cellette, pieghe lunghe e usura; marrone."""
    rng = np.random.default_rng(73)
    d1, d2, _ = voronoi(n, 44, rng)
    grain = smooth(0.0, 0.16, d2 - d1)                      # i bordi delle cellette sono solchi
    wear = smooth(0.55, 0.85, fbm(n, 3, 5, rng))
    creases = 1 - smooth(0.0, 0.03, np.abs(perlin(n, 5, rng)))
    base = lerp(col((0.28, 0.15, 0.075), n), col((0.46, 0.30, 0.17), n), wear * 0.7)
    base = base * (0.8 + 0.2 * grain[..., None])
    base = lerp(base, col((0.14, 0.07, 0.04), n), creases * 0.5)
    h = grain * 0.25 - creases * 0.15 + (fbm(n, 24, 3, rng) - 0.5) * 0.1
    smooth_ = 0.34 + wear * 0.28 - creases * 0.12 + (grain - 0.5) * 0.1
    save("cuoio", base, normals(h, 2.8), np.zeros((n, n), F), 1 - (1 - grain) * 0.3, smooth_)


def guanto(n=512):
    """Cuoio dei guanti del giocatore: grana fine e fitta, pori, pieghe corte e incrociate, lucido dove la pelle si consuma.
    Grigio (la tinta la da' il materiale Mani_Guanto)."""
    rng = np.random.default_rng(91)
    d1, d2, _ = voronoi(n, 110, rng)
    grain = smooth(0.0, 0.10, d2 - d1)
    pores = smooth(0.78, 1.0, perlin(n, 192, rng) * 0.5 + 0.5)
    # pieghe corte e spezzate (non linee continue): creste di un rumore, mascherate da un altro rumore
    creases = (1 - smooth(0.0, 0.030, np.abs(perlin(n, 18, rng)))) * smooth(0.45, 0.72, fbm(n, 6, 3, rng))
    fine = (1 - smooth(0.0, 0.050, np.abs(perlin(n, 40, rng, 20)))) * smooth(0.40, 0.80, fbm(n, 10, 3, rng))
    wear = smooth(0.55, 0.9, fbm(n, 4, 5, rng))
    g = 0.62 + 0.10 * grain + 0.16 * wear - 0.12 * creases - 0.05 * fine
    base = np.repeat(g[..., None], 3, -1) * np.array([1.0, 0.95, 0.90], F)
    h = grain * 0.22 - creases * 0.28 - fine * 0.12 - pores * 0.05
    smooth_ = 0.36 + 0.26 * wear - 0.16 * creases + 0.05 * grain
    save("guanto", base, normals(h, 3.0), np.zeros((n, n), F), 1 - creases * 0.35 - fine * 0.1, smooth_)


def giacca(n=1024):
    """Tela della giacca del giocatore: armatura a saia 2/2 (coste diagonali), fili con fiammature, peluria, usura sbiadita.
    Grigio (la tinta la da' il materiale Mani_Manica). 128 fili per ripetizione."""
    rng = np.random.default_rng(97)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    T = 128
    u, v = x * T, y * T
    warp = np.sin(np.pi * u) ** 2
    weft = np.sin(np.pi * v) ** 2
    diag = np.sin(2 * np.pi * (u + v) / 4.0) * 0.5 + 0.5          # costa della saia: periodo 4 fili, chiude sui bordi (T multiplo di 4)
    slub = perlin(n, 16, rng, 128) * 0.5 + 0.5                     # fiammature lungo il filo di trama
    fuzz = perlin(n, 256, rng) * 0.5 + 0.5
    # niente macchie d'usura: la ripetizione e' di 9 cm e sulla manica diventavano pois; l'usura la fanno le pieghe e la luce
    wear = smooth(0.50, 0.92, fbm(n, 3, 5, rng)) * 0.15
    h = 0.42 * diag + 0.18 * warp + 0.18 * weft + 0.14 * slub + 0.08 * fuzz
    g = 0.56 + 0.10 * (diag - 0.5) + 0.07 * (slub - 0.5) + 0.05 * (fuzz - 0.5) + 0.16 * wear
    base = np.repeat(g[..., None], 3, -1) * np.array([1.0, 0.98, 0.94], F)
    ao = 0.82 + 0.18 * h
    save("giacca", base, normals(h, 2.2), np.zeros((n, n), F), ao, 0.10 + 0.06 * diag + 0.04 * wear)


def rinforzo(n=512):
    """Gomma dei paranocche e del cinturino: buccia d'arancia fine, opaca, grigio scuro."""
    rng = np.random.default_rng(101)
    peel = fbm(n, 48, 3, rng)
    dots = perlin(n, 160, rng) * 0.5 + 0.5
    h = peel * 0.35 + dots * 0.20
    base = np.repeat((0.50 + 0.10 * peel)[..., None], 3, -1) * np.array([0.98, 0.99, 1.0], F)
    save("rinforzo", base, normals(h, 1.6), np.zeros((n, n), F), np.ones((n, n), F), 0.20 + 0.10 * peel)


def forme(n=512):
    """Forme in scala di grigi (alfa) per pozzanghere e macchie: bordo irregolare e sfumato."""
    rng = np.random.default_rng(71)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    r = np.hypot(x - 0.5, y - 0.5) * 2
    for name, rough, rim in (("pozzanghera", 0.35, 0.0), ("macchia", 0.5, 0.45)):
        noise = fbm(n, 4, 5, rng)
        shape = 1 - smooth(0.55, 0.75, r + (noise - 0.5) * rough * 1.6)
        if rim > 0:   # alone d'acqua: piu' scuro sul bordo
            edge = np.exp(-((r + (noise - 0.5) * rough * 1.6 - 0.66) / 0.05) ** 2)
            shape = np.clip(shape * 0.55 + edge * rim + (fbm(n, 16, 3, rng) - 0.5) * 0.2 * shape, 0, 1)
        img = np.zeros((n, n, 4), F)
        img[..., :3] = 1.0
        img[..., 3] = shape
        Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}.png")
        print(f"{name}: {n}px")
    # sporco alla base dei muri: sfuma dal basso verso l'alto con bordo irregolare
    noise = fbm(n, 4, 5, rng)
    grad = 1 - smooth(0.0, 0.9, (1 - y) * 1.0 + (noise - 0.5) * 0.4)
    img = np.zeros((n, n, 4), F)
    img[..., :3] = 1.0
    img[..., 3] = np.clip(grad * (0.7 + 0.3 * fbm(n, 16, 3, rng)), 0, 1)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(OUT / "sporco_base.png")
    print("sporco_base")


def _nz(a):
    """Normalizza un campo di rumore a 0..1."""
    return (a - a.min()) / (a.max() - a.min() + 1e-9)


def linoleo(n=1024):
    """Sanatorium floor: 25 cm tiles of grey-green speckled vinyl (4 x 4 per 1 m repetition), each tile with its own tone and
    chips, thin seams with a little grime, heel scuffs, and a satin sheen that is smoother along the walking lanes."""
    rng = np.random.default_rng(61)
    g = tl.gauss_field
    mm = 1000.0 / n
    px = 1.0 / mm
    cx, cy, lx, ly, edge = tl.grid_cells(n, 4)
    tone = tl.hash2(cx, cy, 5)
    swirl = g(n, rng, 3, 24, 1.1)
    swirl2 = g(n, rng, 10, 60, 0.9)
    chips = g(n, rng, 120, n // 2, 0.1)
    base = tl.rgb((0.505, 0.565, 0.525), n)
    base = tl.mix(base, (0.58, 0.635, 0.585), tl.smooth(0.2, 1.7, swirl) * 0.55)
    base = tl.mix(base, (0.40, 0.465, 0.43), tl.smooth(0.2, 1.7, -swirl2) * 0.45)
    base = tl.mix(base, (0.72, 0.74, 0.69), tl.smooth(2.7, 3.3, chips) * 0.50)
    base = tl.mix(base, (0.26, 0.31, 0.29), tl.smooth(2.7, 3.3, -chips) * 0.45)
    base = base * (0.94 + 0.12 * tone)[..., None]
    gw = 1.4 * px
    seam = 1 - tl.smooth(gw * 0.35, gw * 0.9, edge)
    grime = 1 - tl.smooth(gw * 0.6, gw * 5.0, edge)
    base = tl.mix(base, (0.24, 0.25, 0.22), np.clip(grime * 0.20, 0, 0.5))
    base = tl.mix(base, (0.20, 0.21, 0.19), seam * 0.8)
    scuff = tl.strokes(n, rng, 45, (14, 60), 1.5, curve=1.2, weight=(0.3, 0.9))
    scrape = tl.strokes(n, rng, 70, (10, 40), 0.8, weight=(0.3, 0.8))
    base = tl.mix(base, (0.25, 0.28, 0.26), scuff * 0.35)
    base = tl.mix(base, (0.70, 0.73, 0.68), scrape * 0.20)
    lane = tl.smooth(0.0, 1.5, g(n, rng, 1, 3, 1.3))
    sm = 0.40 + 0.15 * lane - 0.18 * scuff - 0.08 * scrape + 0.03 * g(n, rng, 20, n // 3, 0.5)
    sm = sm * (1 - seam) + 0.10 * seam
    bevel = 1 - tl.smooth(0.0, 1.4 * px, edge)
    h = -0.6 * seam - 0.25 * bevel + 0.15 * (tone - 0.5) + 0.06 * chips
    save_mm("linoleo", match_luminance(base, 0.575), h, sm, 1 - 0.45 * seam - 0.10 * grime, mm)


def vernice_verde(n=1024):
    """Hospital-green oil paint for the lower part of walls, 2 m per repetition, semi-gloss: faint horizontal brush marks, orange
    peel, scuffs and scrapes from trolleys, ragged chips down to the cream undercoat. Nothing depends on the height above the
    floor, so the tile repeats without a seam (the old one had a damp gradient that jumped every 2 m)."""
    rng = np.random.default_rng(73)
    g = tl.gauss_field
    mm = 2000.0 / n
    brush = g(n, rng, 4, n // 3, 0.5, aniso=(24, 1))
    peel = g(n, rng, 50, n // 2, 0.3)
    mott = g(n, rng, 1, 5, 1.2)
    base = tl.rgb((0.395, 0.545, 0.460), n) * (1 + 0.016 * brush + 0.015 * peel + 0.030 * mott)[..., None]
    scuff = tl.strokes(n, rng, 70, (20, 90), 2.2, curve=1.2, weight=(0.3, 0.9))
    scrape = tl.strokes(n, rng, 170, (10, 60), 0.8, direction=0.0, spread=0.7, weight=(0.3, 0.9))
    base = tl.mix(base, (0.28, 0.38, 0.32), scuff * 0.30)
    base = tl.mix(base, (0.66, 0.72, 0.64), scrape * 0.20)
    pf = tl.zs(g(n, rng, 2, 6, 1.2) + 0.8 * g(n, rng, 12, 70, 0.5))
    chip = tl.smooth(2.0, 2.45, pf)
    lip = np.exp(-(((pf - 2.0) / 0.10) ** 2))
    base = tl.mix(base, (0.70, 0.68, 0.60), chip * 0.75)
    base = tl.mix(base, (0.20, 0.27, 0.23), lip * 0.40)
    h = 0.10 * brush + 0.05 * peel - 0.9 * chip + 0.2 * lip - 0.25 * scrape
    sm = 0.32 + 0.02 * brush - 0.12 * scuff - 0.22 * chip - 0.06 * scrape
    save_mm("vernice_verde", base, h, sm, 1 - 0.18 * chip - 0.05 * scuff, mm, relief=1.4)


def ruggine(n=1024):
    """Ferro verniciato e arrugginito (letti, carrelli, sedie a rotelle): smalto grigio-bianco sporco che salta a placche e
    lascia vedere ruggine bruna e arancio, colature verticali scure, graffi; dove e' scoperto il metallo e' opaco."""
    rng = np.random.default_rng(79)
    patch = _nz(warped(n, rng, 4, 6, 0.35))                    # dove lo smalto e' saltato
    flake = smooth(0.50, 0.60, patch)
    detail = _nz(fbm(n, 32, 4, rng))
    mott = _nz(fbm(n, 6, 5, rng))
    streak = blur(np.repeat(rng.random((1, n)).astype(F), n, axis=0), 1) * 0.55 + _nz(fbm(n, 3, 4, rng)) * 0.45
    drip = smooth(0.55, 0.85, streak) * smooth(0.40, 0.62, patch)
    scr = scratches(n, 90, rng, (0.02, 0.10), 1)
    enamel = lerp(col((0.66, 0.67, 0.62), n), col((0.50, 0.52, 0.48), n), mott)
    rust = lerp(col((0.28, 0.11, 0.045), n), col((0.64, 0.29, 0.09), n), detail)
    base = lerp(enamel, rust, flake)
    base = lerp(base, col((0.16, 0.07, 0.035), n), drip * 0.55)
    base = lerp(base, col((0.82, 0.80, 0.74), n), scr * 0.25 * (1 - flake))
    h = -flake * 0.35 + (detail - 0.5) * 0.18 * flake + (mott - 0.5) * 0.05 - scr * 0.1
    metal = flake * 0.22 + scr * 0.1
    smooth_ = 0.52 - flake * 0.36 - drip * 0.08 + (mott - 0.5) * 0.08
    save("ruggine", base, normals(h, 2.6), metal, 1 - flake * 0.18 - drip * 0.1, smooth_)


def anteprima(path):
    names = ["marmo_pavimento", "marmo_pavimento_venato", "marmo_bianco", "damasco", "noce", "intonaco", "moquette", "ottone", "acciaio", "velluto"]
    tiles = []
    for nm in names:
        b = Image.open(OUT / f"{nm}_base.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        nr = Image.open(OUT / f"{nm}_normal.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        mk = Image.open(OUT / f"{nm}_mask.png").getchannel("A").convert("RGB").resize((256, 256), Image.LANCZOS)
        row = Image.new("RGB", (256 * 3 + 8, 256), (0, 0, 0))
        row.paste(b, (0, 0)); row.paste(nr, (260, 0)); row.paste(mk, (520, 0))
        tiles.append(row)
    sheet = Image.new("RGB", (tiles[0].width * 3 + 16, 264 * ((len(tiles) + 2) // 3)), (0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 3) * (t.width + 8), (i // 3) * 264))
    sheet.save(path)
    print(path, sheet.size)


if __name__ == "__main__":
    preview = None
    rest = []
    it = iter(sys.argv[1:])
    for a in it:
        if a == "--anteprima":
            preview = next(it)
        else:
            rest.append(a)
    # "--solo damasco,noce": rigenera solo questi
    only = None
    if "--solo" in rest:
        k = rest.index("--solo")
        only = set(rest[k + 1].split(","))
        rest = rest[:k] + rest[k + 2:]
    if rest:
        OUT = Path(rest[0])
    for fn in (marmo_scacchiera, marmo_scacchiera_venato, marmo_bianco, damasco, noce, intonaco, moquette, ottone, acciaio, velluto, cuoio, forme, linoleo, vernice_verde, ruggine,
               guanto, giacca, rinforzo):
        if only is None or fn.__name__ in only:
            fn()
    if preview:
        anteprima(preview)
