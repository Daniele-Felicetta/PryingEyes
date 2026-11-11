"""Costumi dei quattro nemici dei piani a tema (fase 4): la stoffa del monaco ridipinta.

Maschera e Direttore (teatro), Infermiere e Primario (sanatorio) sono il monaco con un altro vestito: stessa mesh, stesse
UV, stessa normal map delle pieghe (Models/Monaco/Textures/monaco_stoffa_normal.png: non se ne fa una copia). Qui si
scrivono solo il colore (base) e la maschera (R metallico, G occlusione, A lucidita') di ogni costume. Il disegno e'
valutato in 3D: per ogni triangolo della stoffa (dump in ../abate/stoffa_tris.npz, scritto da genera_abate.py) i pixel
dell'atlante si convertono nella posizione a riposo e li' si decide il colore (rombi dell'arlecchino, gessato,
camice, macchie). Cosi' il disegno prosegue attraverso le isole UV senza cuciture. La luminosita' dell'originale
(pieghe, sporco, usura) resta e modula i colori nuovi.

uso: python -I genera_costumi_tex.py [maschera direttore infermiere primario] [--anteprima cartella]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
MONK_TEX = HERE.parent / "monaco" / "export" / "textures"
TRIS = HERE.parent / "abate" / "stoffa_tris.npz"
OUT = Path(r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Models\Temi\Textures")
F = np.float32
N = 2048


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def col(r, g, b):
    return np.array([r, g, b], F)


# ---------------------------------------------------------------- rumore 3D (a reticolo, interpolato)
def hash3(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 2147483647 + seed * 1274126177) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return (h & 0xFFFF).astype(F) / 65535.0


def vnoise(P, freq, seed=0):
    q = P * freq
    i = np.floor(q).astype(np.int64)
    f = (q - i).astype(F)
    u = f * f * (3 - 2 * f)
    out = np.zeros(len(P), F)
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (u[:, 0] if dx else 1 - u[:, 0]) * (u[:, 1] if dy else 1 - u[:, 1]) * (u[:, 2] if dz else 1 - u[:, 2])
                out += w * hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out


def fbm(P, f0, octaves=4, seed=0):
    out = np.zeros(len(P), F)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        out += amp * vnoise(P, f0 * (2 ** o), seed + 17 * o)
        tot += amp
        amp *= 0.5
    return out / tot


# ---------------------------------------------------------------- la geometria del monaco (a riposo, metri)
# guarda verso -Y, la sua sinistra e' +X, l'asse della tonaca e' circa x = 0, y = 0.02
Y0 = 0.02
ROBE_Z = np.array([0.012, 0.40, 0.70, 0.90, 1.05, 1.16], F)
ROBE_R = np.array([0.350, 0.255, 0.215, 0.195, 0.165, 0.150], F)
WRIST = {"r": np.array([-0.215, -0.12, 0.77], F), "l": np.array([0.30, -0.285, 1.02], F)}


class Px:
    """I pixel dell'atlante che toccano la stoffa, con posizione 3D e regione."""

    def __init__(self, P, region, shade, lum):
        self.P = P
        self.x, self.y, self.z = P[:, 0], P[:, 1] - Y0, P[:, 2]
        self.region = region
        self.theta = np.arctan2(self.x, -self.y)             # 0 davanti, cresce verso +X
        self.front = self.y < 0.0
        self.robe, self.sleeve, self.hood = region == 0, region == 1, region == 2
        self.r = np.interp(self.z, ROBE_Z, ROBE_R).astype(F)
        self.s = self.theta * self.r                          # ascissa curvilinea sulla tonaca (m)
        wr = np.where((self.x < 0)[:, None], WRIST["r"], WRIST["l"])
        self.dw = np.linalg.norm(P - wr, axis=1)              # distanza dal polso (maniche)
        self.shade = shade                                    # 0.4 .. 1.2 dalla luminosita' originale
        self.lum = lum


