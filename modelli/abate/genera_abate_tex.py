"""Texture dell'abate (fase 4): la stoffa del monaco ricolorata in nero e ricamata d'oro, piu' una piccola texture
d'oro battuto per l'aureola e il bastone.

L'abate e' il monaco ingrandito: stessa mesh, stesse UV, stessa normal map. Cambia solo il colore della stoffa e si
aggiunge il ricamo, che e' dipinto direttamente nell'atlante: per ogni triangolo della stoffa (scritti da
genera_abate.py -- dump in stoffa_tris.npz) i pixel dentro il triangolo si convertono nella posizione 3D e li' si
valuta il disegno (fascia all'orlo, stola davanti, medaglione sul petto, polsini). Cosi' il ricamo continua attraverso
le isole UV senza cuciture.

uso: python -I genera_abate_tex.py [--anteprima file.png]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
MONK_TEX = HERE.parent / "monaco" / "export" / "textures"
OUT = Path(r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Models\Abate\Textures")
F = np.float32


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- disegno in 3D
# Il monaco guarda verso -Y, la sua sinistra e' +X, l'asse della tonaca e' circa x = 0, y = 0.02.
WRIST = {"r": np.array([-0.215, -0.12, 0.77], F), "l": np.array([0.30, -0.285, 1.02], F)}

GOLD = np.array([0.78, 0.58, 0.20], F)
GOLD_DARK = np.array([0.30, 0.19, 0.06], F)
THREAD_BLACK = np.array([0.03, 0.025, 0.03], F)


def design(P, region):
    """P: (N,3) posizioni a riposo, region: (N,) 0 tonaca, 1 maniche, 2 cappuccio.
    Restituisce (peso oro 0..1, altezza del rilievo, luminosita' del filo, peso 'nero ricamo' 0..1, peso iride 0..1)."""
    x, y, z = P[:, 0], P[:, 1] - 0.02, P[:, 2]
    front = y < 0.0
    theta = np.arctan2(x, -y)                        # 0 davanti
    gold = np.zeros(len(P), F)
    height = np.zeros(len(P), F)
    shade = np.ones(len(P), F)
    dark = np.zeros(len(P), F)
    iris = np.zeros(len(P), F)
    robe = region == 0

    # --- fascia all'orlo, sopra gli strappi (z 0.40 .. 0.53): rombi concatenati
    z0, z1 = 0.40, 0.53
    band = robe & (z > z0) & (z < z1)
    a = (theta / (2 * math.pi) * 26.0) % 1.0 - 0.5
    b = (z - z0) / (z1 - z0) * 2 - 1
    diamond = np.abs(a) * 2.0 + np.abs(b)
    line = 1 - smooth(0.0, 0.10, np.abs(diamond - 0.78))
    dot = 1 - smooth(0.0, 0.16, np.hypot(a * 1.6, b * 0.9))
    edge = 1 - smooth(0.0, 0.10, np.minimum(np.abs(b + 1), np.abs(b - 1)) * 2.0)
    gold = np.where(band, 1.0, gold)
    dark = np.where(band, np.maximum(line * 0.9, edge * 0.9), dark)
    height = np.where(band, 0.7 + 0.3 * (1 - dark) + dot * 0.2, height)
    gold = np.where(band & (dot > 0.5), 1.0, gold)
    dark = np.where(band & (dot > 0.5), 0.0, dark)

    # --- stola davanti (larga 10 cm, dal petto alla fascia): croci e filo scuro al centro
    stole = robe & front & (np.abs(x) < 0.05) & (z > 0.53) & (z < 1.20)
    c = (z / 0.13) % 1.0 - 0.5
    cross = np.minimum(np.abs(x / 0.05), np.abs(c * 2.0)) < 0.18
    gold = np.where(stole, 1.0, gold)
    dark = np.where(stole, np.where(cross, 0.0, 0.0) + (1 - smooth(0.0, 0.30, np.abs(x) / 0.05)) * 0.0 + np.where(np.abs(x) > 0.043, 0.85, 0.0), dark)
    dark = np.where(stole & (np.abs(x) < 0.007), 0.9, dark)
    height = np.where(stole, 0.8, height)

    # --- medaglione sul petto: anello d'oro, raggi, occhio rosso al centro
    cx, cz = 0.0, 0.97
    d = np.hypot(x - cx, z - cz)
    med = robe & front & (d < 0.135)
    ring = (d > 0.068) & (d < 0.082)
    ray_a = np.arctan2(x - cx, z - cz)
    rays = ((d > 0.088) & (d < 0.133)) & (np.abs(((ray_a / (2 * math.pi) * 20) % 1.0) - 0.5) < 0.16 * (1.3 - (d - 0.088) / 0.045 * 0.8))
    disc = d < 0.062
    gold = np.where(med & (ring | rays), 1.0, gold)
    dark = np.where(med & disc, 0.95, dark)
    iris_m = med & (d < 0.046)
    iris = np.where(iris_m, 1.0 - smooth(0.0, 0.046, d) * 0.4, iris)
    iris = np.where(med & (d < 0.017), 0.0, iris)
    dark = np.where(med & (d < 0.017), 1.0, dark)
    height = np.where(med & (ring | rays), 0.9, height)
    gold = np.where(med & (d >= 0.062) & (d < 0.068), 1.0, gold)

    # --- polsini delle maniche: due fasce a 16-24 cm dal polso
    sl = region == 1
    wrist = np.where((x < 0)[:, None], WRIST["r"], WRIST["l"])
    dw = np.linalg.norm(P - wrist, axis=1)
    cuff = sl & (dw > 0.20) & (dw < 0.265)
    e2 = 1 - smooth(0.0, 0.18, np.minimum(np.abs(dw - 0.20), np.abs(dw - 0.265)) / 0.065 * 2.0)
    gold = np.where(cuff, 1.0, gold)
    dark = np.where(cuff, np.maximum(e2 * 0.9, dark), dark)
    height = np.where(cuff, 0.7, height)

    # filo: luminosita' variabile (trama), per non avere un oro piatto
    shade = 0.78 + 0.22 * np.sin(z * 520.0 + x * 130.0) * np.sin(theta * 90.0 + z * 40.0)
    return gold, height, shade, dark, iris


# ---------------------------------------------------------------- pittura sull'atlante
def paint(tris, base, mask_a, mask_r):
    """Dipinge nell'atlante: restituisce (colore RGB, altezza, lucidita' dell'oro, mappa metallicita')."""
    n = base.shape[0]
    col = base.copy()
    hmap = np.zeros((n, n), F)
    gold_w = np.zeros((n, n), F)
    uv = tris["uv"].astype(np.float64) * n
    pos = tris["pos"].astype(F)
    region = tris["region"]
    # solo i triangoli che toccano le zone ricamate
    zmin, zmax = pos[:, :, 2].min(1), pos[:, :, 2].max(1)
    xmin, xmax = pos[:, :, 0].min(1), pos[:, :, 0].max(1)
    near = ((zmax > 0.38) & (zmin < 1.25)) | (region == 1)
    idx = np.nonzero(near)[0]
    done = 0
    for t in idx:
        u = uv[t]
        u = np.stack([u[:, 0], n - u[:, 1]], 1)                       # riga 0 = alto
        x0, x1 = int(max(0, math.floor(u[:, 0].min() - 2))), int(min(n - 1, math.ceil(u[:, 0].max() + 2)))
        y0, y1 = int(max(0, math.floor(u[:, 1].min() - 2))), int(min(n - 1, math.ceil(u[:, 1].max() + 2)))
        if x1 < x0 or y1 < y0:
            continue
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        px, py = xs + 0.5, ys + 0.5
        # coordinate baricentriche e distanze (in pixel) dai tre lati
        (ax, ay), (bx, by), (cx, cy) = u
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        l0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den
        l1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den
        l2 = 1 - l0 - l1
        lens = [math.hypot(bx - cx, by - cy), math.hypot(cx - ax, cy - ay), math.hypot(ax - bx, ay - by)]
        h = [abs(den) / max(L, 1e-9) for L in lens]                   # altezze del triangolo sui lati
        inside = (l0 * h[0] > -1.2) & (l1 * h[1] > -1.2) & (l2 * h[2] > -1.2)
        if not inside.any():
            continue
        L = np.stack([l0[inside], l1[inside], l2[inside]], 1)
        P = L @ pos[t]
        g, hh, sh, dk, ir = design(P, np.full(len(P), region[t]))
        sel = g > 0.02
        if not sel.any() and not (ir > 0.02).any():
            continue
        yy, xx = ys[inside], xs[inside]
        gold_c = GOLD[None, :] * sh[:, None]
        # oro, poi filo scuro sopra, poi iride
        c = col[yy, xx]
        c = c * (1 - g[:, None]) + gold_c * g[:, None]
        c = c * (1 - dk[:, None] * g[:, None] * 0.9) + THREAD_BLACK[None, :] * (dk[:, None] * g[:, None] * 0.9)
        # il disco del medaglione e' scuro, non oro
        iris_c = np.array([0.95, 0.40, 0.05], F)
        c = c * (1 - ir[:, None]) + iris_c[None, :] * ir[:, None]
        col[yy, xx] = c
        hmap[yy, xx] = np.maximum(hmap[yy, xx], hh * np.maximum(g, ir))
        gold_w[yy, xx] = np.maximum(gold_w[yy, xx], g * (1 - dk * 0.8))
        done += 1
    print(f"dipinti {done} triangoli su {len(idx)}")
    return col, hmap, gold_w


def recolor(base):
    """Bordeaux del monaco -> nero lana con un velo viola. Si tiene solo la luminosita' (sporco, usura, trama)."""
    lum = base[..., 0] * 0.35 + base[..., 1] * 0.45 + base[..., 2] * 0.20
    t = smooth(0.02, 0.45, lum)
    dark = np.array([0.030, 0.027, 0.038], F)
    light = np.array([0.225, 0.190, 0.255], F)
    out = dark + (light - dark) * t[..., None]
    # tracce calde dove l'originale era molto rosso (macchie): restano come ruggine sul nero
    redness = np.clip((base[..., 0] - base[..., 1] * 1.6 - base[..., 2] * 0.6) * 2.2, 0, 1) * smooth(0.25, 0.6, base[..., 0])
    out = out + (np.array([0.10, 0.025, 0.015], F) - out) * (redness[..., None] * 0.18)
    return np.clip(out, 0, 1)


def to_u8(a):
    return (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - 0.055)


def stoffa(tris_path):
    OUT.mkdir(parents=True, exist_ok=True)
    tris = np.load(tris_path)
    n = 2048                                         # l'abate si vede da lontano: meta' risoluzione bastano
    base = np.asarray(Image.open(MONK_TEX / "monaco_stoffa_base.png").convert("RGB").resize((n, n), Image.LANCZOS), F) / 255.0
    nrm = np.asarray(Image.open(MONK_TEX / "monaco_stoffa_normal.png").convert("RGB").resize((n, n), Image.LANCZOS), F) / 255.0
    msk = np.asarray(Image.open(MONK_TEX / "monaco_stoffa_mask_unity.png").convert("RGBA").resize((n, n), Image.LANCZOS), F) / 255.0
    col = recolor(base)
    col, hmap, gold_w = paint(tris, col, msk[..., 3], msk[..., 0])
    # il rilievo del ricamo si somma alla normal map del monaco (spazio tangente: l'altezza e' nello spazio UV)
    hs = hmap
    dx = (np.roll(hs, -1, 1) - np.roll(hs, 1, 1)) * 0.5
    dy = (np.roll(hs, -1, 0) - np.roll(hs, 1, 0)) * 0.5
    k = 7.0
    nx = (nrm[..., 0] * 2 - 1) - dx * k
    ny = (nrm[..., 1] * 2 - 1) + dy * k                  # riga 0 = alto: il verde cresce verso l'alto
    nz = nrm[..., 2] * 2 - 1
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm2 = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5
    # maschera: oro = un po' metallico e piu' lucido del lana
    m = msk.copy()
    m[..., 0] = np.clip(m[..., 0] + gold_w * 0.45, 0, 1)
    m[..., 3] = np.clip(m[..., 3] * (1 - gold_w) + (0.55 + 0.1 * hs) * gold_w, 0, 1)
    Image.fromarray(to_u8(col)).save(OUT / "abate_stoffa_base.png")
    Image.fromarray(to_u8(nrm2)).save(OUT / "abate_stoffa_normal.png")
    Image.fromarray(to_u8(m)).save(OUT / "abate_stoffa_mask_unity.png")
    print("stoffa:", n, "px")
    return col


# ---------------------------------------------------------------- oro battuto
def oro(n=512, seed=9):
    rng = np.random.default_rng(seed)

    def noise(cells):
        g = rng.random((cells + 1, cells + 1)).astype(F)
        y, x = np.mgrid[0:n, 0:n].astype(F) * cells / n
        x0, y0 = x.astype(int), y.astype(int)
        fx, fy = x - x0, y - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = g[y0, x0] * (1 - fx) + g[y0, x0 + 1] * fx
        b = g[y0 + 1, x0] * (1 - fx) + g[y0 + 1, x0 + 1] * fx
        return a * (1 - fy) + b * fy
    big = noise(4) * 0.5 + noise(11) * 0.3 + noise(37) * 0.2
    dents = noise(24)
    base = np.stack([0.80 + 0.12 * (big - 0.5), 0.60 + 0.12 * (big - 0.5), 0.22 + 0.08 * (big - 0.5)], -1)
    base *= (0.86 + 0.20 * dents[..., None])
    h = dents * 0.5 + noise(60) * 0.15
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx, ny, nz = -dx * 14, dy * 14, np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5
    mask = np.zeros((n, n, 4), F)
    mask[..., 0] = 0.92
    mask[..., 1] = 0.9 + 0.1 * dents
    mask[..., 3] = np.clip(0.50 + 0.18 * big - 0.12 * dents, 0, 1)
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray(to_u8(base)).save(OUT / "abate_oro_base.png")
    Image.fromarray(to_u8(nrm)).save(OUT / "abate_oro_normal.png")
    Image.fromarray(to_u8(mask)).save(OUT / "abate_oro_mask_unity.png")
    print("oro:", n, "px")


if __name__ == "__main__":
    preview = None
    it = iter(sys.argv[1:])
    for a in it:
        if a == "--anteprima":
            preview = next(it)
    oro()
    col = stoffa(HERE / "stoffa_tris.npz")
    if preview:
        Image.fromarray(to_u8(col)).resize((1024, 1024), Image.LANCZOS).save(preview)
        print(preview)
