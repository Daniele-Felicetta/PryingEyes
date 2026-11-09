"""Texture dell'occhio volante (fase 4), tutte generate in Python/numpy come quelle della hall.

Scrive in Assets/_PryingEyes/Textures/Occhio:
  occhio_base.png / _normal.png / _mask.png / _emiss.png   atlante 2048x1024:
        meta' sinistra = emisfero anteriore (disco, polo = pupilla), meta' destra = emisfero posteriore
  carne_base.png / _normal.png / _mask.png                 1024x1024 ripetibile (palpebre, nervo, tentacoli)
mask = R metallicita', G occlusione, A lucidita' (come le texture della hall).

Le coordinate del disco anteriore devono coincidere con quelle scritte da genera_occhio.py:
  u = 0.25 + 0.25*rho*cos(phi), v = 0.5 + 0.5*rho*sin(phi), rho = theta / THETA_SPLIT  (theta dall'asse della pupilla)
  disco posteriore: u = 0.75 + 0.25*rho*cos(phi), con rho = (pi - theta) / (pi - THETA_SPLIT)

uso: python -I genera_occhio_tex.py [cartella_uscita] [--anteprima file.png]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "hotel"))
import genera_texture as T  # noqa: E402  (perlin, fbm, warped, voronoi, blur, normals, smooth, sample)

OUT = Path(r"C:\Users\danie\Desktop\prying-eyes\unity\PryingEyes\Assets\_PryingEyes\Textures\Occhio")
F = np.float32
THETA_SPLIT = 1.745          # 100 gradi: dove l'emisfero anteriore lascia il posto a quello posteriore
EDGE_COL = np.array([0.80, 0.68, 0.55], np.float32)   # colore comune ai bordi dei due dischi (cucitura)
THETA_IRIS = 0.58            # raggio angolare dell'iride (rad)
PUPIL = 0.36                 # raggio della pupilla, in frazione dell'iride


def smooth(a, b, x):
    return T.smooth(a, b, x)


def lerp3(a, b, t):
    return a + (b - a) * t[..., None]


def ramp(t, stops):
    """Gradiente a tratti: stops = [(posizione, (r, g, b)), ...] ordinate."""
    out = np.zeros(t.shape + (3,), F)
    ps = [p for p, _ in stops]
    for ch in range(3):
        out[..., ch] = np.interp(t, ps, [c[ch] for _, c in stops])
    return out


# ---------------------------------------------------------------- vene
def draw_segment(canvas, p, q, w, value=1.0):
    """Segmento morbido (coordinate normalizzate -1..1, larghezza w in unita' normalizzate)."""
    n = canvas.shape[0]
    half = n / 2.0
    px, py = (p[0] + 1) * half, (1 - p[1]) * half
    qx, qy = (q[0] + 1) * half, (1 - q[1]) * half
    wp = max(w * half, 0.6)
    x0, x1 = int(max(0, min(px, qx) - wp - 2)), int(min(n - 1, max(px, qx) + wp + 2))
    y0, y1 = int(max(0, min(py, qy) - wp - 2)), int(min(n - 1, max(py, qy) + wp + 2))
    if x1 <= x0 or y1 <= y0:
        return
    ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1].astype(F)
    dx, dy = qx - px, qy - py
    l2 = dx * dx + dy * dy + 1e-9
    t = np.clip(((xs - px) * dx + (ys - py) * dy) / l2, 0, 1)
    d = np.hypot(xs - (px + t * dx), ys - (py + t * dy))
    v = (1 - smooth(wp * 0.35, wp + 0.8, d)) * value
    sub = canvas[y0:y1 + 1, x0:x1 + 1]
    np.maximum(sub, v, out=sub)


