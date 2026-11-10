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


def pietra_muro(n=2048):
    """Corsi di calcare irregolari e riprese di malta su un modulo di 4,2 m."""
    rng = np.random.default_rng(101)
    jw = n / 4.2 * 0.013
    y, x = np.mgrid[0:n, 0:n].astype(F)
    xx = (x + (fbm(n, 17, 4, rng) - 0.5) * 0.020 * n) % n
    yy = (y + (fbm(n, 13, 4, rng) - 0.5) * 0.016 * n) % n
    courses = np.array([0, .085, .13, .227, .312, .37, .475, .555, .62, .733, .812, .861, .939, 1], F) * n
    edge, row, bx, _, _ = irregular_courses(xx, yy, courses, rng, (0.55, 1.6), lambda i: 6 + (i * 5) % 4)
    chipped = smooth(0.54, 0.78, fbm(n, 43, 4, rng))
    edge -= chipped * jw * 1.7
    mortar = 1 - smooth(jw * 0.34, jw * 1.25, edge)
    tone = hash2(bx, row, 7)
    cloud = warped(n, rng, 3, 5, 0.3)
    fine = fbm(n, 24, 4, rng)
    base = lerp(col((0.51, 0.48, 0.42), n), col((0.31, 0.32, 0.29), n), np.clip(tone * 1.3 - 0.15, 0, 1))
    base = lerp(base, col((0.64, 0.60, 0.52), n), smooth(0.50, 0.84, cloud) * 0.5)
    base = base * (0.69 + 0.54 * fine[..., None])
    runoff = smooth(0.43, 0.78, perlin(n, 19, rng, cells_y=3) * 0.5 + 0.5)
    base = lerp(base, col((0.28, 0.27, 0.23), n), runoff * 0.42)
    # fuliggine e umido a chiazze grandi (il resto lo fanno le decalcomanie)
    soot = smooth(0.55, 0.85, fbm(n, 2, 5, rng))
    damp = smooth(0.62, 0.88, fbm(n, 3, 5, rng))
    base = lerp(base, col((0.13, 0.12, 0.11), n), soot * 0.45)
    base = lerp(base, col((0.20, 0.25, 0.17), n), damp * 0.22)
    repair = smooth(0.51, 0.73, warped(n, rng, 5, 4, 0.16))
    base = lerp(base, col((0.47, 0.44, 0.38), n), repair * 0.62)
    joint = lerp(col((0.26, 0.24, 0.20), n), col((0.46, 0.43, 0.37), n), repair * 0.85)
    base = lerp(base, joint * (0.76 + fine[..., None] * 0.44), mortar * 0.9)
    # rilievo: ogni blocco un po' bombato, giunti incavati, scheggiature ai bordi
    bevel = smooth(0, jw * 5, edge)
    pit = (fbm(n, 70, 3, rng) - 0.5) * 0.19 + (fbm(n, 16, 4, rng) - 0.5) * 0.25
    chips = smooth(0.72, 0.9, fbm(n, 20, 3, rng)) * (1 - smooth(0, jw * 8, edge))
    h = bevel * 0.24 + pit + (tone - 0.5) * 0.10 - chips * 0.22 - mortar * (0.19 - repair * 0.10)
    sm = 0.16 + 0.10 * (1 - soot) - mortar * 0.08 + damp * 0.08
    ao = 1 - mortar * 0.5 - chips * 0.2
    save("pietra_muro", base, normals(h, 11.0), np.zeros((n, n), F), ao, sm)