class Canvas:
    def __init__(self, base, mask):
        self.c = base.copy()
        self.met = mask[:, 0].copy()
        self.smo = mask[:, 3].copy()

    def put(self, w, color, met=None, smo=None, shade=None):
        w = np.clip(w, 0, 1).astype(F)[:, None]
        color = np.broadcast_to(np.asarray(color, F), self.c.shape)
        if shade is not None:
            color = color * shade[:, None]
        self.c = self.c * (1 - w) + color * w
        if met is not None:
            self.met = self.met * (1 - w[:, 0]) + met * w[:, 0]
        if smo is not None:
            self.smo = self.smo * (1 - w[:, 0]) + smo * w[:, 0]


def lines(d, width, soft=0.35):
    """1 sulla linea (d = distanza dalla linea in metri), 0 oltre 'width'."""
    return 1 - smooth(width * (1 - soft), width, np.abs(d))


def pick(idx, palette, weights=None):
    """Colore da una tavolozza con un indice casuale per cella (idx in [0,1))."""
    pal = np.array(palette, F)
    if weights is None:
        weights = np.ones(len(pal))
    cum = np.cumsum(np.array(weights, float) / np.sum(weights))
    k = np.searchsorted(cum, idx.clip(0, 0.9999))
    return pal[k]


def stains(px, seed, rust, blood, amount=1.0, lower=1.0):
    """Macchie d'umido, iodio e sangue rappreso: (peso_ruggine, peso_sangue)."""
    n1 = fbm(px.P, 2.6, 4, seed)
    n2 = fbm(px.P, 5.0, 3, seed + 5)
    low = smooth(1.1, 0.2, px.z) * lower + (1 - lower)
    w_rust = smooth(0.62, 0.74, n1 * 0.7 + n2 * 0.3 + (low - 0.5) * 0.18) * amount
    w_blood = smooth(0.74, 0.80, n2 * 0.55 + n1 * 0.45 + (low - 0.5) * 0.20) * amount
    return w_rust, w_blood


# ---------------------------------------------------------------- Maschera (teatro): arlecchino stinto
def maschera(px, cv):
    cream = col(0.70, 0.62, 0.46)
    pal = [col(0.36, 0.05, 0.07), col(0.66, 0.58, 0.42), col(0.06, 0.22, 0.24), col(0.60, 0.40, 0.07), col(0.035, 0.035, 0.045)]
    wts = [0.30, 0.20, 0.20, 0.15, 0.15]
    cols = 10                                                 # rombi attorno al giro (intero: niente cucitura sul retro)
    a = px.theta / (2 * math.pi) * cols
    hz = 0.115
    u, v = a + px.z / hz, a - px.z / hz
    ci, cj = np.floor(u).astype(np.int64), np.floor(v).astype(np.int64)
    hsh = hash3(ci % cols, cj % cols, np.zeros_like(ci), 3)
    cell = pick(hsh, pal, wts)
    fu, fv = u - ci, v - cj
    dcell = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv))
    ink = 1 - smooth(0.035, 0.075, dcell)
    robe = px.robe & (px.z > 0.40)
    cv.put(robe, cell, shade=px.shade * 1.05)
    cv.put(robe * ink, col(0.03, 0.03, 0.035), shade=px.shade)
    # sotto la fascia degli strappi il vestito diventa un sacco bianco sporco
    cv.put(px.robe & (px.z <= 0.40), cream, shade=px.shade * 0.85)
    # cintura di corda dorata a vita
    for zc, zw in ((0.62, 0.014), (0.575, 0.010)):
        cv.put(px.robe * lines(px.z - zc, zw), col(0.78, 0.58, 0.20), met=0.85, smo=0.62, shade=px.shade)
    # maniche: panna con righe bordeaux, polsini a gorgiera chiari
    st = (np.abs(((px.theta * 6.0 / math.pi + px.z * 1.5) % 1.0) - 0.5) < 0.16)
    cv.put(px.sleeve, cream, shade=px.shade * 0.95)
    cv.put(px.sleeve * st, col(0.36, 0.05, 0.07), shade=px.shade)
    cv.put(px.sleeve * (px.dw < 0.22), col(0.74, 0.70, 0.60), shade=px.shade * 0.95)
    cv.put(px.sleeve * lines(px.dw - 0.22, 0.012), col(0.78, 0.58, 0.20), met=0.85, smo=0.62, shade=px.shade)
    # cappuccio da giullare: spicchi bordeaux e panna; il bordo basso (mantellina) a rombi neri
    g = np.floor(px.theta / (2 * math.pi) * 12.0).astype(np.int64) % 2
    cv.put(px.hood, np.where((g == 0)[:, None], col(0.34, 0.05, 0.07), col(0.68, 0.60, 0.44)), shade=px.shade)
    cv.put(px.hood * lines((((px.theta / (2 * math.pi) * 12.0) + 0.5) % 1.0 - 0.5) * 0.2, 0.004), col(0.03, 0.03, 0.035), shade=px.shade)
    cv.put(px.hood * (px.z < 1.12) * smooth(1.12, 1.07, px.z), col(0.04, 0.04, 0.05), shade=px.shade)
    # polvere e umido: scurisce in basso, qualche alone
    rust, blood = stains(px, 11, None, None, amount=0.8)
    cv.put(rust * (px.z < 1.0), col(0.20, 0.15, 0.08), shade=px.shade, smo=0.15)
    dirt = smooth(0.55, 0.0, px.z)
    cv.put(dirt * 0.45, col(0.05, 0.045, 0.04))


