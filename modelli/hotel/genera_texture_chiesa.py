"""Texture PBR della chiesa e delle stanze nuove (piani procedurali): pietra di muro, lastre del pavimento, pietra
grezza della cripta, mattoni delle volte, parquet a listoni, piastrelle, cemento di servizio e le vetrate colorate.
Stesse regole di genera_texture.py (ripetibili per costruzione, normal map OpenGL, maschera R metallo / G occlusione / A lucidita');
riusa le sue funzioni.

uso: python -I genera_texture_chiesa.py [--solo pietra_muro,parquet] [--anteprima file.png]
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import genera_texture as gt
import texlib as tl  # noqa: E402  (realistic periodic materials)
from genera_texture import F, fbm, perlin, voronoi, smooth, blur, normals, lerp, col, scratches, warped

OUT = gt.OUT


def save(name, base, nrm, metal, ao, smooth_):
    gt.OUT = OUT
    gt.save(name, base, nrm, metal, ao, smooth_)


def hash2(ix, iy, seed):
    h = (ix.astype(np.int64) * 73856093) ^ (iy.astype(np.int64) * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF).astype(F) / 65535.0


def wall_bond(n, cols, rows, rng, jw, warp=0.012):
    """Corsi sfalsati a mezzo blocco: ritorna (distanza dal bordo del blocco in pixel, id riga, id colonna,
    coordinate locali 0..1 dentro il blocco). Il bordo e' deformato da rumore (blocchi non perfetti)."""
    y, x = np.mgrid[0:n, 0:n].astype(F)
    wx = (fbm(n, 6, 3, rng) - 0.5) * warp * n
    wy = (fbm(n, 6, 3, rng) - 0.5) * warp * n
    xx, yy = x + wx, y + wy
    rh, bw = n / rows, n / cols
    row = np.floor(yy / rh).astype(int)
    off = (row % 2) * bw * 0.5
    xo = xx + off
    bx = np.floor(xo / bw).astype(int)
    lx = (xo - bx * bw)
    ly = yy - row * rh
    edge = np.minimum(np.minimum(lx, bw - lx), np.minimum(ly, rh - ly))
    return edge, row % rows, bx % cols, lx / bw, ly / rh


def irregular_courses(x, y, cuts_y, rng, widths, count):
    n = x.shape[0]
    cy = np.searchsorted(cuts_y[1:-1], y)
    height = cuts_y[cy + 1] - cuts_y[cy]
    ly = (y - cuts_y[cy]) / height
    edge = np.minimum(ly, 1 - ly) * height
    cx = np.empty_like(cy)
    lx = np.empty_like(x)
    for row in range(len(cuts_y) - 1):
        indices = cy == row
        cuts = np.r_[0, np.cumsum(rng.uniform(*widths, count(row)))].astype(F)
        cuts *= n / cuts[-1]
        local = (x[indices] + rng.uniform(0, n)) % n
        tile = np.searchsorted(cuts[1:-1], local)
        width = cuts[tile + 1] - cuts[tile]
        u = (local - cuts[tile]) / width
        lx[indices], cx[indices] = u, tile
        edge[indices] = np.minimum(edge[indices], np.minimum(u, 1 - u) * width)
    return edge, cy, cx, lx, ly


def save_mm(name, base, h_mm, smooth_, ao, mm_per_px, relief=1.0, metal=None):
    """The three maps from a height map in millimetres (see texlib.write_set)."""
    gt.OUT = OUT
    gt.save_mm(name, base, h_mm, smooth_, ao, mm_per_px, relief, metal)


match = gt.match_luminance          # keeps the mean luminance of the texture it replaces


def block_shift(field, cx, cy, seed):
    """Every block reads ``field`` from a place of its own, so neighbouring stones are cut from different rock."""
    n = field.shape[0]
    y, x = np.mgrid[0:n, 0:n]
    ox = (tl.hash2(cx, cy, seed) * n).astype(np.int64)
    oy = (tl.hash2(cx, cy, seed + 1) * n).astype(np.int64)
    return field[(y + oy) % n, (x + ox) % n]