def pietra_lastre(n=2048):
    """Lastre su 4,8 m, disposte a corsi di larghezza e sfalsatura diversa."""
    rng = np.random.default_rng(103)
    jw = n / 4.8 * 0.007
    y, x = np.mgrid[0:n, 0:n].astype(F)
    xx = (x + (fbm(n, 14, 3, rng) - 0.5) * 0.012 * n) % n
    yy = (y + (fbm(n, 11, 4, rng) - 0.5) * 0.011 * n) % n
    courses = np.array([0, 0.16, 0.34, 0.57, 0.70, 0.87, 1], F) * n
    edge, cy, cx, lx, ly = irregular_courses(xx, yy, courses, rng, (0.55, 1.5), lambda row: 4 + row % 3)
    grain = fbm(n, 86, 3, rng)
    chipped = smooth(0.51, 0.78, fbm(n, 21, 4, rng))
    grout = 1 - smooth(jw * 0.35, jw * 1.65, edge - chipped * jw * 1.6)
    tone = hash2(cx, cy, 11)
    cloud = warped(n, rng, 3, 5, 0.3)
    fine = fbm(n, 32, 4, rng)
    base = lerp(col((0.51, 0.47, 0.39), n), col((0.34, 0.36, 0.32), n), tone)
    base = lerp(base, col((0.59, 0.53, 0.43), n), smooth(0.44, 0.78, cloud) * 0.57)
    base = base * (0.69 + 0.48 * fine[..., None] + 0.15 * grain[..., None])
    # lastre consumate al centro (piu' chiare e lucide)
    uc = np.hypot(lx - 0.5, ly - 0.5)
    worn = (1 - smooth(0.08, 0.5, uc)) * (0.4 + 0.6 * smooth(0.3, 0.7, fbm(n, 3, 4, rng)))
    base = lerp(base, col((0.56, 0.52, 0.44), n), worn * 0.35)
    # crepe: ramificate, dentro le lastre
    d1, d2, _ = voronoi(n, 9, rng)
    crack = (1 - smooth(0.0, 0.018, d2 - d1 + (grain - 0.5) * 0.025)) * smooth(0.5, 0.69, fbm(n, 8, 4, rng))
    dirt = smooth(0.55, 0.85, fbm(n, 3, 5, rng))
    base = lerp(base, col((0.16, 0.15, 0.14), n), dirt * 0.4)
    calcite = smooth(0.62, 0.8, fbm(n, 15, 5, rng)) * smooth(0.36, 0.67, grain)
    base = lerp(base, col((0.64, 0.60, 0.51), n), calcite * 0.65)
    base = lerp(base, col((0.22, 0.19, 0.16), n), crack * 0.52)
    mortar = lerp(col((0.28, 0.24, 0.20), n), col((0.46, 0.40, 0.33), n), cloud)
    base = lerp(base, mortar, grout * 0.86)
    bevel = smooth(0, jw * 5, edge - chipped * jw * 1.6)
    h = bevel * 0.15 + (grain - 0.5) * 0.19 + (cloud - 0.5) * 0.22 - crack * 0.12 - grout * 0.085
    sm = 0.18 + worn * 0.24 - dirt * 0.12 - crack * 0.11
    sm = sm * (1 - grout) + 0.1 * grout
    save("pietra_lastre", base, normals(h, 13.0), np.zeros((n, n), F), 1 - grout * 0.27 - crack * 0.14, sm)


def pietra_massello(n=1024):
    """Calcare scolpito senza fughe, per conci, capitelli e cornici con giunti in geometria."""
    rng = np.random.default_rng(104)
    cloud = warped(n, rng, 4, 5, 0.28)
    grain = fbm(n, 68, 4, rng)
    pits = smooth(0.54, 0.79, fbm(n, 34, 4, rng))
    base = lerp(col((0.29, 0.275, 0.25), n), col((0.63, 0.58, 0.49), n), cloud)
    base *= 0.71 + grain[..., None] * 0.49
    base = lerp(base, col((0.16, 0.155, 0.13), n), pits * 0.42)
    h = 0.3 * cloud + 0.21 * grain - 0.27 * pits
    save("pietra_massello", base, normals(h, 7.5), np.zeros((n, n), F), 1 - pits * 0.4, 0.16 + 0.15 * cloud)


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