# ---------------------------------------------------------------- Direttore (teatro): frac nero da direttore d'orchestra
def direttore(px, cv):
    black = col(0.040, 0.040, 0.050)
    cv.put(px.robe | px.sleeve | px.hood, black, shade=px.shade * 0.9, smo=0.38)
    # gessato: righe grigio-ardesia ogni 2.4 cm
    a = px.s / 0.024
    gess = lines(((a % 1.0) - 0.5) * 0.024, 0.0022)
    cv.put((px.robe | px.hood) * gess * 0.55, col(0.30, 0.30, 0.34), shade=px.shade)
    cv.put(px.sleeve * lines((((px.z + px.x) / 0.024) % 1.0 - 0.5) * 0.024, 0.0022) * 0.55, col(0.30, 0.30, 0.34), shade=px.shade)
    # sparato bianco a V sul petto, con tre borchie d'oro
    vw = 0.012 + 0.115 * smooth(0.62, 1.05, px.z)
    bib = px.robe & px.front & (np.abs(px.x) < vw) & (px.z > 0.62) & (px.z < 1.06)
    cv.put(bib, col(0.80, 0.78, 0.70), shade=px.shade * 1.0, smo=0.45)
    cv.put(bib * (np.abs(px.x) > vw - 0.008), col(0.12, 0.12, 0.14), shade=px.shade)
    for zc in (0.96, 0.86, 0.76):
        d = np.hypot(px.x, px.z - zc)
        cv.put(bib * (1 - smooth(0.011, 0.014, d)), col(0.80, 0.60, 0.20), met=0.9, smo=0.72, shade=px.shade)
    # fusciacca rossa di raso a vita, sopra gli strappi
    sash = px.robe & (px.z > 0.50) & (px.z < 0.62)
    cv.put(sash, col(0.34, 0.02, 0.045), shade=px.shade * 1.15, smo=0.78)
    cv.put(sash * lines(px.z - 0.56, 0.0035) * 0.4, col(0.60, 0.07, 0.09), shade=px.shade, smo=0.85)
    # polsini: treccia d'oro e bordo bianco
    cv.put(px.sleeve * lines(px.dw - 0.20, 0.014), col(0.80, 0.60, 0.20), met=0.88, smo=0.66, shade=px.shade)
    cv.put(px.sleeve * lines(px.dw - 0.245, 0.007), col(0.80, 0.60, 0.20), met=0.88, smo=0.66, shade=px.shade)
    cv.put(px.sleeve * (px.dw < 0.17), col(0.78, 0.76, 0.70), shade=px.shade, smo=0.4)
    # filetto rosso lungo il bordo basso del cappuccio, collarino bianco
    cv.put(px.hood * lines(px.z - 1.075, 0.006), col(0.45, 0.04, 0.06), shade=px.shade, smo=0.7)
    # polvere di palcoscenico: grigio sulle spalle e sul fondo
    dust = fbm(px.P, 4.0, 3, 21)
    cv.put(smooth(0.5, 0.8, dust) * 0.35 * (1 - px.front * 0.4), col(0.22, 0.21, 0.20), smo=0.1)
    cv.put(smooth(0.45, 0.0, px.z) * 0.5, col(0.09, 0.075, 0.065))