def stain(n, rng, scale=1.0, coverage=0.25, soft=0.8, detail=1.25, aniso=(1.0, 1.0)):
    """Ragged stain mask, 0..1: a fractal field cut at the level that covers ``coverage`` of the surface, with a soft edge. Unlike a
    blurred blob it has detail at every scale, which is what keeps soot and damp from reading as smudges."""
    f = tl.gauss_field(n, rng, scale, n // 4, detail, aniso)
    thr = float(np.quantile(f, 1.0 - coverage))
    return tl.smooth(thr - soft * 0.5, thr + soft * 0.5, f)


def cavity(h_mm, mm, strength=1.0, sigma_mm=6.0):
    """Darkening in the hollows (pores, joints, chips) taken from the height map: what ambient occlusion does under flat light."""
    c = tl.blur(h_mm, sigma_mm / mm) - h_mm
    c = c / (float(c.std()) + 1e-6)
    return (1.0 - strength * 0.22 * tl.smooth(0.0, 3.0, c)).astype(F)


def _stone(n, rng, mm, cx, cy, lx, ly, light, dark, tone_var=0.15, bedding=0.3, pits=1.0, tooling=0.5, roughness=1.2, dome_mm=2.5, hue=0.06, speckle=1.0, mottling=0.10, chisel=0.5):
    """Dressed limestone: colour, height in mm, smoothness and the pit mask. (cx, cy) identify the block of every pixel and (lx, ly)
    are the position inside it, 0..1 (lx None: the block wraps around, like a column drum). Every block has its own tone and pits, a
    slightly domed face, fractal mottling, pale and dark mineral flecks and, if asked, soft strata."""
    g = tl.gauss_field
    seed = int(rng.integers(1, 9999))
    counter = iter(range(1, 99))

    def sh(field):
        return block_shift(field, cx, cy, seed + 3 * next(counter))

    tone = tl.hash2(cx, cy, 7)
    warm = tl.hash2(cx, cy, 11)
    mott = sh(g(n, rng, 2, n // 6, 1.0))
    bed = sh(g(n, rng, 2, max(6, n // 40), 0.4, aniso=(14, 1)))
    grain = g(n, rng, n // 6, n // 2, 0.0)
    fleck = g(n, rng, n // 3, n // 2, 0.0)
    t = np.clip(0.5 + tone_var * (tone - 0.5) * 2.0 + mottling * mott + 0.025 * bedding * bed + 0.04 * speckle * grain, 0.0, 1.0)
    stone = tl.mix(dark, light, t)
    stone = stone * (1.0 + hue * (warm - 0.5)[..., None] * np.array([1.0, 0.25, -1.0], F))
    stone = tl.mix(stone, np.asarray(light, F) * 1.1, tl.smooth(2.9 - 0.3 * speckle, 3.6 - 0.3 * speckle, fleck) * 0.35)
    stone = tl.mix(stone, np.asarray(dark, F) * 0.6, tl.smooth(2.9 - 0.3 * speckle, 3.6 - 0.3 * speckle, -fleck) * 0.35)
    pit = pits * np.maximum(tl.smooth(2.7, 3.4, g(n, rng, n // 5, n // 2, 0.0)), tl.smooth(3.0, 3.7, g(n, rng, n // 12, n // 5, 0.0)))
    stone = tl.mix(stone, np.asarray(dark, F) * 0.55, pit * 0.65)
    face = np.abs(2.0 * ly - 1.0) if lx is None else np.maximum(np.abs(2.0 * lx - 1.0), np.abs(2.0 * ly - 1.0))
    dome = 1.0 - np.clip(face, 0.0, 1.0) ** 4
    h = (dome_mm * dome * (0.4 + tone) + roughness * sh(g(n, rng, 8, 50, 1.0)) + chisel * sh(g(n, rng, 40, 120, 0.8))
         + tooling * g(n, rng, n // 10, n // 3, 0.3) - 1.6 * pit - 0.4 * bedding * bed)
    sm = 0.13 + 0.07 * tone + 0.03 * grain - 0.05 * pit
    return stone.astype(F), h.astype(F), sm.astype(F), pit


def pietra_muro(n=2048):
    """Dressed limestone courses of different heights on a 4.2 m repetition. Every block has its own tone, pits, mineral flecks and a
    domed face; arrises are rounded and chipped (the chips show fresh pale stone); the joints are recessed sandy lime mortar; a few
    blocks were replaced and are paler; rain runs down the face, soot and damp darken it. The old texture was blurred clouds over
    flat joints."""
    rng = np.random.default_rng(101)
    g = tl.gauss_field
    mm = 4200.0 / n
    px = 1.0 / mm
    jw = 26.0 * px                                                     # joints 26 mm wide
    y, x = np.mgrid[0:n, 0:n].astype(F)
    xx = (x + g(n, rng, 2, n // 8, 1.3) * 0.0020 * n + g(n, rng, n // 40, n // 10, 0.8) * 0.9) % n
    yy = (y + g(n, rng, 2, n // 8, 1.3) * 0.0016 * n + g(n, rng, n // 40, n // 10, 0.8) * 0.9) % n
    courses = np.array([0, .085, .13, .227, .312, .37, .475, .555, .62, .733, .812, .861, .939, 1], F) * n
    edge, row, bx, lx, ly = irregular_courses(xx, yy, courses, rng, (0.55, 1.6), lambda i: 6 + (i * 5) % 4)
    stone, h, sm, pit = _stone(n, rng, mm, bx, row, lx, ly, (0.62, 0.58, 0.50), (0.38, 0.37, 0.33), tone_var=0.30,
                               dome_mm=5.0, roughness=1.4, tooling=0.15, chisel=0.35, pits=0.5)
    mortar = 1.0 - tl.smooth(jw * 0.30, jw * 0.62, edge)
    arris = 1.0 - tl.smooth(0.0, 14.0 * px, edge)
    chip = tl.smooth(1.3, 2.6, g(n, rng, 15, n // 5, 1.0)) * arris
    h = h - 3.5 * arris - 6.0 * chip
    stone = tl.mix(stone, (0.66, 0.62, 0.54), chip * 0.6)
    fresh = (tl.hash2(bx, row, 17) > 0.90).astype(F)                   # replaced blocks
    stone = tl.mix(stone, (0.68, 0.64, 0.56), fresh * 0.5)
    sand = g(n, rng, n // 6, n // 2, 0.0)
    lime = tl.mix((0.40, 0.38, 0.32), (0.58, 0.55, 0.48), tl.smooth(-1.0, 1.0, g(n, rng, 3, n // 8, 1.0))) * (1.0 + 0.07 * sand)[..., None]
    base = tl.mix(stone, lime, mortar)
    h = h * (1.0 - mortar) + (-8.0 + 1.6 * g(n, rng, n // 20, n // 5, 0.8)) * mortar
    streak = stain(n, rng, 3, 0.30, 0.8, aniso=(1, 6)) * (0.4 + 0.6 * tl.smooth(-0.5, 1.2, g(n, rng, 2, 8, 1.2)))
    soot, damp = stain(n, rng, 1, 0.22, 0.9), stain(n, rng, 1, 0.18, 0.9)
    base = tl.mix(base, (0.26, 0.25, 0.22), streak * 0.22)
    base = tl.mix(base, (0.12, 0.11, 0.10), soot * 0.20)
    base = tl.mix(base, (0.20, 0.26, 0.16), damp * 0.14 * (1.0 - fresh))
    base = base * cavity(h, mm)[..., None]
    sm = sm * (1.0 - mortar) + 0.07 * mortar + 0.10 * damp - 0.05 * soot
    ao = 1.0 - 0.55 * mortar - 0.15 * arris * (1.0 - mortar) - 0.5 * pit
    save_mm("pietra_muro", match(base, 0.370), h, sm, ao, mm)


def pietra_lastre(n=2048):
    """Flagstones of a church floor on a 4.8 m repetition: courses of different widths, fine-grained stone worn pale and smooth at the
    centre of the slabs by feet, hairline cracks that wander, dirt packed into the joints."""
    rng = np.random.default_rng(103)
    g = tl.gauss_field
    mm = 4800.0 / n
    px = 1.0 / mm
    jw = 10.0 * px
    y, x = np.mgrid[0:n, 0:n].astype(F)
    xx = (x + g(n, rng, 2, n // 8, 1.3) * 0.0015 * n + g(n, rng, n // 40, n // 10, 0.8) * 1.0) % n
    yy = (y + g(n, rng, 2, n // 8, 1.3) * 0.0015 * n + g(n, rng, n // 40, n // 10, 0.8) * 1.0) % n
    courses = np.array([0, 0.16, 0.34, 0.57, 0.70, 0.87, 1], F) * n
    edge, cy, cx, lx, ly = irregular_courses(xx, yy, courses, rng, (0.55, 1.5), lambda row: 4 + row % 3)
    stone, h, sm, pit = _stone(n, rng, mm, cx, cy, lx, ly, (0.62, 0.57, 0.48), (0.40, 0.39, 0.35), tone_var=0.14,
                               bedding=0.2, pits=0.6, tooling=0.10, roughness=0.9, dome_mm=2.0, chisel=0.25)
    grout = 1.0 - tl.smooth(jw * 0.30, jw * 0.65, edge)
    arris = 1.0 - tl.smooth(0.0, 8.0 * px, edge)
    chip = tl.smooth(1.3, 2.6, g(n, rng, 15, n // 5, 1.0)) * arris
    stone = tl.mix(stone, (0.66, 0.62, 0.54), chip * 0.5)
    uc = np.hypot(lx - 0.5, ly - 0.5)
    worn = (1.0 - tl.smooth(0.10, 0.55, uc)) * (0.35 + 0.65 * tl.smooth(-0.5, 1.0, block_shift(g(n, rng, 2, 8, 1.2), cx, cy, 5)))
    stone = tl.mix(stone, (0.64, 0.60, 0.52), worn * 0.14)
    crack = tl.contour(g(n, rng, 3, n // 8, 1.7), 2.4 * px) * tl.smooth(1.1, 2.0, g(n, rng, 1, 4, 1.3))
    stone = tl.mix(stone, (0.13, 0.12, 0.10), crack * 0.70)
    sand = g(n, rng, n // 6, n // 2, 0.0)
    dirt, damp = stain(n, rng, 1, 0.30, 0.9), stain(n, rng, 1, 0.20, 1.0)
    base = tl.mix(stone, (0.15, 0.14, 0.12), dirt * 0.28)
    base = tl.mix(base, (0.16, 0.20, 0.13), damp * 0.16)
    packed = tl.mix((0.28, 0.25, 0.20), (0.46, 0.42, 0.35), tl.smooth(-1.0, 1.0, g(n, rng, 2, n // 8, 1.0))) * (1.0 + 0.07 * sand)[..., None]
    base = tl.mix(base, packed, grout)
    h = h - 1.6 * arris - 3.5 * chip - 1.6 * crack
    h = h * (1.0 - grout) + (-4.5 + 1.2 * g(n, rng, n // 20, n // 5, 0.8)) * grout
    base = base * cavity(h, mm)[..., None]
    sm = 0.17 + 0.20 * worn - 0.10 * dirt + 0.12 * damp + 0.3 * (sm - 0.13)
    sm = sm * (1.0 - grout) + 0.08 * grout
    ao = 1.0 - 0.50 * grout - 0.12 * arris * (1.0 - grout) - 0.4 * pit - 0.2 * crack
    save_mm("pietra_lastre", match(base, 0.426), h, sm, ao, mm)


def pietra_massello(n=1024):
    """Carved limestone with no joints, for ashlar blocks, capitals and cornices (1.5 m repetition): fine pores and mineral flecks and
    fractal clouds of tone."""
    rng = np.random.default_rng(104)
    g = tl.gauss_field
    mm = 1500.0 / n
    px = 1.0 / mm
    zero = np.zeros((n, n), np.int64)
    stone, h, sm, pit = _stone(n, rng, mm, zero, zero, None, np.full((n, n), 0.5, F), (0.66, 0.61, 0.52), (0.36, 0.34, 0.30),
                               tone_var=0.0, bedding=0.6, pits=2.0, tooling=0.15, roughness=1.2, dome_mm=0.0, speckle=3.0, mottling=0.07, chisel=0.4)
    weather = stain(n, rng, 2, 0.35, 1.3, detail=1.4)                  # weathered patches, dark and pale, with ragged edges
    pale = stain(n, rng, 2, 0.25, 1.3, detail=1.4)
    stone = tl.mix(stone, (0.26, 0.245, 0.22), weather * 0.38)
    stone = tl.mix(stone, (0.72, 0.67, 0.58), pale * 0.25)
    pore = tl.smooth(1.9, 2.7, g(n, rng, n // 8, n // 3, 0.0))
    stone = tl.mix(stone, (0.20, 0.18, 0.15), pore * 0.55)
    h = h - 1.3 * pore
    stone = stone * cavity(h, mm)[..., None]
    sm = 0.16 + 0.3 * (sm - 0.13)
    save_mm("pietra_massello", match(stone, 0.400), h, sm, 1.0 - 0.45 * pit - 0.25 * pore, mm)


def pietra_colonna(n=1024):
    """Shaft of a stone column for a 2.4 m x 2.4 m tile: three drums of 80 cm, each one a single stone that goes all the way around, so
    the texture wraps with no vertical seam. Hairline joints between the drums, chipped edges, soot and rain-run stains."""
    rng = np.random.default_rng(151)
    g = tl.gauss_field
    mm = 2400.0 / n
    px = 1.0 / mm
    drums = 3
    y, x = np.mgrid[0:n, 0:n].astype(F)
    yy = (y + g(n, rng, 1, 3, 1.5) * 0.0020 * n) % n                    # the drums are not quite level
    d = yy / n * drums
    cy = np.floor(d).astype(np.int64) % drums
    ly = (d - np.floor(d)).astype(F)
    cx = np.zeros_like(cy)
    edge = np.minimum(ly, 1.0 - ly) * (n / drums) + 0.6 * g(n, rng, n // 40, n // 10, 0.8)
    stone, h, sm, pit = _stone(n, rng, mm, cx, cy, None, ly, (0.62, 0.58, 0.50), (0.40, 0.38, 0.34), tone_var=0.05,
                               bedding=0.3, pits=0.5, tooling=0.12, roughness=1.0, dome_mm=1.2, hue=0.015, chisel=0.3)
    arris = 1.0 - tl.smooth(0.0, 7.0 * px, edge)
    chip = tl.smooth(1.3, 2.6, g(n, rng, 12, n // 5, 1.0)) * arris
    joint = 1.0 - tl.smooth(5.0 * px * 0.35, 5.0 * px * 1.0, edge)
    streak = stain(n, rng, 3, 0.30, 0.8, aniso=(1, 6)) * (0.4 + 0.6 * tl.smooth(-0.5, 1.2, g(n, rng, 2, 8, 1.2)))
    soot = stain(n, rng, 1, 0.22, 0.9)
    base = tl.mix(stone, (0.66, 0.62, 0.54), chip * 0.5)
    base = tl.mix(base, (0.14, 0.13, 0.11), joint * 0.85)
    base = tl.mix(base, (0.26, 0.25, 0.22), streak * 0.16)
    base = tl.mix(base, (0.12, 0.11, 0.10), soot * 0.16)
    h = h - 2.0 * arris - 4.0 * chip - 2.5 * joint
    base = base * cavity(h, mm)[..., None]
    sm = sm * (1.0 - joint) + 0.06 * joint - 0.04 * soot
    save_mm("pietra_colonna", match(base, 0.380), h, sm, 1.0 - 0.5 * joint - 0.15 * arris - 0.4 * pit, mm)


def quercia_portale(n=1024):
    """Quercia brunita, con fibre verticali, pori e velature di umidità."""
    rng = np.random.default_rng(149)
    phase = 2 * np.pi * (np.arange(n, dtype=F)[None, :] * 47 / n + perlin(n, 8, rng, cells_y=4) * 2.0)
    rings = (np.sin(phase) * 0.5 + 0.5) ** 5
    fiber = perlin(n, 120, rng, cells_y=9) * 0.5 + 0.5
    pores = smooth(0.57, 0.78, perlin(n, 122, rng, cells_y=52) * 0.5 + 0.5)
    cloud = warped(n, rng, 3, 4, 0.23)
    grain = np.clip(0.42 + fiber * 0.5 + cloud * 0.16 - rings * 0.34 - pores * 0.30, 0, 1)
    base = lerp(col((0.10, 0.097, 0.079), n), col((0.37, 0.29, 0.205), n), grain)
    damp = smooth(0.53, 0.81, warped(n, rng, 4, 4, 0.22))
    base = lerp(base, col((0.12, 0.14, 0.107), n), damp * 0.60)
    h = fiber * 0.14 - rings * 0.15 - pores * 0.25
    save("quercia_portale", base, normals(h, 6.2), np.zeros((n, n), F), 1 - pores * 0.25, 0.18 + grain * 0.15 - damp * 0.07)


def _rubble(n, cells, rng, jitter, warp, wmin, wmax):
    """Multiplicatively weighted Voronoi on a torus: rounded stones of different sizes. Returns the distance to the stone border in px
    (approximately), the id of the stone, the offset from its centre and the weighted distance to it (about 0.5 at the border)."""
    pts = 0.5 + (rng.random((cells, cells, 2)).astype(F) - 0.5) * jitter
    wgt = rng.uniform(wmin, wmax, (cells, cells)).astype(F)
    c = (np.arange(n, dtype=F) + 0.5) * cells / n
    cy, cx = np.meshgrid(c, c, indexing="ij")
    cx, cy = cx + warp[0], cy + warp[1]
    ix, iy = np.floor(cx).astype(np.int64), np.floor(cy).astype(np.int64)
    d1 = np.full((n, n), 99.0, F)
    d2 = d1.copy()
    ident = np.zeros((n, n), np.int64)
    w1 = np.ones((n, n), F)
    for dj in range(-2, 3):
        for di in range(-2, 3):
            jx, jy = ix + di, iy + dj
            p = pts[jy % cells, jx % cells]
            w = wgt[jy % cells, jx % cells]
            d = np.hypot(cx - (jx + p[..., 0]), cy - (jy + p[..., 1])) / w
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d))
            ident = np.where(closer, (jy % cells) * cells + (jx % cells), ident)
            w1 = np.where(closer, w, w1)
            d1 = np.minimum(d1, d)
    border = (d2 - d1) * 0.5 * w1 * (n / cells)
    return border.astype(F), ident, d1.astype(F)


def pietra_cripta(n=1024):
    """Rough rubble of the crypt on a 2 m repetition: rounded fieldstones of different sizes, wide dark lime mortar, damp, moss in the
    hollows next to the joints and white salts (nitre) creeping over the stone. The old texture was the cells of a Voronoi diagram, which
    reads as a pattern; here sizes, shapes and surfaces differ from stone to stone."""
    rng = np.random.default_rng(107)
    g = tl.gauss_field
    mm = 2000.0 / n
    px = 1.0 / mm
    cells = 10
    warp = (g(n, rng, 2, 7, 1.2) * 0.06, g(n, rng, 2, 7, 1.2) * 0.06)
    border, ident, d1 = _rubble(n, cells, rng, 0.75, warp, 0.85, 1.2)
    border = border + 2.2 * g(n, rng, n // 40, n // 10, 0.8)                         # wavy joints
    sx, sy = ident % cells, ident // cells
    tone, warm = tl.hash2(sx, sy, 3), tl.hash2(sx, sy, 5)
    cloud = block_shift(g(n, rng, 2, 9, 1.2), sx, sy, 21)
    grain = g(n, rng, n // 6, n // 2, 0.0)
    t = np.clip(0.5 + 0.7 * (tone - 0.5) + 0.12 * cloud + 0.04 * grain, 0.0, 1.0)
    stone = tl.mix((0.17, 0.16, 0.15), (0.34, 0.31, 0.27), t)
    stone = stone * (1.0 + 0.14 * (warm - 0.5)[..., None] * np.array([1.0, 0.3, -1.0], F))
    stone = stone * (1.0 + 0.05 * grain)[..., None]
    pit = tl.smooth(2.7, 3.4, g(n, rng, n // 5, n // 2, 0.0))
    stone = tl.mix(stone, (0.08, 0.075, 0.07), pit * 0.6)
    mw = (8.0 + 14.0 * tl.smooth(-1.0, 1.5, g(n, rng, 2, 9, 1.0))) * px           # joints 8 to 22 mm: rubble is laid in a lot of mortar
    mortar = 1.0 - tl.smooth(mw * 0.30, mw * 0.70, border)
    near = 1.0 - tl.smooth(0.0, 16.0 * px, border)
    damp = tl.smooth(0.7, 2.0, g(n, rng, 1, 5, 1.4))
    moss = tl.smooth(1.0, 2.4, g(n, rng, 2, 9, 1.2)) * (0.35 + 0.65 * near) * (0.75 + 0.25 * tl.smooth(-1.0, 1.0, g(n, rng, n // 8, n // 3, 0.3)))
    nitre = tl.smooth(1.6, 2.6, g(n, rng, 4, 20, 1.0)) * (0.4 + 0.6 * near)
    base = tl.mix(stone, (0.08, 0.085, 0.075), damp * 0.45)
    base = tl.mix(base, (0.15, 0.22, 0.09), moss * 0.62)
    base = tl.mix(base, (0.62, 0.62, 0.57), nitre * 0.28)
    sand = g(n, rng, n // 6, n // 2, 0.0)
    lime = tl.mix((0.11, 0.105, 0.09), (0.22, 0.20, 0.17), tl.smooth(-1.0, 1.0, g(n, rng, 3, 14, 1.0))) * (1.0 + 0.45 * sand)[..., None]
    base = tl.mix(base, lime, mortar * 0.95)
    dome = 1.0 - np.clip(d1 / 0.62, 0.0, 1.0) ** 2
    rough = block_shift(g(n, rng, 8, 40, 1.0), sx, sy, 27)
    h = 5.5 * dome * (0.5 + tone) + 1.5 * rough + 0.25 * g(n, rng, n // 10, n // 3, 0.3) - 1.6 * pit + 0.8 * nitre
    h = h - 3.0 * (1.0 - tl.smooth(0.0, 9.0 * px, border))
    h = h * (1.0 - mortar) + (-7.0 + 1.6 * sand) * mortar
    sm = 0.20 + 0.06 * tone + 0.28 * damp + 0.08 * moss - 0.12 * nitre
    sm = sm * (1.0 - mortar) + 0.10 * mortar
    ao = 1.0 - 0.65 * mortar - 0.18 * near * (1.0 - mortar) - 0.4 * pit
    base = base * cavity(h, mm)[..., None]
    save_mm("pietra_cripta", match(base, 0.203), h, sm, ao, mm)


def mattoni_volta(n=1024):
    """Fired-clay bricks of a vault on a 1.6 m repetition (6 x 16 bricks of 27 x 10 cm). Every brick has its own hue, a coarse sandy
    surface, pits and rounded edges; a few are almost black clinker. Candle smoke has blackened the vault, most of all in the joints and
    on the rough faces. The old texture blurred the soot into clouds over a flat brick pattern."""
    rng = np.random.default_rng(109)
    g = tl.gauss_field
    mm = 1600.0 / n
    px = 1.0 / mm
    cols, rows = 6, 16
    jw = 10.0 * px
    edge, row, bx, lx, ly = wall_bond(n, cols, rows, rng, jw, 0.004)
    tone, kiln = tl.hash2(bx, row, 3), tl.hash2(bx, row, 8)
    clay = tl.mix((0.31, 0.15, 0.11), (0.55, 0.29, 0.18), tone)
    clay = tl.mix(clay, (0.16, 0.10, 0.11), tl.smooth(0.92, 0.98, kiln) * 0.8)                # burnt clinker
    mott = block_shift(g(n, rng, 2, 9, 1.0), bx, row, 31)
    grain = g(n, rng, n // 5, n // 2, 0.0)
    clay = clay * (1.0 + 0.14 * mott + 0.10 * grain)[..., None]
    pit = tl.smooth(2.5, 3.3, g(n, rng, n // 6, n // 2, 0.0))
    clay = tl.mix(clay, (0.10, 0.06, 0.05), pit * 0.6)
    chip = tl.smooth(1.0, 2.4, g(n, rng, 20, 90, 0.9))
    mortar = 1.0 - tl.smooth(jw * 0.30, jw * 0.75, edge - chip * jw * 1.2)
    sand = g(n, rng, n // 6, n // 2, 0.0)
    lime = tl.mix((0.40, 0.37, 0.32), (0.56, 0.52, 0.45), tl.smooth(-1.0, 1.0, g(n, rng, 2, 9, 1.0))) * (1.0 + 0.10 * sand)[..., None]
    base = tl.mix(clay, lime, mortar)
    rough = block_shift(g(n, rng, 8, 40, 1.0), bx, row, 37)
    smoke = stain(n, rng, 1, 0.55, 3.0, detail=2.0) * (0.65 + 0.35 * tl.smooth(-1.0, 1.0, rough)) * (0.85 + 0.15 * mortar)
    smoke = np.clip(smoke + 0.15 * stain(n, rng, 3, 0.25, 1.6, detail=1.6), 0.0, 1.0)
    base = base * (1.0 - 0.80 * smoke)[..., None]
    h = 0.8 * rough + 0.15 * g(n, rng, n // 10, n // 3, 0.3) - 1.4 * pit - 2.0 * (1.0 - tl.smooth(0.0, 6.0 * px, edge)) - 3.0 * chip * (1.0 - tl.smooth(0.0, 8.0 * px, edge))
    h = h * (1.0 - mortar) + (-3.5 + 1.2 * g(n, rng, n // 20, n // 5, 0.8)) * mortar
    base = base * cavity(h, mm)[..., None]
    sm = 0.17 - 0.08 * smoke - 0.04 * pit
    sm = sm * (1.0 - mortar) + 0.08 * mortar
    ao = 1.0 - 0.45 * mortar - 0.4 * pit - 0.1 * smoke
    save_mm("mattoni_volta", match(base, 0.196), h, sm, ao, mm)


def parquet(n=1024):
    """Parquet a listoni di rovere scurito (1,2 m, listoni da 12 cm): giunti sfalsati, graffi, macchie scure, cera consumata."""
    rng = np.random.default_rng(113)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    rows = 10
    rh = n / rows
    row = np.floor(y / rh).astype(int)
    ly = y - row * rh
    # un giunto per riga (a volte due), posizione casuale periodica
    j1 = np.array([rng.random() for _ in range(rows)]) * n
    j2 = (j1 + n * (0.45 + 0.2 * np.array([rng.random() for _ in range(rows)]))) % n
    xs = (x - j1[row]) % n
    L2 = ((j2 - j1) % n)[row]
    plank = (xs > L2).astype(int) + row * 2
    tone = hash2(plank, plank * 0 + 1, 5)
    # fibre lungo x
    fibers = perlin(n, 3, rng, cells_y=220) * 0.5 + 0.5
    rings = np.sin(2 * np.pi * (y / n * 40 + (perlin(n, 2, rng, cells_y=30) * 1.2))) * 0.5 + 0.5
    g = np.clip(0.55 - rings ** 2 * 0.25 + (fibers - 0.5) * 0.2, 0, 1)
    base = lerp(col((0.14, 0.085, 0.045), n), col((0.34, 0.21, 0.11), n), g * (0.55 + 0.7 * tone))
    gap_y = np.minimum(ly, rh - ly)
    gap_x = np.minimum(np.minimum(xs, n - xs), np.abs(xs - L2))
    gap =np.maximum(1 - smooth(0.8, 2.6, gap_y), 1 - smooth(0.8, 2.6, gap_x))
    wear = smooth(0.5, 0.82, fbm(n, 2, 5, rng))
    stain = smooth(0.66, 0.85, fbm(n, 4, 5, rng))
    scr = scratches(n, 300, rng, (0.02, 0.12), 1)
    base = lerp(base, col((0.45, 0.33, 0.20), n), wear * 0.3)
    base = lerp(base, col((0.05, 0.035, 0.025), n), stain * 0.55)
    base = lerp(base, col((0.52, 0.40, 0.26), n), scr * 0.3)
    base = lerp(base, col((0.03, 0.02, 0.015), n), gap * 0.9)
    h = g * 0.25 - gap * 0.3 - scr * 0.1
    sm = 0.34 + wear * 0.2 - stain * 0.2 - scr * 0.2
    save("parquet", base, normals(h, 4.0), np.zeros((n, n), F), 1 - gap * 0.5, sm)


def _tile_grid(n, cells, rng, warp_px):
    """Square tiles whose joints wander a little (nobody laid them with a ruler): tile ids, position inside the tile in px and the
    distance to the nearest joint in px."""
    ts = n / cells
    y, x = np.mgrid[0:n, 0:n].astype(F)
    xx = (x + tl.gauss_field(n, rng, 2, 12, 1.2) * warp_px) % n
    yy = (y + tl.gauss_field(n, rng, 2, 12, 1.2) * warp_px) % n
    ix, iy = np.floor(xx / ts), np.floor(yy / ts)
    lx, ly = xx - ix * ts, yy - iy * ts
    edge = np.minimum(np.minimum(lx, ts - lx), np.minimum(ly, ts - ly))
    return ix.astype(np.int64) % cells, iy.astype(np.int64) % cells, lx, ly, edge


def _chipped_corners(n, rng, cx, cy, lx, ly, ts, share, radius_px):
    """Tiles that lost a corner: 0..1 mask of the missing piece (a ragged quarter-disc at one corner of a fraction ``share`` of the tiles)."""
    has = tl.hash2(cx, cy, 33) > 1.0 - share
    corner = (tl.hash2(cx, cy, 34) * 4).astype(np.int64) % 4
    qx = np.where(corner % 2 == 0, 0.0, ts)
    qy = np.where(corner < 2, 0.0, ts)
    dist = np.hypot(lx - qx, ly - qy) + 0.35 * radius_px * tl.gauss_field(n, rng, 20, 90, 0.8)
    r = radius_px * (0.6 + 0.8 * tl.hash2(cx, cy, 35))
    return (has * (1.0 - tl.smooth(r * 0.85, r * 1.1, dist))).astype(F)


def piastrelle(n=1024):
    """White glazed wall tiles (15 cm, 8 x 8 on a 1.2 m repetition): glossy glaze with a faint orange-peel ripple, pinholes and fine
    crazing, tile-to-tile tone, a few chipped corners down to the biscuit, grout darkened by grime and mould, grease and rust drips.
    The old tiles were a flat grid under big cloud stains."""
    rng = np.random.default_rng(127)
    g = tl.gauss_field
    mm = 1200.0 / n
    px = 1.0 / mm
    cells = 8
    ts = n / cells
    cx, cy, lx, ly, edge = _tile_grid(n, cells, rng, 0.45)
    gw = 3.0 * px
    grout = 1.0 - tl.smooth(gw * 0.35, gw * 0.9, edge)
    grime = 1.0 - tl.smooth(gw * 0.6, gw * 2.6, edge)
    tone = tl.hash2(cx, cy, 21)
    glaze = tl.mix((0.80, 0.82, 0.80), (0.70, 0.74, 0.71), tone) * (1.0 + 0.025 * g(n, rng, 2, 12, 1.0))[..., None]
    peel = g(n, rng, n // 12, n // 4, 0.5)
    craze = tl.contour(g(n, rng, 25, 100, 0.9), 1.1) * tl.smooth(-0.2, 1.0, block_shift(g(n, rng, 1, 3, 1.4), cx, cy, 9))
    pin = tl.smooth(3.0, 3.7, g(n, rng, n // 4, n // 2, 0.0))
    glaze = tl.mix(glaze, (0.52, 0.50, 0.44), craze * 0.35)
    glaze = tl.mix(glaze, (0.35, 0.34, 0.30), pin * 0.5)
    chipped = _chipped_corners(n, rng, cx, cy, lx, ly, ts, 0.07, 22.0 * px / 2.0)
    base = tl.mix(glaze, (0.70, 0.62, 0.50), chipped)
    grease = tl.smooth(1.2, 2.5, g(n, rng, 2, 8, 1.2))
    drip = tl.smooth(1.7, 2.9, g(n, rng, 4, 40, 0.5, aniso=(1, 8)))
    base = tl.mix(base, (0.62, 0.55, 0.34), grease * 0.30)
    base = tl.mix(base, (0.48, 0.30, 0.17), drip * 0.18)
    mould = tl.smooth(0.5, 1.8, g(n, rng, 2, 9, 1.2))
    joint = tl.mix((0.46, 0.45, 0.41), (0.20, 0.23, 0.15), mould * grime * 0.7) * (1.0 + 0.10 * g(n, rng, n // 6, n // 2, 0.0))[..., None]
    base = tl.mix(base, joint, grout)
    base = tl.mix(base, (0.34, 0.32, 0.26), np.clip(grime * (0.05 + 0.12 * mould), 0.0, 0.3) * (1.0 - grout))
    bevel = 1.0 - tl.smooth(0.0, 3.5 * px, edge)
    h = 0.12 * peel - 0.7 * bevel - 1.8 * grout - 0.35 * pin - 1.5 * chipped
    sm = 0.84 + 0.05 * peel - 0.40 * grease - 0.25 * drip - 0.10 * craze - 0.5 * chipped
    sm = sm * (1.0 - grout) + 0.12 * grout
    save_mm("piastrelle", match(base, 0.662), h, sm, 1.0 - 0.45 * grout - 0.15 * grime - 0.2 * chipped, mm)


def piastrelle_pav(n=1024):
    """Checkerboard floor of 30 cm tiles (4 x 4 on a 1.2 m repetition), black and dirty white: speckled stoneware, each tile with its
    tone, scuffs and ground-in dirt, a cracked tile, chipped corners, grout black with grime."""
    rng = np.random.default_rng(131)
    g = tl.gauss_field
    mm = 1200.0 / n
    px = 1.0 / mm
    cells = 4
    ts = n / cells
    cx, cy, lx, ly, edge = _tile_grid(n, cells, rng, 0.5)
    gw = 4.0 * px
    grout = 1.0 - tl.smooth(gw * 0.35, gw * 0.9, edge)
    grime = 1.0 - tl.smooth(gw * 0.6, gw * 2.6, edge)
    black = ((cx + cy) % 2 == 1)
    tone = tl.hash2(cx, cy, 25)
    white = tl.mix((0.66, 0.65, 0.60), (0.50, 0.49, 0.45), tone)
    dark = tl.mix((0.07, 0.07, 0.075), (0.12, 0.11, 0.11), tone)
    cloud = block_shift(g(n, rng, 1, 5, 1.2), cx, cy, 13)
    base = np.where(black[..., None], dark, white) * (1.0 + 0.04 * cloud)[..., None]
    fleck = g(n, rng, n // 4, n // 2, 0.0)
    base = tl.mix(base, (0.26, 0.25, 0.22), tl.smooth(2.9, 3.6, fleck) * 0.5 * (~black))
    base = tl.mix(base, (0.36, 0.35, 0.33), tl.smooth(2.9, 3.6, -fleck) * 0.35 * black)
    dirt = stain(n, rng, 1, 0.30, 1.6)
    base = tl.mix(base, (0.20, 0.16, 0.11), dirt * np.where(black, 0.10, 0.26))
    scuff = tl.strokes(n, rng, 90, (15, 70), 1.6, curve=1.0, weight=(0.3, 0.9))
    base = tl.mix(base, np.where(black[..., None], np.array((0.36, 0.35, 0.33), F), np.array((0.20, 0.18, 0.16), F)), scuff * 0.38)
    crack_tile = tl.hash2(cx, cy, 41) > 0.82
    crack = tl.strokes(n, rng, 40, (60, 160), 0.9, curve=0.8, weight=(0.9, 1.0)) * crack_tile
    base = tl.mix(base, (0.04, 0.04, 0.04), crack * 0.85)
    chipped = _chipped_corners(n, rng, cx, cy, lx, ly, ts, 0.18, 16.0 * px)
    base = tl.mix(base, (0.60, 0.53, 0.43), chipped)
    ground = tl.mix((0.13, 0.12, 0.11), (0.05, 0.05, 0.05), tl.smooth(-1.0, 1.0, g(n, rng, 2, 9, 1.0)))
    base = tl.mix(base, ground, grout)
    base = tl.mix(base, (0.10, 0.09, 0.08), np.clip(grime * 0.07, 0.0, 0.3) * (1.0 - grout) * (~black))
    bevel = 1.0 - tl.smooth(0.0, 3.0 * px, edge)
    h = 0.18 * g(n, rng, n // 12, n // 4, 0.5) - 0.8 * bevel - 2.0 * grout - 1.2 * crack - 1.8 * chipped - 0.3 * scuff
    sm = np.where(black, 0.66, 0.56) - 0.30 * dirt - 0.30 * scuff - 0.15 * tl.smooth(0.0, 1.5, cloud) - 0.45 * chipped
    sm = sm * (1.0 - grout) + 0.10 * grout
    save_mm("piastrelle_pav", match(base, 0.268), h, sm, 1.0 - 0.5 * grout - 0.1 * grime - 0.2 * chipped, mm)


def cemento(n=1024):
    """Service concrete on a 2 m repetition: four 1 m slabs separated by saw cuts, each poured on its own (own tone), mottled and troweled,
    with fine aggregate showing through, air holes, hairline cracks, oil and water stains. The old texture was clouds and a few
    Voronoi cracks."""
    rng = np.random.default_rng(137)
    g = tl.gauss_field
    mm = 2000.0 / n
    px = 1.0 / mm
    cx, cy, lx, ly, edge = _tile_grid(n, 2, rng, 1.2)
    cut = 1.0 - tl.smooth(1.5 * px * 0.35, 1.5 * px * 0.9, edge)                    # saw cuts 3 mm wide
    slab = tl.hash2(cx, cy, 9)
    mott, mid, grain = g(n, rng, 1, 6, 1.2), g(n, rng, 6, 30, 0.8), g(n, rng, n // 6, n // 2, 0.0)
    base = tl.rgb((0.37, 0.365, 0.35), n) * (1.0 + 0.10 * (slab - 0.5) + 0.05 * mott + 0.05 * mid + 0.05 * g(n, rng, 40, n // 4, 0.8) + 0.05 * grain)[..., None]
    agg = g(n, rng, n // 10, n // 3, 0.0)
    base = tl.mix(base, (0.60, 0.58, 0.54), tl.smooth(2.6, 3.4, agg) * 0.45)
    base = tl.mix(base, (0.14, 0.14, 0.14), tl.smooth(2.6, 3.4, -agg) * 0.35)
    hole = tl.smooth(2.9, 3.6, g(n, rng, n // 6, n // 3, 0.0))
    base = tl.mix(base, (0.08, 0.08, 0.08), hole * 0.7)
    stress = 1.0 - tl.smooth(0.0, 60.0 * px, edge)                                  # cracks like to start at the cuts
    crack = tl.contour(g(n, rng, 3, 12, 1.4), 2.2 * px) * np.clip(tl.smooth(0.6, 1.6, g(n, rng, 1, 3, 1.4)) + 0.6 * stress, 0.0, 1.0) * tl.smooth(0.0, 1.0, g(n, rng, 2, 6, 1.2))
    oil = stain(n, rng, 1, 0.045, 0.9)
    wet = stain(n, rng, 1, 0.20, 1.0)
    base = tl.mix(base, (0.08, 0.075, 0.07), oil * 0.38)
    base = tl.mix(base, (0.18, 0.18, 0.17), wet * 0.20)
    base = tl.mix(base, (0.05, 0.05, 0.05), crack * 0.80)
    base = tl.mix(base, (0.06, 0.06, 0.06), cut * 0.90)
    h = 0.35 * mid + 0.08 * grain - 0.9 * hole - 1.0 * crack - 2.5 * cut
    sm = 0.22 + 0.04 * mott + 0.06 * mid + 0.30 * oil + 0.18 * wet - 0.10 * crack
    sm = sm * (1.0 - cut) + 0.10 * cut
    save_mm("cemento", match(base, 0.351), h, sm, 1.0 - 0.40 * cut - 0.50 * hole - 0.20 * crack, mm)


# ---------------------------------------------------------------- vetrate
PALETTES = [
    [(0.16, 0.28, 0.38), (0.48, 0.21, 0.18), (0.65, 0.54, 0.32)],
    [(0.17, 0.28, 0.45), (0.37, 0.22, 0.36), (0.55, 0.64, 0.66)],
    [(0.19, 0.32, 0.34), (0.26, 0.44, 0.29), (0.57, 0.55, 0.33)],
    [(0.19, 0.28, 0.38), (0.49, 0.31, 0.17), (0.66, 0.52, 0.30)],
]


def vetrata(idx, w=512, h=1280):
    """Grisaglia, medaglioni e vetro soffiato, con una griglia di piombo in losanghe."""
    rng = np.random.default_rng(200 + idx)
    field, accent, enamel = (np.broadcast_to(np.array(rgb, F), (h, w, 3)) for rgb in PALETTES[idx])
    y, x = np.mgrid[0:h, 0:w].astype(F)
    u, v = x / w, 1 - y / h
    n = max(h, w)
    ripple = fbm(n, 21, 4, rng)[:h, :w] - 0.5
    glass_flow = fbm(n, 7, 4, rng)[:h, :w] - 0.5
    hammered = fbm(n, 70, 3, rng)[:h, :w] - 0.5
    p = (u - 0.5) * 5
    q = v * (5 * h / w)
    diagonal_a = p + q + ripple * 0.13
    diagonal_b = q - p - ripple * 0.09
    cell_a, cell_b = np.floor(diagonal_a), np.floor(diagonal_b)
    fract_a, fract_b = diagonal_a - cell_a, diagonal_b - cell_b
    diagonal = np.minimum(np.minimum(fract_a, 1 - fract_a), np.minimum(fract_b, 1 - fract_b))
    tone = hash2(cell_a, cell_b, 31 + idx)
    grisaille = np.array((0.48, 0.51, 0.47), F)
    glass = lerp(field, grisaille, smooth(0.18, 0.92, tone) * 0.70)
    lead = 1 - smooth(0.016, 0.045, diagonal)
    border = np.abs(u - 0.5)
    for boundary in (0.355, 0.427):
        lead = np.maximum(lead, 1 - smooth(0.003, 0.009, np.abs(border - boundary)))
    beads = np.sin(q * np.pi) ** 2
    band = smooth(0.35, 0.36, border) * (1 - smooth(0.422, 0.432, border))
    glass = lerp(glass, lerp(accent, enamel, beads * 0.75), band)
    lead = np.maximum(lead, smooth(0.457, 0.481, border))

    # Tre medaglioni policromi, con l'occhio inciso nel vetro del medaglione centrale.
    for center in (0.15, 0.365, 0.635):
        dx, dy = u - 0.5, (v - center) * (h / w)
        radius = np.hypot(dx, dy)
        angle = np.arctan2(dy, dx)
        outer = 0.285 + 0.018 * np.cos(angle * 4)
        inside = 1 - smooth(outer - 0.008, outer + 0.004, radius)
        ring = smooth(0.202, 0.221, radius)
        petal = (np.cos(angle * 4) * 0.5 + 0.5) ** 2
        surround = lerp(accent, enamel, petal * 0.6)
        center_glass = lerp(enamel, np.array((0.73, 0.73, 0.65), F), (1 - smooth(0.0, 0.24, radius)) * 0.6)
        motif = lerp(center_glass, surround, ring)
        inner_lead = 1 - smooth(0.004, 0.010, np.abs(radius - 0.211))
        if center == 0.365:
            almond = np.maximum(np.hypot(dx, dy + 0.168), np.hypot(dx, dy - 0.168)) - 0.235
            eye = 1 - smooth(-0.004, 0.004, almond)
            pupil = 1 - smooth(0.036, 0.044, radius)
            iris = 1 - smooth(0.072, 0.079, radius)
            paint = lerp(np.broadcast_to(np.array((0.69, 0.71, 0.64), F), (h, w, 3)), field, iris)
            paint = lerp(paint, np.array((0.05, 0.06, 0.055), F), pupil)
            motif = lerp(motif, paint, eye)
            inner_lead = np.maximum(inner_lead, 1 - smooth(0.003, 0.008, np.abs(almond)))
        else:
            foil = radius - (0.102 + np.cos(angle * 4) * 0.035)
            motif = lerp(motif, field, (1 - smooth(-0.006, 0.005, foil)) * 0.77)
            inner_lead = np.maximum(inner_lead, 1 - smooth(0.004, 0.009, np.abs(foil)))
        glass = lerp(glass, motif, inside)
        lead = np.maximum(lead * (1 - inside), np.maximum(inner_lead * inside, 1 - smooth(0.004, 0.010, np.abs(radius - outer))))

    for joint in (0.25, 0.48):
        lead = np.maximum(lead, 1 - smooth(0.002, 0.005, np.abs(v - joint)))
    # Trasmissione disomogenea: onde del vetro, velature dipinte e bollicine.
    bubbles = smooth(0.19, 0.34, hammered)
    transmission = 0.88 + glass_flow * 0.65 + ripple * 0.43 + hammered * 0.14 - bubbles * 0.19
    glass = glass * transmission[..., None]
    patina = lerp(np.array((0.026, 0.030, 0.032), F), np.array((0.10, 0.106, 0.098), F), smooth(0.26, 0.82, lead + ripple * 0.5)[..., None] * 0.65)
    img = np.clip(lerp(glass, patina, lead), 0, 1)
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(OUT / f"vetrata{idx}.png")
    print(f"vetrata{idx}: {w}x{h}")


def vetrata0(): vetrata(0)
def vetrata1(): vetrata(1)
def vetrata2(): vetrata(2)
def vetrata3(): vetrata(3)


def vetrata_chiara(w=384, h=960):
    """Vetro chiaro colato a mano, in piccole lastre montate a piombo."""
    rng = np.random.default_rng(215)
    y, x = np.mgrid[0:h, 0:w].astype(F)
    u, v = x / w, 1 - y / h
    flow = fbm(h, 11, 4, rng)[:, :w] - 0.5
    pits = fbm(h, 72, 3, rng)[:, :w] - 0.5
    col_id = u > 0.5
    row_id = np.searchsorted([0.25, 0.48, 0.68], v)
    shade = hash2(col_id, row_id, 43)
    strength = np.clip(0.71 + shade * 0.22 + flow * 0.49 + pits * 0.16, 0, 1)
    glass = strength[..., None] * np.array((0.88, 0.94, 1.0), F)
    edge = np.minimum(np.minimum(u, 1 - u), np.abs(u - 0.5 + flow * 0.006))
    lead = 1 - smooth(0.009, 0.021, edge)
    for seam in (0.25, 0.48, 0.68):
        lead = np.maximum(lead, 1 - smooth(0.002, 0.007, np.abs(v - seam + flow * 0.005)))
    img = lerp(glass, np.array((0.070, 0.080, 0.084), F), lead)
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(OUT / "vetrata_chiara.png")


def anteprima(path):
    names = ["pietra_muro", "pietra_lastre", "pietra_massello", "pietra_colonna", "pietra_cripta", "mattoni_volta", "parquet", "piastrelle", "piastrelle_pav", "cemento"]
    tiles = []
    for nm in names:
        b = Image.open(OUT / f"{nm}_base.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        nr = Image.open(OUT / f"{nm}_normal.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        mk = Image.open(OUT / f"{nm}_mask.png").getchannel("A").convert("RGB").resize((256, 256), Image.LANCZOS)
        row = Image.new("RGB", (256 * 3 + 8, 256), (0, 0, 0))
        row.paste(b, (0, 0)); row.paste(nr, (260, 0)); row.paste(mk, (520, 0))
        tiles.append(row)
    vet = [Image.open(OUT / f"vetrata{i}.png").convert("RGB").resize((205, 512), Image.LANCZOS) for i in range(4)]
    rows = (len(tiles) + 1) // 2
    sheet = Image.new("RGB", (tiles[0].width * 2 + 8, 264 * rows + 520), (0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 2) * (t.width + 8), (i // 2) * 264))
    for i, im in enumerate(vet):
        sheet.paste(im, (i * 215, 264 * rows + 4))
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
    only = None
    if "--solo" in rest:
        k = rest.index("--solo")
        only = set(rest[k + 1].split(","))
        rest = rest[:k] + rest[k + 2:]
    if rest:
        OUT = Path(rest[0])
    gt.OUT = OUT
    for fn in (pietra_muro, pietra_lastre, pietra_massello, pietra_colonna, quercia_portale, pietra_cripta, mattoni_volta, parquet, piastrelle, piastrelle_pav, cemento,
               vetrata0, vetrata1, vetrata2, vetrata3, vetrata_chiara):
        if only is None or fn.__name__ in only:
            fn()
    if preview:
        anteprima(preview)