def pietra_cripta(n=1024):
    """Pietra grezza e umida della cripta: ciottoli irregolari (Voronoi), malta nera, muschio e salnitro."""
    rng = np.random.default_rng(107)
    d1, d2, ident = voronoi(n, 12, rng)
    mortar = 1 - smooth(0.0, 0.07, d2 - d1)
    tone = ((ident * 2654435761) & 0xFFFF).astype(F) / 65535.0
    cloud = warped(n, rng, 3, 5, 0.3)
    fine = fbm(n, 40, 4, rng)
    base = lerp(col((0.30, 0.28, 0.25), n), col((0.17, 0.16, 0.15), n), tone)
    base = lerp(base, col((0.42, 0.40, 0.35), n), smooth(0.5, 0.85, cloud) * 0.3)
    base = base * (0.8 + 0.35 * fine[..., None])
    damp = smooth(0.5, 0.82, fbm(n, 3, 5, rng))
    moss = smooth(0.62, 0.85, fbm(n, 5, 5, rng)) * (1 - mortar)
    nitre = smooth(0.75, 0.92, fbm(n, 9, 4, rng))
    base = lerp(base, col((0.10, 0.11, 0.09), n), damp * 0.5)
    base = lerp(base, col((0.16, 0.24, 0.10), n), moss * 0.55)
    base = lerp(base, col((0.62, 0.62, 0.58), n), nitre * 0.25)
    base = lerp(base, col((0.05, 0.05, 0.05), n), mortar * 0.8)
    dome = smooth(0.0, 0.5, d1)
    h = (1 - dome) * 0.7 * (1 - mortar) + (fbm(n, 36, 3, rng) - 0.5) * 0.25 - mortar * 0.4
    sm = 0.22 + damp * 0.28 - mortar * 0.15
    save("pietra_cripta", base, normals(h, 9.0), np.zeros((n, n), F), 1 - mortar * 0.6, sm)


def mattoni_volta(n=1024):
    """Volta di mattoni anneriti dal fumo delle candele: 4 x 12 mattoni su 1,6 m, giunti chiari consumati."""
    rng = np.random.default_rng(109)
    jw = n / 1.6 * 0.01
    edge, row, bx, lx, ly = wall_bond(n, 4, 12, rng, jw, 0.006)
    mortar = 1 - smooth(jw * 0.4, jw * 1.5, edge)
    tone = hash2(bx, row, 3)
    cloud = warped(n, rng, 3, 5, 0.3)
    fine = fbm(n, 36, 4, rng)
    base = lerp(col((0.40, 0.22, 0.15), n), col((0.26, 0.15, 0.11), n), tone)
    base = base * (0.8 + 0.4 * fine[..., None])
    soot = smooth(0.35, 0.8, cloud)
    base = lerp(base, col((0.05, 0.045, 0.04), n), soot * 0.7)
    base = lerp(base, col((0.36, 0.33, 0.28), n), mortar * 0.8)
    h = smooth(0, jw * 4, edge) * 0.4 * (1 - mortar) + (fbm(n, 40, 3, rng) - 0.5) * 0.2 - mortar * 0.25
    save("mattoni_volta", base, normals(h, 6.0), np.zeros((n, n), F), 1 - mortar * 0.4, 0.14 + (1 - soot) * 0.08)


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


def piastrelle(n=1024):
    """Piastrelle bianche da cucina (15 cm, 8 x 8 su 1,2 m), fughe scure, macchie di grasso, scheggiature."""
    rng = np.random.default_rng(127)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    cell = n / 8
    cx, cy = np.floor(x / cell).astype(int), np.floor(y / cell).astype(int)
    lx, ly = x - cx * cell, y - cy * cell
    edge = np.minimum(np.minimum(lx, cell - lx), np.minimum(ly, cell - ly))
    gw = n / 1.2 * 0.004
    grout = 1 - smooth(gw * 0.5, gw * 1.8, edge)
    tone = hash2(cx, cy, 21)
    base = lerp(col((0.78, 0.80, 0.78), n), col((0.64, 0.67, 0.64), n), tone)
    grease = smooth(0.6, 0.85, fbm(n, 3, 5, rng))
    drip = smooth(0.7, 0.95, fbm(n, 6, 4, rng))
    base = lerp(base, col((0.40, 0.36, 0.22), n), grease * 0.4)
    base = lerp(base, col((0.30, 0.24, 0.14), n), drip * 0.25)
    base = lerp(base, col((0.20, 0.19, 0.17), n), grout)
    h = smooth(0, gw * 3, edge) * 0.4 - grout * 0.3 + (fbm(n, 32, 2, rng) - 0.5) * 0.04
    sm = 0.82 - grease * 0.4 - grout * 0.5
    save("piastrelle", base, normals(h, 5.0), np.zeros((n, n), F), 1 - grout * 0.4, sm)