# ---------------------------------------------------------------- Infermiere (sanatorio): grembiule macchiato e croce rossa
def infermiere(px, cv):
    linen = col(0.66, 0.70, 0.62)
    cv.put(px.robe | px.hood, linen, shade=px.shade * 1.05, smo=0.30)
    cv.put(px.sleeve, col(0.56, 0.66, 0.60), shade=px.shade * 1.0, smo=0.30)
    # grembiule: pannello piu' chiaro davanti, con orlo e lacci
    ap_w = 0.115 + 0.05 * smooth(0.5, 1.05, px.z) * 0.0
    apron = px.robe & px.front & (np.abs(px.x) < ap_w) & (px.z > 0.46)
    cv.put(apron, col(0.82, 0.82, 0.76), shade=px.shade * 1.05)
    cv.put(apron * (np.abs(np.abs(px.x) - ap_w) < 0.006), col(0.42, 0.48, 0.44), shade=px.shade)
    cv.put(px.robe * lines(px.z - 0.66, 0.012), col(0.30, 0.38, 0.34), shade=px.shade)           # cintura
    # croce rossa sul petto (sul pannello) e sul cappuccio
    for cx, cz, arm, wd, hoodmask in ((0.0, 0.84, 0.080, 0.026, px.robe & px.front), (0.0, 1.69, 0.030, 0.010, px.hood & px.front)):
        dx, dz = np.abs(px.x - cx), np.abs(px.z - cz)
        cross = ((dx < wd) & (dz < arm)) | ((dz < wd) & (dx < arm))
        cv.put(hoodmask * cross, col(0.55, 0.035, 0.05), shade=px.shade, smo=0.5)
    # polsini rialzati
    cv.put(px.sleeve * (px.dw < 0.20), col(0.80, 0.80, 0.74), shade=px.shade)
    cv.put(px.sleeve * lines(px.dw - 0.20, 0.006), col(0.40, 0.46, 0.42), shade=px.shade)
    # macchie: iodio, ruggine, sangue rappreso; piu' fitte in basso e sulle maniche
    rust, blood = stains(px, 31, None, None, amount=1.0)
    cv.put(rust, col(0.42, 0.28, 0.08), shade=px.shade, smo=0.18)
    cv.put(blood * 0.9, col(0.22, 0.025, 0.03), shade=px.shade, smo=0.55)
    sp = smooth(0.86, 0.90, vnoise(px.P, 110.0, 4)) * (px.sleeve | (px.robe & (px.z < 0.8)))
    cv.put(sp, col(0.20, 0.02, 0.025), smo=0.6)
    cv.put(smooth(0.5, 0.0, px.z) * 0.55, col(0.12, 0.09, 0.06))


