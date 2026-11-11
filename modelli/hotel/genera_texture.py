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

OUT = Path(r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Textures\Hotel")
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


def marmo_bianco(n=1024):
    """Marmo bianco liscio per scalone, colonne e banco: stesse venature senza fughe."""
    rng = np.random.default_rng(37)
    cloud = warped(n, rng, 3, 6, 0.3)
    v1 = marble_veins(n, rng, 2, 0.35, 0.018, 5)
    v2 = marble_veins(n, rng, 4, 0.25, 0.006, 2)
    base = col((0.85, 0.83, 0.79), n)
    base = lerp(base, col((0.71, 0.70, 0.69), n), smooth(0.35, 0.75, cloud) * 0.55)
    base = lerp(base, col((0.46, 0.46, 0.48), n), np.clip(v1 * 0.55 + v2 * 0.45, 0, 1))
    dirt = smooth(0.6, 0.9, fbm(n, 3, 5, rng))
    base = lerp(base, col((0.30, 0.27, 0.23), n), dirt * 0.25)
    scr = scratches(n, 120, rng, (0.01, 0.05), 1)
    h = (fbm(n, 12, 3, rng) - 0.5) * 0.02 - scr * 0.02
    smooth_ = 0.80 - dirt * 0.3 - scr * 0.3
    save("marmo_bianco", base, normals(h, 8.0), np.zeros((n, n), F), np.ones((n, n), F) - dirt * 0.1, smooth_)


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
    """Soffitto: intonaco color crema, crepe ramificate, aloni d'acqua."""
    rng = np.random.default_rng(47)
    d1, d2, _ = voronoi(n, 6, rng)
    crack = 1 - smooth(0.0, 0.025, d2 - d1)
    crack *= smooth(0.45, 0.65, fbm(n, 4, 4, rng))         # crepe solo in alcune zone
    mott = fbm(n, 8, 6, rng)
    stain = fbm(n, 3, 5, rng)
    ring = np.exp(-((stain - 0.62) / 0.02) ** 2) * 0.6     # bordo scuro degli aloni
    base = lerp(col((0.66, 0.62, 0.55), n), col((0.55, 0.50, 0.43), n), mott)
    base = lerp(base, col((0.52, 0.44, 0.32), n), smooth(0.6, 0.75, stain) * 0.5)
    base = lerp(base, col((0.36, 0.29, 0.20), n), ring * 0.5)
    base = lerp(base, col((0.18, 0.16, 0.14), n), crack * 0.8)
    h = (mott - 0.5) * 0.4 + (fbm(n, 64, 2, rng) - 0.5) * 0.2 - crack * 0.6
    save("intonaco", base, normals(h, 3.0), np.zeros((n, n), F), 1 - crack * 0.4, 0.12 - crack * 0.05)


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
    """Linoleum del sanatorio: grigio-verde spento, grana fine, usura lucida lungo i passaggi, qualche graffio."""
    rng = np.random.default_rng(61)
    mott = _nz(fbm(n, 6, 5, rng))
    grain = _nz(perlin(n, n // 2, rng))
    wear = smooth(0.55, 0.8, _nz(fbm(n, 3, 4, rng)))
    base = lerp(col((0.56, 0.62, 0.57), n), col((0.46, 0.53, 0.49), n), mott)
    base = lerp(base, col((0.66, 0.70, 0.66), n), wear * 0.5)
    base = base * (0.93 + 0.14 * grain)[..., None]
    base = lerp(base, col((0.30, 0.34, 0.31), n), scratches(n, 40, rng, (0.01, 0.05), 1.0) * 0.6)
    h = (mott - 0.5) * 0.2 + (grain - 0.5) * 0.15 - wear * 0.05
    save("linoleo", base, normals(h, 1.5), np.zeros((n, n), F), 1 - (1 - grain) * 0.08, 0.36 + 0.10 * wear - 0.05 * grain)


def vernice_verde(n=1024):
    """Vernice verde da ospedale, parte bassa dei muri: satinata, pennellate orizzontali, scrostature chiare, aloni d'umidita' in basso."""
    rng = np.random.default_rng(73)
    brush = _nz(perlin(n, 5, rng, cells_y=n // 2))
    mott = _nz(fbm(n, 5, 5, rng))
    d1, _, _ = voronoi(n, 12, rng)
    chip = (1 - smooth(0.0, 0.035, d1)) * smooth(0.45, 0.6, fbm(n, 4, 4, rng))
    bottom = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    damp = smooth(0.55, 1.0, bottom) * smooth(0.5, 0.7, _nz(fbm(n, 3, 5, rng)))
    base = lerp(col((0.40, 0.55, 0.47), n), col((0.45, 0.60, 0.51), n), brush * 0.7)
    base = base * (0.95 + 0.08 * mott)[..., None]
    base = lerp(base, col((0.74, 0.73, 0.67), n), chip * 0.85)
    base = lerp(base, col((0.22, 0.28, 0.24), n), damp * 0.6)
    h = (brush - 0.5) * 0.18 + (mott - 0.5) * 0.1 - chip * 0.7
    save("vernice_verde", base, normals(h, 2.0), np.zeros((n, n), F), 1 - damp * 0.25, 0.26 - chip * 0.2)


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
    names = ["marmo_pavimento", "marmo_bianco", "damasco", "noce", "intonaco", "moquette", "ottone", "acciaio", "velluto"]
    tiles = []
    for nm in names:
        b = Image.open(OUT / f"{nm}_base.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        nr = Image.open(OUT / f"{nm}_normal.png").convert("RGB").resize((256, 256), Image.LANCZOS)
        mk = Image.open(OUT / f"{nm}_mask.png").getchannel("A").convert("RGB").resize((256, 256), Image.LANCZOS)
        row = Image.new("RGB", (256 * 3 + 8, 256), (0, 0, 0))
        row.paste(b, (0, 0)); row.paste(nr, (260, 0)); row.paste(mk, (520, 0))
        tiles.append(row)
    sheet = Image.new("RGB", (tiles[0].width * 3 + 16, 264 * 3), (0, 0, 0))
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
    for fn in (marmo_scacchiera, marmo_bianco, damasco, noce, intonaco, moquette, ottone, acciaio, velluto, cuoio, forme, linoleo, vernice_verde, ruggine):
        if only is None or fn.__name__ in only:
            fn()
    if preview:
        anteprima(preview)