def piastrelle_pav(n=1024):
    """Pavimento a scacchi nero e bianco sporco da 30 cm (4 x 4 su 1,2 m), usurato, fughe nere."""
    rng = np.random.default_rng(131)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    cell = n / 4
    cx, cy = np.floor(x / cell).astype(int), np.floor(y / cell).astype(int)
    lx, ly = x - cx * cell, y - cy * cell
    edge = np.minimum(np.minimum(lx, cell - lx), np.minimum(ly, cell - ly))
    gw = n / 1.2 * 0.005
    grout = 1 - smooth(gw * 0.5, gw * 1.8, edge)
    black = (cx + cy) % 2 == 1
    tone = hash2(cx, cy, 25)
    wht = lerp(col((0.62, 0.61, 0.56), n), col((0.46, 0.45, 0.41), n), tone)
    blk = lerp(col((0.06, 0.06, 0.065), n), col((0.11, 0.10, 0.10), n), tone)
    base = np.where(black[..., None], blk, wht)
    dirt = smooth(0.55, 0.85, fbm(n, 3, 5, rng))
    scr = scratches(n, 260, rng, (0.02, 0.1), 1)
    base = lerp(base, col((0.17, 0.14, 0.10), n), dirt * 0.45)
    base = lerp(base, col((0.55, 0.53, 0.5), n), scr * np.where(black, 0.35, 0.08))
    base = lerp(base, col((0.04, 0.04, 0.04), n), grout)
    h = smooth(0, gw * 3, edge) * 0.3 - grout * 0.3 - scr * 0.04
    sm = 0.55 - dirt * 0.3 - grout * 0.4
    save("piastrelle_pav", base, normals(h, 4.0), np.zeros((n, n), F), 1 - grout * 0.4, sm)


def cemento(n=1024):
    """Cemento di servizio: grigio, macchie d'olio e d'acqua, crepe sottili (2 m)."""
    rng = np.random.default_rng(137)
    cloud = warped(n, rng, 3, 5, 0.3)
    fine = fbm(n, 48, 4, rng)
    base = lerp(col((0.30, 0.30, 0.29), n), col((0.44, 0.43, 0.41), n), cloud)
    base = base * (0.85 + 0.25 * fine[..., None])
    oil = smooth(0.7, 0.9, fbm(n, 4, 5, rng))
    wet = smooth(0.62, 0.85, fbm(n, 3, 5, rng))
    d1, d2, _ = voronoi(n, 4, rng)
    crack = (1 - smooth(0.0, 0.012, d2 - d1)) * smooth(0.5, 0.7, fbm(n, 4, 4, rng))
    base = lerp(base, col((0.07, 0.06, 0.06), n), oil * 0.6)
    base = lerp(base, col((0.15, 0.15, 0.15), n), wet * 0.4)
    base = lerp(base, col((0.05, 0.05, 0.05), n), crack * 0.8)
    h = (fbm(n, 64, 3, rng) - 0.5) * 0.2 + (fbm(n, 8, 3, rng) - 0.5) * 0.1 - crack * 0.3
    sm = 0.25 + wet * 0.25 + oil * 0.2 - crack * 0.15
    save("cemento", base, normals(h, 4.0), np.zeros((n, n), F), 1 - crack * 0.3, sm)


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
    names = ["pietra_muro", "pietra_lastre", "pietra_cripta", "mattoni_volta", "parquet", "piastrelle", "piastrelle_pav", "cemento"]
    tiles = []
    for nm in names:
        b = Image.open(OUT / f"{nm}_base.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        nr = Image.open(OUT / f"{nm}_normal.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        mk = Image.open(OUT / f"{nm}_mask.png").getchannel("A").convert("RGB").resize((256, 256), Image.LANCZOS)
        row = Image.new("RGB", (256 * 3 + 8, 256), (0, 0, 0))
        row.paste(b, (0, 0)); row.paste(nr, (260, 0)); row.paste(mk, (520, 0))
        tiles.append(row)
    vet = [Image.open(OUT / f"vetrata{i}.png").convert("RGB").resize((205, 512), Image.LANCZOS) for i in range(4)]
    sheet = Image.new("RGB", (tiles[0].width * 2 + 8, 264 * 4 + 520), (0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % 2) * (t.width + 8), (i // 2) * 264))
    for i, im in enumerate(vet):
        sheet.paste(im, (i * 215, 264 * 4 + 4))
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
    for fn in (pietra_muro, pietra_lastre, pietra_massello, quercia_portale, pietra_cripta, mattoni_volta, parquet, piastrelle, piastrelle_pav, cemento,
               vetrata0, vetrata1, vetrata2, vetrata3, vetrata_chiara):
        if only is None or fn.__name__ in only:
            fn()
    if preview:
        anteprima(preview)