# ---------------------------------------------------------------- Primario (sanatorio): camice lungo, calotta chirurgica
def primario(px, cv):
    coat = col(0.74, 0.79, 0.82)
    cv.put(px.robe | px.sleeve, coat, shade=px.shade * 1.05, smo=0.32)
    cv.put(px.hood, col(0.34, 0.52, 0.50), shade=px.shade * 1.0, smo=0.30)            # calotta verde acqua
    cv.put(px.hood * lines(px.z - 1.085, 0.010), col(0.20, 0.32, 0.32), shade=px.shade)     # elastico
    # bavero e colletto blu notte, fila di bottoni grigi, taschino col nome
    cv.put(px.robe * px.front * (px.z > 1.0) * (np.abs(px.x) < 0.02 + (px.z - 1.0) * 0.35), col(0.10, 0.13, 0.22), shade=px.shade)
    cv.put(px.robe * px.front * lines(px.x, 0.0035) * (px.z > 0.36) * (px.z < 1.06), col(0.30, 0.34, 0.37), shade=px.shade)
    for zc in (0.98, 0.88, 0.78, 0.68, 0.58, 0.48):
        d = np.hypot(px.x - 0.012, px.z - zc)
        cv.put(px.robe * px.front * (1 - smooth(0.0085, 0.011, d)), col(0.20, 0.22, 0.25), met=0.4, smo=0.6, shade=px.shade)
    pocket = px.robe & px.front & (px.x > 0.07) & (px.x < 0.155) & (px.z > 0.80) & (px.z < 0.90)
    cv.put(pocket * ((px.x < 0.075) | (px.x > 0.150) | (px.z > 0.895) | (px.z < 0.805)), col(0.44, 0.50, 0.53), shade=px.shade)
    cv.put(px.robe * px.front * (px.x > 0.085) * (px.x < 0.095) * (px.z > 0.89) * (px.z < 0.95), col(0.05, 0.05, 0.12), shade=px.shade)   # penna
    tag = px.robe & px.front & (px.x < -0.06) & (px.x > -0.135) & (px.z > 0.90) & (px.z < 0.95)
    cv.put(tag, col(0.14, 0.14, 0.17), shade=px.shade, smo=0.5)
    # polsini azzurri
    cv.put(px.sleeve * (px.dw < 0.20), col(0.60, 0.70, 0.76), shade=px.shade)
    cv.put(px.sleeve * lines(px.dw - 0.20, 0.006), col(0.30, 0.38, 0.42), shade=px.shade)
    # sangue schizzato: spruzzi sul fondo e sulle maniche, una grande macchia
    rust, blood = stains(px, 47, None, None, amount=0.9, lower=1.0)
    cv.put(rust * 0.7, col(0.50, 0.42, 0.24), shade=px.shade, smo=0.20)
    cv.put(blood, col(0.20, 0.02, 0.025), shade=px.shade, smo=0.55)
    sp = smooth(0.84, 0.88, vnoise(px.P, 95.0, 8)) * (0.2 + 0.8 * np.maximum(smooth(0.7, 0.1, px.z), px.sleeve * 1.0))
    cv.put(sp, col(0.18, 0.015, 0.02), smo=0.6)
    cv.put(smooth(0.45, 0.0, px.z) * 0.5, col(0.10, 0.07, 0.05))


COSTUMES = {"maschera": maschera, "direttore": direttore, "infermiere": infermiere, "primario": primario}

# i camici bianchi sotto la torcia a 3-4 m uscivano quasi bruciati (prova in gioco del 2026-11-11, -temitest): le alte luci
# (luminosita' sopra ~0,35) si abbassano fino a un terzo, i colori scuri (sangue, croce, ruggine) restano come sono
HIGHLIGHT = {"infermiere": 0.33, "primario": 0.35}