def veins(n, rng, count, r_stop, width0, branch_p, jitter, max_depth=3, capillaries=0):
    """Rete di vasi che parte dal bordo del disco e converge verso il centro, con ramificazioni.
    Restituisce (spessore 0..1, 'sangue' sfocato 0..1)."""
    canvas = np.zeros((n, n), F)
    stack = []
    for _ in range(count):
        a = rng.uniform(0, 2 * math.pi)
        p = np.array([math.cos(a), math.sin(a)]) * rng.uniform(1.0, 1.08)
        d = -p / np.linalg.norm(p)
        stack.append((p, d, width0 * rng.uniform(0.7, 1.3), 0))
    step_len = 0.018
    while stack:
        p, d, w, depth = stack.pop()
        for _ in range(240):
            to_c = -p / (np.linalg.norm(p) + 1e-6)
            ang = math.atan2(d[1], d[0]) + rng.normal(0, jitter)
            d = np.array([math.cos(ang), math.sin(ang)]) * 0.88 + to_c * (0.12 if depth == 0 else 0.04)
            d /= np.linalg.norm(d)
            q = p + d * step_len
            r = np.linalg.norm(q)
            if r > 1.12:
                p = q
                continue
            draw_segment(canvas, p, q, w, value=min(1.0, 0.45 + w * 40))
            p = q
            w *= 0.992
            if r < r_stop or w < 0.0008:
                break
            if depth < max_depth and rng.random() < branch_p:
                side = rng.choice([-1, 1]) * rng.uniform(0.35, 0.95)
                c, s = math.cos(side), math.sin(side)
                d2 = np.array([c * d[0] - s * d[1], s * d[0] + c * d[1]])
                stack.append((p.copy(), d2, w * rng.uniform(0.5, 0.75), depth + 1))
    for _ in range(capillaries):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(r_stop + 0.05, 1.0)
        p = np.array([math.cos(a), math.sin(a)]) * r
        d = np.array([math.cos(a + rng.uniform(-1.2, 1.2)), math.sin(a + rng.uniform(-1.2, 1.2))])
        w = 0.0016 * rng.uniform(0.6, 1.4)
        for _ in range(rng.integers(8, 26)):
            ang = math.atan2(d[1], d[0]) + rng.normal(0, 0.4)
            d = np.array([math.cos(ang), math.sin(ang)])
            q = p + d * 0.012
            if np.linalg.norm(q) < r_stop or np.linalg.norm(q) > 1.08:
                break
            draw_segment(canvas, p, q, w, value=0.55)
            p = q
    haze = T.blur(canvas, max(2, n // 60))
    return np.clip(canvas, 0, 1), np.clip(haze * 2.2, 0, 1)


# ---------------------------------------------------------------- disco anteriore
def front_disc(n=1024, seed=7):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    sx = (x + 0.5) / n * 2 - 1
    sy = 1 - (y + 0.5) / n * 2
    rho = np.hypot(sx, sy)
    phi = np.arctan2(sy, sx)
    theta = rho * THETA_SPLIT
    rho_i = THETA_IRIS / THETA_SPLIT
    rn = rho / rho_i                                   # 0 al centro, 1 al bordo dell'iride

    # --- sclera: giallo-rosato macchiato, piu' rosso verso il bordo e vicino all'iride
    mott = T.fbm(n, 5, 6, rng)
    mott2 = T.fbm(n, 22, 4, rng)
    sclera = ramp(mott, [(0.0, (0.70, 0.62, 0.44)), (0.5, (0.82, 0.76, 0.60)), (1.0, (0.92, 0.88, 0.76))])
    sclera = lerp3(sclera, np.array([0.60, 0.40, 0.30], F), smooth(0.45, 0.8, mott2) * 0.22)
    sclera = lerp3(sclera, np.array([0.66, 0.34, 0.26], F), smooth(0.55, 1.0, rho) * 0.16)
    # ombra sottile attorno all'iride (il limbo)
    limbus = smooth(1.0, 1.12, rn) * (1 - smooth(1.12, 1.9, rn))
    sclera = lerp3(sclera, sclera * 0.42 + np.array([0.05, 0.03, 0.01], F), limbus * 0.80)

    vein_w, vein_haze = veins(n, rng, count=20, r_stop=rho_i * 1.10, width0=0.0085, branch_p=0.05, jitter=0.20,
                              capillaries=90)
    vein_col = ramp(np.clip(vein_w, 0, 1), [(0.0, (0.60, 0.12, 0.10)), (1.0, (0.40, 0.03, 0.04))])
    sclera = lerp3(sclera, np.array([0.70, 0.30, 0.26], F), vein_haze * 0.34)
    sclera = lerp3(sclera, vein_col, np.clip(vein_w * 0.96, 0, 1))

    # --- iride: fibre radiali ambra come quelle dell'occhio del monaco
    # rumore in coordinate polari (angolo x raggio), periodico in angolo
    ang01 = (phi / (2 * math.pi) + 0.5) % 1.0
    pol = lambda img: T.sample(img, ang01 * (img.shape[0]), np.clip(rn, 0, 1.2) * (img.shape[0] * 0.8))
    f_fine = pol(T.perlin(n, 160, rng, cells_y=8))
    f_med = pol(T.perlin(n, 48, rng, cells_y=5))
    f_big = pol(T.perlin(n, 9, rng, cells_y=3))
    fibers = np.clip(0.5 + f_fine * 0.9 + f_med * 0.8, 0, 1)
    base_iris = ramp(np.clip(rn, 0, 1), [
        (0.00, (1.00, 0.78, 0.22)), (0.36, (1.00, 0.74, 0.20)), (0.48, (0.98, 0.58, 0.09)),
        (0.70, (0.80, 0.32, 0.05)), (0.88, (0.46, 0.14, 0.03)), (1.00, (0.14, 0.05, 0.02))])
    iris = base_iris * (0.55 + fibers[..., None] * 0.95)
    # chiazze piu' scure e piu' chiare larghe
    iris *= (0.85 + f_big[..., None] * 0.55)
    # collaretto (anello frastagliato attorno alla pupilla)
    coll_r = 0.52 + 0.035 * pol(T.perlin(n, 30, rng, cells_y=2)) * 6
    collar = np.exp(-((rn - coll_r) / 0.035) ** 2)
    iris = iris * (1 - collar[..., None] * 0.25) + collar[..., None] * np.array([0.5, 0.28, 0.06], F)
    # cripte scure
    crypt = smooth(0.62, 0.78, pol(T.perlin(n, 70, rng, cells_y=14))) * smooth(0.5, 0.62, rn) * (1 - smooth(0.84, 0.96, rn))
    iris *= 1 - crypt[..., None] * 0.55
    # anello limbare scuro
    ring = smooth(0.80, 0.995, rn)
    iris = iris * (1 - ring[..., None] * 0.70)
    # pupilla
    pupil_edge = PUPIL * (1 + 0.025 * pol(T.perlin(n, 24, rng, cells_y=2)))
    pm = 1 - smooth(pupil_edge - 0.022, pupil_edge + 0.012, rn)
    iris = iris * (1 - pm[..., None]) + np.array([0.006, 0.005, 0.007], F) * pm[..., None]

    inside = 1 - smooth(0.985, 1.012, rn)
    base = lerp3(sclera, iris, inside)
    base = lerp3(base, EDGE_COL * (0.94 + 0.12 * mott[..., None]), smooth(0.78, 0.97, rho) * 0.85)

    # --- altezza: vene in rilievo, fibre dell'iride, bordo della pupilla
    h = 0.5 + (T.fbm(n, 90, 3, rng) - 0.5) * 0.10
    h += vein_w * 0.18
    h = np.where(rn < 1.0, 0.5 + (fibers - 0.5) * 0.16 - crypt * 0.10 + collar * 0.05, h)
    h += np.exp(-((rn - PUPIL) / 0.02) ** 2) * 0.12 * (rn < 1.0)
    # --- lucidita'
    sm = 0.68 + (T.fbm(n, 14, 4, rng) - 0.5) * 0.22 - vein_w * 0.08
    sm = np.where(rn < 1.0, 0.84 - fibers * 0.08, sm)
    # --- emissione: l'iride brilla in ambra (si legge nel buio), la pupilla no
    em = iris * (1 - pm[..., None]) * (smooth(0.99, 0.80, rn))[..., None]
    em = em * (0.5 + fibers[..., None] * 0.8)
    em = em * inside[..., None]
    ao = 1 - 0.55 * smooth(0.0, 0.5, vein_w) * 0.2 - 0.25 * limbus
    return base, h, sm, em, ao


# ---------------------------------------------------------------- disco posteriore
def back_disc(n=1024, seed=21):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n].astype(F)
    sx = (x + 0.5) / n * 2 - 1
    sy = 1 - (y + 0.5) / n * 2
    rho = np.hypot(sx, sy)
    mott = T.fbm(n, 6, 6, rng)
    base = ramp(mott, [(0.0, (0.72, 0.58, 0.48)), (0.5, (0.82, 0.70, 0.60)), (1.0, (0.90, 0.80, 0.70))])
    vw, haze = veins(n, rng, count=34, r_stop=0.10, width0=0.011, branch_p=0.07, jitter=0.24, capillaries=120)
    base = lerp3(base, np.array([0.62, 0.28, 0.22], F), np.clip(haze * 0.40 + smooth(0.0, 0.5, 1 - rho) * 0.22, 0, 1))
    vcol = ramp(vw, [(0.0, (0.58, 0.12, 0.10)), (1.0, (0.34, 0.02, 0.03))])
    base = lerp3(base, vcol, np.clip(vw * 0.97, 0, 1))
    base = lerp3(base, EDGE_COL * (0.94 + 0.12 * mott[..., None]), smooth(0.78, 0.97, rho) * 0.85)
    h = 0.5 + (T.fbm(n, 70, 3, rng) - 0.5) * 0.12 + vw * 0.20
    sm = 0.55 + (T.fbm(n, 12, 4, rng) - 0.5) * 0.25
    ao = np.ones((n, n), F) * 0.92
    return base, h, sm, np.zeros((n, n, 3), F), ao


# ---------------------------------------------------------------- carne ripetibile
def carne(n=1024, seed=44):
    rng = np.random.default_rng(seed)
    w1 = T.warped(n, rng, 3, 6, 0.25)
    w2 = T.fbm(n, 9, 5, rng)
    base = ramp(w1, [(0.0, (0.34, 0.07, 0.07)), (0.45, (0.55, 0.22, 0.20)), (1.0, (0.72, 0.45, 0.40))])
    # fibre muscolari lungo v (la lunghezza del tubo)
    fib = T.perlin(n, 80, rng, cells_y=3) + 0.6 * T.perlin(n, 200, rng, cells_y=6)
    base *= (0.62 + np.clip(fib * 0.8 + 0.5, 0, 1)[..., None] * 0.70)
    # tendini biancastri
    tend = 1 - smooth(0.0, 0.05, np.abs(T.perlin(n, 36, rng, cells_y=2)))
    tend *= smooth(0.45, 0.7, T.fbm(n, 6, 3, rng))
    base = lerp3(base, np.array([0.84, 0.74, 0.64], F), tend * 0.75)
    # rete di capillari: bordi di Voronoi deformati (domain warp), sottili e poco opachi
    d1, d2, _ = T.voronoi(n, 9, rng)
    wy_, wx_ = np.mgrid[0:n, 0:n].astype(F)
    off1, off2 = T.fbm(n, 5, 4, rng) - 0.5, T.fbm(n, 5, 4, rng) - 0.5
    edge = T.sample((d2 - d1).astype(F), wx_ + off1 * 0.35 * n, wy_ + off2 * 0.35 * n)
    web = (1 - smooth(0.0, 0.045, edge)) * smooth(0.35, 0.65, T.fbm(n, 6, 3, rng))
    base = lerp3(base, np.array([0.30, 0.03, 0.05], F), web * 0.42)
    # fascia lucida: chiazze chiare e piu' lisce
    fascia = smooth(0.66, 0.84, T.fbm(n, 5, 5, rng))
    base = lerp3(base, base * 0.55 + np.array([0.40, 0.33, 0.28], F) * 0.45, fascia * 0.55)
    # chiazze scure di sangue raggrumato
    clot = smooth(0.62, 0.8, T.fbm(n, 4, 5, rng))
    base = lerp3(base, base * 0.45, clot * 0.6)
    h = 0.5 + np.clip(fib, -1, 1) * 0.10 + (w2 - 0.5) * 0.45 - web * 0.12 + tend * 0.08
    sm = 0.46 + (T.fbm(n, 7, 4, rng) - 0.5) * 0.45 + tend * 0.10 + fascia * 0.22 - clot * 0.15
    ao = 1 - web * 0.35 - clot * 0.25
    return base, h, sm, ao


# ---------------------------------------------------------------- scrittura
def write(name, base, h, sm, ao, em=None, nstrength=2.2):
    OUT.mkdir(parents=True, exist_ok=True)
    nrm = T.normals(h.astype(F), nstrength)
    mask = np.zeros(base.shape[:2] + (4,), F)
    mask[..., 1] = np.clip(ao, 0, 1)
    mask[..., 3] = np.clip(sm, 0, 1)
    Image.fromarray((np.clip(base, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_base.png")
    Image.fromarray((np.clip(nrm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_normal.png")
    Image.fromarray((mask * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_mask.png")
    if em is not None:
        Image.fromarray((np.clip(em, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f"{name}_emiss.png")
    print(f"{name}: {base.shape[1]}x{base.shape[0]}")


def occhio():
    n = 1024
    fb, fh, fs, fe, fa = front_disc(n)
    bb, bh, bs, be, ba = back_disc(n)
    # il normale dell'atlante e' calcolato su un'immagine sola: le due meta' non si toccano (bordo del disco)
    base = np.concatenate([fb, bb], axis=1)
    h = np.concatenate([fh, bh], axis=1)
    sm = np.concatenate([fs, bs], axis=1)
    em = np.concatenate([fe, be], axis=1)
    ao = np.concatenate([fa, ba], axis=1)
    write("occhio", base, h, sm, ao, em, nstrength=3.4)


def carne_tex():
    base, h, sm, ao = carne(1024)
    write("carne", base, h, sm, ao, nstrength=7.0)


def anteprima(path):
    names = ("occhio_base", "occhio_emiss", "carne_base", "carne_normal")
    ims = []
    for nm in names:
        p = OUT / f"{nm}.png"
        if p.exists():
            im = Image.open(p).convert("RGB")
            im.thumbnail((1024, 512))
            ims.append(im)
    w = sum(i.width for i in ims[:2]) + 8
    h = max(i.height for i in ims[:2]) + 8 + max(i.height for i in ims[2:])
    sheet = Image.new("RGB", (max(w, ims[2].width + ims[3].width + 8), h), (0, 0, 0))
    x = 0
    for i in ims[:2]:
        sheet.paste(i, (x, 0))
        x += i.width + 8
    y0 = max(i.height for i in ims[:2]) + 8
    sheet.paste(ims[2], (0, y0))
    sheet.paste(ims[3], (ims[2].width + 8, y0))
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
    if rest:
        OUT = Path(rest[0])
    occhio()
    carne_tex()
    if preview:
        anteprima(preview)