# ---------------------------------------------------------------- atlante: posizione 3D di ogni pixel
def rasterize(tris):
    uv = tris["uv"].astype(np.float64) * N
    pos = tris["pos"].astype(F)
    region = tris["region"]
    pa = np.zeros((N, N, 3), F)
    rg = np.full((N, N), -1, np.int8)
    for t in range(len(uv)):
        u = np.stack([uv[t][:, 0], N - uv[t][:, 1]], 1)                       # riga 0 = alto
        x0, x1 = int(max(0, math.floor(u[:, 0].min() - 2))), int(min(N - 1, math.ceil(u[:, 0].max() + 2)))
        y0, y1 = int(max(0, math.floor(u[:, 1].min() - 2))), int(min(N - 1, math.ceil(u[:, 1].max() + 2)))
        if x1 < x0 or y1 < y0:
            continue
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        px, py = xs + 0.5, ys + 0.5
        (ax, ay), (bx, by), (cx, cy) = u
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        l0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den
        l1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den
        l2 = 1 - l0 - l1
        lens = [math.hypot(bx - cx, by - cy), math.hypot(cx - ax, cy - ay), math.hypot(ax - bx, ay - by)]
        h = [abs(den) / max(L, 1e-9) for L in lens]
        inside = (l0 * h[0] > -1.2) & (l1 * h[1] > -1.2) & (l2 * h[2] > -1.2)
        if not inside.any():
            continue
        L = np.stack([l0[inside], l1[inside], l2[inside]], 1)
        yy, xx = ys[inside], xs[inside]
        # dentro il triangolo vince su quello che c'era (che puo' essere solo il bordo allargato di un altro)
        core = (L.min(1) >= 0)
        write = core | (rg[yy, xx] < 0)
        pa[yy[write], xx[write]] = (L @ pos[t])[write]
        rg[yy[write], xx[write]] = region[t]
    return pa, rg


def load_rgb(path, mode="RGB", size=N):
    im = Image.open(path).convert(mode)
    if im.size != (size, size):
        im = im.resize((size, size), Image.LANCZOS)
    return np.asarray(im, F) / 255.0


def to_u8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)


def main(names, preview):
    OUT.mkdir(parents=True, exist_ok=True)
    tris = np.load(TRIS)
    print("rasterizzo", len(tris["region"]), "triangoli...")
    pa, rg = rasterize(tris)
    cover = rg >= 0
    print("pixel coperti:", int(cover.sum()), "su", N * N)
    base = load_rgb(MONK_TEX / "monaco_stoffa_base.png")
    mask = load_rgb(MONK_TEX / "monaco_stoffa_mask_unity.png", "RGBA")
    lum = base[..., 0] * 0.35 + base[..., 1] * 0.45 + base[..., 2] * 0.20
    idx = np.nonzero(cover)
    med = float(np.median(lum[idx]))
    # ombreggiatura dall'originale (pieghe, sporco, usura), riportata a 1 sulla mediana: i colori dei costumi restano quelli scritti
    shade_full = (np.clip(lum / max(med, 1e-4), 0.30, 1.8) ** 0.65).astype(F)
    px = Px(pa[idx], rg[idx], shade_full[idx], lum[idx])
    print("luminosita': min %.3f mediana %.3f max %.3f" % (float(lum[idx].min()), float(np.median(lum[idx])), float(lum[idx].max())))
    for name in names:
        cv = Canvas(np.zeros((len(px.P), 3), F) + 0.5, mask[idx])
        COSTUMES[name](px, cv)
        c = np.clip(cv.c, 0, 1)
        k = HIGHLIGHT.get(name, 0.0)
        if k:
            lm = c[:, 0] * 0.30 + c[:, 1] * 0.50 + c[:, 2] * 0.20
            c = c * (1.0 - k * smooth(0.35, 0.75, lm))[:, None]
        outc = base.copy()
        outc[idx] = c
        outm = mask.copy()
        outm[idx[0], idx[1], 0] = np.clip(cv.met, 0, 1)
        outm[idx[0], idx[1], 3] = np.clip(cv.smo, 0, 1)
        Image.fromarray(to_u8(outc)).save(OUT / f"{name}_stoffa_base.png")
        Image.fromarray(to_u8(outm)).save(OUT / f"{name}_stoffa_mask_unity.png")
        print(name, "scritto")
        if preview:
            Path(preview).mkdir(parents=True, exist_ok=True)
            Image.fromarray(to_u8(outc)).resize((1024, 1024), Image.LANCZOS).save(Path(preview) / f"{name}_base.png")


if __name__ == "__main__":
    args = sys.argv[1:]
    prev = None
    if "--anteprima" in args:
        i = args.index("--anteprima")
        prev = args[i + 1]
        del args[i:i + 2]
    main([a for a in args if a in COSTUMES] or list(COSTUMES), prev)
