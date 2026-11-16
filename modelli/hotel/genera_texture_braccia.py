"""Texture uniche (non ripetute) della mano articolata e della manica neutra del giocatore, dipinte sulla superficie vera.

genera_braccia.py -- libera / -- manica scrivono, oltre agli FBX, braccia/mano_libera_uv.npz e braccia/manica_libera_uv.npz: per ogni
triangolo i vertici (riferimento canonico della mano: dita +y, dorso +z, pollice -x; per la manica il suo riferimento d'autore), le
normali, le coordinate dell'atlante, la parte, la cavita' e, per la manica, la lunghezza dall'orlo e il giro dalla cucitura interna.
Qui ogni texel dell'atlante sa dove sta sulla superficie (punto, normale, direzioni di u e v) e colore, altezza e lucidita' si
calcolano in quel punto: per il guanto cuoio a grana con le cuciture lungo i fianchi delle dita e attorno ai polpastrelli, i tre
'punti' cuciti sul dorso, il contorno delle imbottiture delle nocche, le pieghe sopra e sotto le articolazioni e del palmo, il palmo
scamosciato, il consumo su nocche, polpastrelli e indice, la manichetta con l'orlo, l'elastico e il cinturino di nastro; per la
manica la tela sbiadita sul lato esterno, il polsino con le impunture, la cucitura ribattuta interna, la toppa del gomito, lo
sporco verso il polso. Su tutti e due un po' di sangue secco. La normal map esce dall'altezza con le differenze finite nello spazio
del texel e le direzioni u, v di ogni triangolo (le tangenti di MikkTSpace a meno di piccole differenze); le isole si allargano di
16 texel, perche' le mipmap non portino dentro il nero dei bordi.

Scrive in unity/PryingEyes/Assets/_PryingEyes/Textures/Braccia:
  guanto_libero_base.png (sRGB), guanto_libero_normal.png (verde = +v, come vuole Unity), guanto_libero_mask.png (R metallo,
  G occlusione, A lucidita': va su _MetallicGlossMap e _OcclusionMap); manica_libera_base/normal/mask.png (1024 x 2048: il giro
  per la lunghezza); tela_dettaglio_base.png (la saia della giacca in grigio attorno a 0,5, per la mappa di dettaglio di URP: la
  normal map di dettaglio e' Textures/Hotel/giacca_normal.png).

uso: python -I genera_texture_braccia.py [guanto] [manica] [dettaglio]   (senza argomenti tutte e tre)
"""
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

F = np.float32
HERE = Path(__file__).resolve().parent
IN = HERE / 'braccia'
TEX = HERE.parents[1] / 'unity' / 'PryingEyes' / 'Assets' / '_PryingEyes' / 'Textures'
OUT = TEX / 'Braccia'


# ------------------------------------------------------------------ utilita'
def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def norm(v):
    return v / np.maximum(1e-12, np.linalg.norm(v, axis=-1, keepdims=True))


def dot(a, b):
    return (a * b).sum(-1)


def lerp(a, b, t):
    t = np.asarray(t)
    if t.ndim == 1 and np.ndim(a) == 2 or t.ndim == 1 and np.ndim(b) == 2:
        t = t[:, None]
    return a + (b - a) * t


# ------------------------------------------------------------------ rumore 3D (gradiente e celle), vettoriale
GRAD = np.array([[1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0], [1, 0, 1], [-1, 0, 1], [1, 0, -1], [-1, 0, -1],
                 [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1]], np.float64)


def _hash(ix, iy, iz, seed):
    with np.errstate(over='ignore'):
        h = (ix * np.int64(-1640531535)) ^ (iy * np.int64(805459861)) ^ (iz * np.int64(-1175848235)) ^ np.int64(seed * 974634761 + 12345)
        h = h ^ (h >> 15)
        h = h * np.int64(0x2C1B3C6D)
        h = h ^ (h >> 12)
        h = h * np.int64(0x297A2D39)
        h = h ^ (h >> 15)
    return h & np.int64(0x7FFFFFFF)


def perlin(p, seed=0):
    pi = np.floor(p)
    f = p - pi
    pi = pi.astype(np.int64)
    u = f * f * f * (f * (f * 6 - 15) + 10)
    out = np.zeros(len(p))
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                g = GRAD[_hash(pi[:, 0] + dx, pi[:, 1] + dy, pi[:, 2] + dz, seed) % 12]
                out += wx * wy * wz * (g[:, 0] * (f[:, 0] - dx) + g[:, 1] * (f[:, 1] - dy) + g[:, 2] * (f[:, 2] - dz))
    return out


def fbm(p, octaves, seed=0, gain=0.5):
    out = np.zeros(len(p))
    amp, tot, q = 1.0, 0.0, np.asarray(p, np.float64)
    for o in range(octaves):
        out += amp * perlin(q, seed + 31 * o)
        tot += amp
        amp *= gain
        q = q * 2.03 + 17.17
    return out / tot


def cells(p, seed=0):
    """Celle di Voronoi 3D: distanza dal punto piu' vicino (F1), dal secondo (F2) e un numero per cella (0..1)."""
    pi = np.floor(p)
    f = p - pi
    pi = pi.astype(np.int64)
    d1 = np.full(len(p), 9.0)
    d2 = np.full(len(p), 9.0)
    c1 = np.zeros(len(p), np.int64)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                h = _hash(pi[:, 0] + dx, pi[:, 1] + dy, pi[:, 2] + dz, seed)
                q = np.stack([dx + (h & 1023) / 1023.0, dy + ((h >> 10) & 1023) / 1023.0, dz + ((h >> 20) & 1023) / 1023.0], 1) - f
                d = (q * q).sum(1)
                closer = d < d1
                d2 = np.where(closer, d1, np.minimum(d2, d))
                c1 = np.where(closer, h, c1)
                d1 = np.where(closer, d, d1)
    return np.sqrt(d1), np.sqrt(d2), (c1 % 9973) / 9973.0


# ------------------------------------------------------------------ dalla superficie all'atlante
class Surface:
    """I texel coperti dall'atlante con punto, normale, attributi e le direzioni dP/du e dP/dv del loro triangolo."""

    def __init__(self, path, W, H):
        t0 = time.time()
        d = np.load(path)
        self.meta = {k[5:]: d[k] for k in d.files if k.startswith('meta_')}
        self.W, self.H = W, H
        UV = d['UV'].astype(np.float64)
        Pt = d['P'].astype(np.float64)
        tri = np.full(H * W, -1, np.int64)
        bary = np.zeros((H * W, 3))
        X = UV[..., 0] * W - 0.5
        Y = (1.0 - UV[..., 1]) * H - 0.5
        xa = np.floor(X.min(1)).astype(int)
        xb = np.ceil(X.max(1)).astype(int)
        ya = np.floor(Y.min(1)).astype(int)
        yb = np.ceil(Y.max(1)).astype(int)
        eps = -0.03
        for t in range(len(UV)):
            x0, x1, y0, y1 = max(0, xa[t]), min(W - 1, xb[t]), max(0, ya[t]), min(H - 1, yb[t])
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            ax, ay, bx, by, cx, cy = X[t, 0], Y[t, 0], X[t, 1], Y[t, 1], X[t, 2], Y[t, 2]
            den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(den) < 1e-12:
                continue
            l0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
            l1 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / den
            l2 = 1.0 - l0 - l1
            ins = (l0 >= eps) & (l1 >= eps) & (l2 >= eps)
            if not ins.any():
                continue
            idx = gy[ins] * W + gx[ins]
            tri[idx] = t
            bary[idx] = np.clip(np.stack([l0[ins], l1[ins], l2[ins]], 1), 0.0, 1.0)
        self.cov = tri >= 0
        ti = tri[self.cov]
        b = bary[self.cov]
        b /= b.sum(1, keepdims=True)
        self.ti, self.b = ti, b
        self.P = np.einsum('mk,mkj->mj', b, Pt[ti])
        self.N = norm(np.einsum('mk,mkj->mj', b, d['N'].astype(np.float64)[ti]))
        self.cav = (b * d['cav'][ti]).sum(1)
        self.part = d['part'][ti]
        self.attr = {k: (b * d[k][ti]).sum(1) for k in d.files if k in ('s', 'phi')}
        if 'phi' in self.attr:
            # sul taglio delle UV (u = 0 e 1) i tre angoli di un triangolo hanno giri diversi: si prende quello del texel
            u = (np.flatnonzero(self.cov) % W + 0.5) / W
            self.attr['phi'] = u
        e1 = Pt[:, 1] - Pt[:, 0]
        e2 = Pt[:, 2] - Pt[:, 0]
        t1 = UV[:, 1] - UV[:, 0]
        t2 = UV[:, 2] - UV[:, 0]
        den = t1[:, 0] * t2[:, 1] - t2[:, 0] * t1[:, 1]
        den = np.where(np.abs(den) < 1e-14, 1e-14, den)
        Tu = (e1 * t2[:, 1:2] - e2 * t1[:, 1:2]) / den[:, None]
        Tv = (e2 * t1[:, 0:1] - e1 * t2[:, 0:1]) / den[:, None]
        self.Tu, self.Tv = Tu[ti], Tv[ti]
        self.M = len(ti)
        print(f'  {path.name}: {self.M} texel coperti su {W}x{H} ({100.0 * self.M / (W * H):.0f}%), {time.time() - t0:.1f} s')

    def image(self, vals, fill=0.0):
        vals = np.asarray(vals)
        ch = 1 if vals.ndim == 1 else vals.shape[1]
        img = np.full((self.H * self.W, ch), fill, np.float32)
        img[self.cov] = vals.reshape(self.M, ch)
        return img.reshape(self.H, self.W, ch)

    def normals(self, h):
        """Normal map (spazio tangente, verde = +v) dall'altezza h (metri, per texel coperto)."""
        img = dilate(self.image(h), self.cov.reshape(self.H, self.W), 4)[..., 0]
        hu = ((np.roll(img, -1, 1) - np.roll(img, 1, 1)) * 0.5 * self.W).ravel()[self.cov]
        hv = ((np.roll(img, 1, 0) - np.roll(img, -1, 0)) * 0.5 * self.H).ravel()[self.cov]
        Tu, Tv, N = self.Tu, self.Tv, self.N
        a11, a12, a22 = dot(Tu, Tu), dot(Tu, Tv), dot(Tv, Tv)
        det = a11 * a22 - a12 * a12
        det = np.where(np.abs(det) < 1e-24, 1e-24, det)
        ca = (a22 * hu - a12 * hv) / det
        cb = (a11 * hv - a12 * hu) / det
        n2 = norm(N - (ca[:, None] * Tu + cb[:, None] * Tv))
        t = norm(Tu - N * dot(N, Tu)[:, None])
        bt = np.cross(N, t)
        sg = np.sign(dot(bt, Tv))
        sg[sg == 0] = 1.0
        bt *= sg[:, None]
        return np.stack([dot(n2, t), dot(n2, bt), dot(n2, N)], 1)


def dilate(img, mask, iters):
    """Allarga i texel coperti nei vuoti accanto (media dei vicini coperti), 'iters' texel alla volta."""
    img = img.copy()
    m = mask.copy()
    for _ in range(iters):
        acc = np.zeros_like(img)
        cnt = np.zeros(m.shape, img.dtype)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sm = np.roll(np.roll(m, dy, 0), dx, 1)
                acc += np.roll(np.roll(img, dy, 0), dx, 1) * sm[..., None]
                cnt += sm
        new = (~m) & (cnt > 0)
        if not new.any():
            break
        img[new] = acc[new] / cnt[new][:, None]
        m |= new
    return img


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def save(S, name, albedo, nrm, ao, smoothness):
    """Le tre mappe, coi bordi delle isole allargati di 16 texel e il resto riempito con la media."""
    OUT.mkdir(parents=True, exist_ok=True)
    m2 = S.cov.reshape(S.H, S.W)
    grown = dilate(S.image(np.ones(S.M)), m2, 16)[..., 0] > 0.5

    def finish(vals, fill):
        img = dilate(S.image(vals), m2, 16)
        img[~grown] = fill
        return img
    base = finish(to_srgb(albedo), to_srgb(albedo).mean(0))
    Image.fromarray((base * 255 + 0.5).astype(np.uint8)).save(OUT / f'{name}_base.png', optimize=True)
    n = finish(nrm * 0.5 + 0.5, np.array([0.5, 0.5, 1.0]))
    Image.fromarray((np.clip(n, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / f'{name}_normal.png', optimize=True)
    mk = np.stack([np.zeros(S.M), np.clip(ao, 0, 1), np.zeros(S.M), np.clip(smoothness, 0, 1)], 1)
    mk = finish(mk, np.array([0.0, 1.0, 0.0, smoothness.mean()]))
    Image.fromarray((mk * 255 + 0.5).astype(np.uint8)).save(OUT / f'{name}_mask.png', optimize=True)
    print(f'  scritte {name}_base/normal/mask.png ({S.W}x{S.H})')


# ------------------------------------------------------------------ cuciture
def stitch_row(off, along, pitch=0.0028, duty=0.64, width=0.00028):
    """Una fila di punti: 'off' e' la distanza (metri, con segno) dalla linea della fila, 'along' la coordinata lungo la fila.
    Ritorna (filo 0..1, altezza in metri, fori 0..1)."""
    ph = along / pitch
    fr = ph - np.floor(ph)
    dash = smooth(0.0, 0.10, fr) * smooth(duty, duty - 0.10, fr)
    across = np.exp(-(off / width) ** 2)
    thread = dash * across
    # il filo e' tondo: piu' alto a meta' del punto
    hump = np.sin(np.clip(fr / duty, 0, 1) * np.pi) ** 0.6
    height = 0.00011 * thread * hump
    holes = (np.exp(-(fr / 0.05) ** 2) + np.exp(-((fr - 1.0) / 0.05) ** 2) + np.exp(-((fr - duty) / 0.05) ** 2)) * np.exp(-(off / (width * 0.9)) ** 2)
    return np.clip(thread, 0, 1), height - 0.00005 * holes, np.clip(holes, 0, 1)


def seg_param(P, A, B):
    ab = B - A
    L = np.linalg.norm(ab)
    d = ab / L
    t = np.clip(dot(P - A, d) / L, 0.0, 1.0)
    return t, A + np.outer(t * L, d), d, L


def polyline_dist2d(xy, pts):
    """Distanza nel piano (x, y) da una polilinea e la coordinata lungo di essa (metri)."""
    best = np.full(len(xy), 9.0)
    along = np.zeros(len(xy))
    acc = 0.0
    for a, b in zip(pts[:-1], pts[1:]):
        a, b = np.asarray(a, np.float64), np.asarray(b, np.float64)
        ab = b - a
        L = np.linalg.norm(ab)
        t = np.clip(((xy - a) @ ab) / (L * L), 0.0, 1.0)
        q = a + np.outer(t, ab)
        dd = np.linalg.norm(xy - q, axis=1)
        upd = dd < best
        best = np.where(upd, dd, best)
        along = np.where(upd, acc + t * L, along)
        acc += L
    return best, along


# ------------------------------------------------------------------ guanto
LEATHER = np.array([0.040, 0.025, 0.017])
SUEDE = np.array([0.040, 0.032, 0.026])
THREAD = np.array([0.105, 0.075, 0.047])
NYLON = np.array([0.020, 0.020, 0.022])
BLOOD = np.array([0.050, 0.0075, 0.0055])


def glove():
    t0 = time.time()
    S = Surface(IN / 'mano_libera_uv.npz', 2048, 2048)
    P, N, cav, part, mt = S.P, S.N, S.cav, S.part, S.meta
    fingers = mt['fingers']
    thumb = mt['thumb']
    radii = mt['radii'] * float(mt['gs'])
    thumb_r = mt['thumb_r']
    y_cuff_end = float(mt['cuff'][1])
    Mn = S.M
    is_strap = part == 1
    zhat = np.array([0.0, 0.0, 1.0])

    # --- il segmento (falange) piu' vicino: dito, punto sull'asse, verso del dorso
    segs = []
    for f in range(4):
        cum = 0.0
        for i in range(3):
            A, B = fingers[f][i], fingers[f][i + 1]
            d = (B - A) / np.linalg.norm(B - A)
            dor = zhat - d * d[2]
            segs.append(dict(A=A, B=B, rA=radii[f][i], rB=radii[f][i + 1], f=f, i=i, dor=dor / np.linalg.norm(dor), cum=cum))
            cum += np.linalg.norm(B - A)
        for s in segs[-3:]:
            s['total'] = cum
            s['rtip'] = radii[f][3]
    tcum = 0.0
    for i in range(3):
        A, B = thumb[i], thumb[i + 1]
        d = (B - A) / np.linalg.norm(B - A)
        dor = np.array([-0.70, 0.0, 0.72]) - d * dot(np.array([-0.70, 0.0, 0.72]), d)
        segs.append(dict(A=A, B=B, rA=thumb_r[i], rB=thumb_r[i + 1], f=4, i=i, dor=dor / np.linalg.norm(dor), cum=tcum))
        tcum += np.linalg.norm(B - A)
    for s in segs[-3:]:
        s['total'] = tcum
        s['rtip'] = thumb_r[3]
    best = np.full(Mn, 9.0)
    k_best = np.zeros(Mn, np.int64)
    for k, s in enumerate(segs):
        t, Q, d, L = seg_param(P, s['A'], s['B'])
        sd = np.linalg.norm(P - Q, axis=1) - (s['rA'] + (s['rB'] - s['rA']) * t)
        upd = sd < best
        best = np.where(upd, sd, best)
        k_best = np.where(upd, k, k_best)
    # dorso e palmo: l'ellissoide del palmo di build_free_bm
    pc, pr = np.array([0.003, 0.046, -0.002]), np.array([0.036 * 1.08, 0.047, 0.0125 * 1.08])
    sd_palm = (np.linalg.norm((P - pc) / pr, axis=1) - 1.0) * 0.0135
    on_digit = (best < sd_palm - 0.0012) & ~is_strap
    cuff = (P[:, 1] < -0.004) & ~is_strap & ~((k_best >= 12) & (best < 0.002))
    on_digit &= ~cuff
    on_palm = ~on_digit & ~cuff & ~is_strap

    # campi per dito: asse, verso, distanza dal piano dorso-palmo (delta), coordinata lungo la cucitura
    delta = np.zeros(Mn)
    along = np.zeros(Mn)
    ax_t = np.zeros(Mn)
    finger_id = np.full(Mn, -1)
    seg_i = np.full(Mn, -1)
    cosd = np.zeros(Mn)
    rad = np.zeros(Mn)
    axial = np.zeros(Mn)
    for k, s in enumerate(segs):
        sel = np.flatnonzero(on_digit & (k_best == k))
        if len(sel) == 0:
            continue
        p = P[sel]
        t, Q, d, L = seg_param(p, s['A'], s['B'])
        R = p - Q
        rr = np.linalg.norm(R, axis=1)
        dl = dot(R, s['dor'])
        lat_dir = np.cross(d, s['dor'])
        lat = dot(R, lat_dir)
        a_lin = s['cum'] + t * L
        # la fila sale lungo un fianco, gira attorno al polpastrello e scende dall'altro: una coordinata continua
        rt = s['rtip']
        al = np.where(lat < 0, a_lin, 2 * s['total'] + np.pi * rt - a_lin)
        if s['i'] == 2:
            ex = dot(p - s['B'], d)
            psi = np.arctan2(lat, np.maximum(ex, 1e-9))
            al = np.where(ex > 0, s['total'] + rt * (psi + np.pi / 2), al)
        delta[sel] = dl
        along[sel] = al
        ax_t[sel] = t
        finger_id[sel] = s['f']
        seg_i[sel] = s['i']
        cosd[sel] = dl / np.maximum(rr, 1e-6)
        rad[sel] = rr
        axial[sel] = a_lin
    # sul palmo e sul dorso il piano dorso-palmo e' z = -2 mm; la cucitura gira sui fianchi
    zmid = -0.002
    delta = np.where(on_palm, P[:, 2] - zmid, delta)
    along = np.where(on_palm, P[:, 1] + 0.6 * np.abs(P[:, 0]), along)
    dorsal = np.where(on_digit | on_palm, smooth(-0.0003, 0.0003, delta), 1.0)
    suede = (1.0 - dorsal) * (on_digit | on_palm)

    h = np.zeros(Mn)
    thread = np.zeros(Mn)
    crease = np.zeros(Mn)
    groove_ao = np.zeros(Mn)

    # --- grana del cuoio (celle) e pelo dello scamosciato
    f1, f2, cid = cells(P / 0.00062, seed=3)
    valley = 1.0 - smooth(0.0, 0.20, f2 - f1)
    fine = fbm(P / 0.00018, 2, seed=5)
    h += dorsal * (-0.000016 * valley + 0.000007 * (cid - 0.5) + 0.000003 * fine)
    nap = fbm(P / 0.00022, 3, seed=7)
    h += suede * 0.000006 * nap

    # --- cucitura dorso-palmo lungo dita, pollice e fianchi del palmo, con la fila di punti sul dorso
    side_palm = on_palm & (np.abs(N[:, 2]) < 0.55)
    seam_on = (on_digit & ~((seg_i == 0) & (ax_t < 0.30) & (finger_id < 4))) | side_palm
    seam_g = np.exp(-(delta / 0.00045) ** 2) * seam_on
    h -= 0.00013 * seam_g
    groove_ao += seam_g
    th, hh, holes = stitch_row(delta - 0.0013, along)
    th *= seam_on
    h += hh * seam_on
    thread = np.maximum(thread, th)
    groove_ao += 0.5 * holes * seam_on

    # --- i tre 'punti' sul dorso: cordoncino cucito dalle valli tra le nocche verso il polso
    dors_back = on_palm & (P[:, 2] > 0.004)
    for j in range(3):
        a = 0.5 * (fingers[j][0] + fingers[j + 1][0])
        start = np.array([a[0], a[1] - 0.012])
        end = np.array([a[0] * 0.55 + 0.0015, a[1] - 0.056])
        dd, al = polyline_dist2d(P[:, :2], [start, end])
        L = np.linalg.norm(end - start)
        fade = smooth(0.0, 0.006, al) * smooth(L, L - 0.010, al)
        cord = np.exp(-(dd / 0.0006) ** 2) * fade * dors_back
        h += 0.00012 * cord
        for side in (-1.0, 1.0):
            # distanza con segno dalla linea: lato dal prodotto vettoriale
            dvec = (end - start) / L
            sgn = np.sign((P[:, 0] - start[0]) * dvec[1] - (P[:, 1] - start[1]) * dvec[0])
            tt, hs, hl = stitch_row(dd * sgn - side * 0.0011, al, pitch=0.0024, width=0.00024)
            m_ = fade * dors_back
            h += hs * m_
            thread = np.maximum(thread, tt * m_)
            groove_ao += 0.4 * hl * m_

    # --- contorno cucito delle imbottiture delle nocche
    for f in range(4):
        c = fingers[f][0]
        rx, ry = radii[f][0] * 0.95 * 1.12, 0.0085 * 1.15
        ex = (P[:, 0] - c[0]) / rx
        ey = (P[:, 1] - (c[1] - 0.002)) / ry
        e = np.sqrt(ex * ex + ey * ey)
        m_ = (P[:, 2] > 0.002) & ~cuff & ~is_strap & (N[:, 2] > 0.2)
        off = (e - 1.0) * min(rx, ry)
        al = np.arctan2(ey, ex) * 0.5 * (rx + ry)
        tt, hs, hl = stitch_row(off, al, pitch=0.0024, width=0.00024)
        h += hs * m_
        thread = np.maximum(thread, tt * m_)
        rim = np.exp(-((off + 0.0006) / 0.0007) ** 2) * m_
        h -= 0.00006 * rim
        groove_ao += 0.4 * (hl * m_ + rim)

    # --- pieghe delle articolazioni: sul dorso due o tre solchi di traverso, sotto un solco profondo
    wob_all = 0.00045 * fbm(P / 0.004, 2, seed=40)
    for f in range(5):
        joints = (fingers[f] if f < 4 else thumb)
        for jn in (1, 2) if f < 4 else (1, 2):
            J = joints[jn]
            dprev = joints[jn] - joints[jn - 1]
            dnext = joints[jn + 1] - joints[jn]
            d = norm(dprev / np.linalg.norm(dprev) + dnext / np.linalg.norm(dnext))
            sel = on_digit & (finger_id == f)
            a = dot(P - J, d)
            w_ = np.zeros(Mn)
            n_lines = 3 if jn == 1 else 2
            wob = wob_all * (1.0 if (f + jn) % 2 else -1.0)
            ridge = np.zeros(Mn)
            latc = np.arcsin(np.clip(np.where(sel, dot(np.cross(d, zhat)[None, :], norm(P - J)), 0.0), -1, 1))
            for kk in range(n_lines):
                ak = (kk - (n_lines - 1) / 2) * 0.0019 + 0.0005 * (1 - cosd) + wob
                # ogni piega ha la sua lunghezza: si spegne prima su un fianco o sull'altro
                c0 = 0.35 * np.sin(1.7 * kk + 2.3 * f + jn)
                env = np.exp(-((latc - c0) / (0.55 + 0.25 * ((kk + f) % 2))) ** 2)
                w_ = np.maximum(w_, np.exp(-((a - ak) / 0.00052) ** 2) * env * (1.0 - 0.22 * kk))
                ridge = np.maximum(ridge, np.exp(-((a - ak - 0.00095) / 0.0005) ** 2) * env)
            back = smooth(0.10, 0.60, cosd)
            cd = w_ * back * sel
            h -= 0.00010 * cd
            h += 0.00004 * ridge * back * sel
            crease = np.maximum(crease, cd)
            # sotto: un solco unico, appena oltre l'articolazione, morbido
            under = smooth(-0.2, -0.6, cosd) * sel
            cu = np.exp(-((a - 0.0007 + 0.6 * wob) / 0.0007) ** 2) * under
            h -= 0.00012 * cu
            crease = np.maximum(crease, 0.7 * cu)
    # la piega alla base delle dita (il palmo finisce e comincia il dito)
    for f in range(4):
        J = fingers[f][0]
        d = norm(fingers[f][1] - fingers[f][0])
        a = dot(P - J, d)
        sel = (on_digit | on_palm) & (P[:, 2] < zmid) & (np.abs(P[:, 0] - J[0]) < radii[f][0] * 1.4)
        wq = 0.0006 * fbm(P / 0.005, 2, seed=50 + f)
        cu = np.exp(-((a - 0.017 + wq) / 0.0009) ** 2) * sel + 0.5 * np.exp(-((a - 0.0212 + wq) / 0.0007) ** 2) * sel
        h -= 0.00010 * cu
        crease = np.maximum(crease, 0.6 * cu)
    # le pieghe del palmo (scamosciato): distale, prossimale e attorno al pollice
    palm_under = on_palm & (P[:, 2] < zmid - 0.004)
    wob_palm = 0.0005 * fbm(P / 0.006, 2, seed=60)
    for pts, amp in (([(0.041, 0.069), (0.033, 0.071), (0.024, 0.074), (0.014, 0.077), (0.004, 0.080), (-0.008, 0.084)], 1.0),
                     ([(-0.033, 0.063), (-0.022, 0.061), (-0.010, 0.058), (0.002, 0.055), (0.016, 0.051), (0.032, 0.046)], 0.85),
                     ([(-0.030, 0.062), (-0.025, 0.052), (-0.020, 0.042), (-0.015, 0.031), (-0.010, 0.019), (-0.005, 0.006)], 0.75)):
        dd, al = polyline_dist2d(P[:, :2], np.array(pts))
        L = sum(np.linalg.norm(np.subtract(b_, a_)) for a_, b_ in zip(pts[:-1], pts[1:]))
        ends = smooth(0.0, 0.008, al) * smooth(L, L - 0.010, al)
        cu = np.exp(-((dd + wob_palm * amp) / 0.0011) ** 2) * palm_under * amp * ends
        h -= 0.00010 * cu
        crease = np.maximum(crease, 0.6 * cu)

    # --- manichetta: l'orlo cucito in fondo, la cucitura al polso, l'elastico arricciato sotto il polso
    ang = np.arctan2(P[:, 2], P[:, 0])
    rr = np.sqrt(P[:, 0] ** 2 + P[:, 2] ** 2)
    arc = ang * rr
    for yrow, ww in ((y_cuff_end + 0.0050, 1.0), (y_cuff_end + 0.0085, 1.0), (-0.0135, 1.0)):
        tt, hs, hl = stitch_row(P[:, 1] - yrow, arc, pitch=0.0030)
        m_ = cuff * ww
        h += hs * m_
        thread = np.maximum(thread, tt * m_)
        groove_ao += 0.4 * hl * m_
    hem = smooth(y_cuff_end + 0.0030, y_cuff_end + 0.0010, P[:, 1]) * cuff
    h += 0.00025 * hem
    wseam = np.exp(-((P[:, 1] + 0.0100) / 0.0005) ** 2) * cuff
    h -= 0.00015 * wseam
    groove_ao += wseam
    gather_zone = cuff & (P[:, 2] < -0.008) & (P[:, 1] > -0.026)
    gath = np.sin(arc / 0.0019 * 2 * np.pi + 2.0 * fbm(P / 0.01, 2, seed=70)) * smooth(-0.026, -0.020, P[:, 1]) * smooth(-0.004, -0.008, P[:, 1])
    h += 0.00007 * gath * gather_zone
    crease = np.maximum(crease, np.clip(-gath, 0, 1) * gather_zone * 0.5)

    # --- cinturino: nastro di nylon a coste con i bordi cuciti
    ys = P[is_strap, 1]
    y_lo, y_hi = (np.percentile(ys, 1), np.percentile(ys, 99)) if len(ys) else (0.0, 0.0)
    ribs = 0.5 + 0.5 * np.sin(arc / 0.0009 * 2 * np.pi)
    h += is_strap * (0.000028 * ribs + 0.000006 * fine)
    for yrow in (y_lo + 0.0017, y_hi - 0.0017):
        tt, hs, hl = stitch_row(P[:, 1] - yrow, arc, pitch=0.0026, width=0.00026)
        h += hs * is_strap
        thread = np.maximum(thread, tt * is_strap * 0.7)
        groove_ao += 0.4 * hl * is_strap

    # --- consumo, sporco, sangue
    convex = np.clip(smooth(-0.015, -0.055, cav), 0, 1)
    tips = np.zeros(Mn)
    for f in range(4):
        tips = np.maximum(tips, np.exp(-(np.linalg.norm(P - fingers[f][3], axis=1) / 0.009) ** 2))
    tips = np.maximum(tips, np.exp(-(np.linalg.norm(P - thumb[3], axis=1) / 0.010) ** 2))
    # il fianco dell'indice verso il pollice (grilletto, sicura) e il dorso delle nocche
    idx_side = (finger_id == 0) & (seg_i >= 1) & (P[:, 0] < fingers[0][1][0])
    knuck = np.zeros(Mn)
    for f in range(4):
        knuck = np.maximum(knuck, np.exp(-(np.linalg.norm(P - (fingers[f][1] + np.array([0, 0, radii[f][1]])), axis=1) / 0.007) ** 2))
    breakup = smooth(-0.2, 0.5, fbm(P / 0.003, 3, seed=80))
    wear = np.clip((0.55 * convex + 0.7 * tips + 0.5 * idx_side + 0.6 * knuck) * breakup, 0, 1) * ~is_strap
    # graffi: brevi segmenti chiari sul dorso
    rng = np.random.default_rng(11)
    scratch = np.zeros(Mn)
    back_zone = (on_palm | on_digit | cuff) & (N[:, 2] > 0.25)
    for _ in range(46):
        c0 = np.array([rng.uniform(-0.035, 0.045), rng.uniform(-0.07, 0.15), rng.uniform(0.0, 0.02)])
        dirv = norm(rng.normal(size=3) * np.array([1.0, 1.0, 0.2]))
        A = c0 - dirv * rng.uniform(0.002, 0.008)
        B = c0 + dirv * rng.uniform(0.002, 0.008)
        t, Q, d, L = seg_param(P, A, B)
        sc = np.exp(-(np.linalg.norm(P - Q, axis=1) / 0.00012) ** 2) * np.sin(np.clip(t, 0, 1) * np.pi)
        scratch = np.maximum(scratch, sc * rng.uniform(0.4, 1.0))
    scratch *= back_zone
    h -= 0.00002 * scratch
    grime = np.clip(smooth(0.010, 0.050, cav) + 0.8 * groove_ao + 0.5 * crease, 0, 1)
    blood_big = smooth(0.30, 0.42, fbm(P / 0.016, 4, seed=91)) * smooth(0.05, 0.35, fbm(P / 0.05, 2, seed=92))
    blood_dots = smooth(0.50, 0.58, fbm(P / 0.0022, 2, seed=93)) * smooth(0.0, 0.3, fbm(P / 0.03, 2, seed=94))
    blood = np.clip(blood_big * 0.9 + blood_dots * 0.8, 0, 1) * (N[:, 2] > -0.1) * ~is_strap * (P[:, 1] > -0.06)

    # --- colore
    var = 1.0 + 0.16 * fbm(P / 0.018, 4, seed=1)
    col = LEATHER[None, :] * var[:, None]
    col *= (1.0 - 0.28 * valley * dorsal)[:, None]
    sc_ = SUEDE[None, :] * (1.0 + 0.10 * fbm(P / 0.007, 3, seed=2) + 0.05 * nap)[:, None]
    col = lerp(col, sc_, suede)
    col = lerp(col, col * 1.55 + np.array([0.012, 0.008, 0.005]), 0.55 * wear * (1.0 - 0.6 * suede))
    col = lerp(col, col * 1.9 + 0.02, 0.6 * scratch)
    col *= (1.0 - 0.16 * crease)[:, None]
    col *= (1.0 - 0.32 * grime)[:, None]
    nylon = NYLON[None, :] * (1.0 + 0.25 * (ribs - 0.5) + 0.06 * fine)[:, None]
    col = np.where(is_strap[:, None], nylon, col)
    col = lerp(col, THREAD[None, :] * (1.0 + 0.15 * fine)[:, None], np.clip(thread, 0, 1))
    col = lerp(col, BLOOD[None, :] * (1.0 + 0.3 * fine)[:, None], 0.78 * blood)

    # --- lucidita' e occlusione
    sm = 0.50 - 0.16 * valley * dorsal + 0.03 * fine
    sm = lerp(sm, 0.13 + 0.03 * nap, suede)
    sm = sm + 0.14 * wear * dorsal - 0.10 * crease - 0.12 * grime
    sm = np.where(is_strap, 0.27 + 0.06 * ribs, sm)
    sm = lerp(sm, 0.24, np.clip(thread, 0, 1))
    sm = lerp(sm, 0.52, 0.7 * blood)
    ao = 1.0 - 0.25 * crease - 0.35 * np.clip(groove_ao, 0, 1) - 0.30 * smooth(0.012, 0.06, cav)

    nrm = S.normals(h)
    save(S, 'guanto_libero', col, nrm, ao, sm)
    print(f'[texture] guanto: dita {on_digit.mean() * 100:.0f}%, palmo e dorso {on_palm.mean() * 100:.0f}%, manichetta {cuff.mean() * 100:.0f}%, '
          f'cinturino {is_strap.mean() * 100:.0f}%, scamosciato {suede.mean() * 100:.0f}%, {time.time() - t0:.1f} s')


# ------------------------------------------------------------------ manica
CANVAS = np.array([0.080, 0.068, 0.047])
PATCH = np.array([0.050, 0.036, 0.025])
SLEEVE_THREAD = np.array([0.120, 0.100, 0.068])


def sleeve():
    t0 = time.time()
    S = Surface(IN / 'manica_libera_uv.npz', 1024, 2048)
    P, N, cav, mt = S.P, S.N, S.cav, S.meta
    s, phi = S.attr['s'], S.attr['phi']
    Mn = S.M
    sE = float(mt['sE'])
    phi_in = ((float(mt['th_in']) - float(mt['th0'])) / (2 * np.pi)) % 1.0
    phi_out = (phi_in + 0.5) % 1.0
    circ = np.linalg.norm(S.Tu, axis=1)
    arc = phi * circ
    sd_seam = np.where(phi < 0.5, phi, phi - 1.0) * circ
    d_out = (((phi - phi_out + 0.5) % 1.0) - 0.5) * circ
    h = np.zeros(Mn)
    thread = np.zeros(Mn)
    groove = np.zeros(Mn)

    # --- fiammature della tela: fili piu' grossi lungo l'ordito (s) e lungo la trama (il giro)
    warp = perlin(np.stack([arc / 0.0011, s / 0.045, np.full(Mn, 3.3)], 1), seed=5)
    weft = perlin(np.stack([arc / 0.05, s / 0.0010, np.full(Mn, 7.7)], 1), seed=6)
    slub = np.clip(0.6 * warp + 0.4 * weft, -1, 1)
    h += 0.000010 * slub

    # --- polsino: impunture vicino all'orlo e alla cucitura del polsino
    for srow in (0.0058, 0.0098, 0.0315, 0.0405):
        tt, hs, hl = stitch_row(s - srow, arc, pitch=0.0032, width=0.00032)
        h += hs
        thread = np.maximum(thread, tt)
        groove += 0.4 * hl
    hem_edge = smooth(0.0025, 0.0, s)

    # --- cucitura ribattuta interna: fascia rialzata, solco nel mezzo, due file di punti da un lato
    band = smooth(0.0052, 0.0030, np.abs(sd_seam - 0.0012))
    h += 0.00022 * band
    sg = np.exp(-(sd_seam / 0.0006) ** 2)
    h -= 0.00016 * sg
    groove += sg
    for off in (0.0021, 0.0043):
        tt, hs, hl = stitch_row(sd_seam - off, s, pitch=0.0032, width=0.00032)
        h += hs
        thread = np.maximum(thread, tt)
        groove += 0.4 * hl

    # --- toppa del gomito: ovale scamosciato con l'orlo ripiegato e cucito
    ex = d_out / 0.042
    ey = (s - (sE + 0.012)) / 0.055
    e = np.sqrt(ex * ex + ey * ey)
    patch = smooth(1.0, 0.985, e)
    h += 0.00030 * patch + 0.00012 * np.exp(-((e - 0.975) / 0.012) ** 2)
    tt, hs, hl = stitch_row((e - 0.91) * 0.048, np.arctan2(ey, ex) * 0.048, pitch=0.0034, width=0.00034)
    h += hs * patch
    thread = np.maximum(thread, tt * patch)
    groove += 0.4 * hl * patch
    nap = fbm(P / 0.0003, 2, seed=12)
    h += patch * 0.000006 * nap

    # --- colore: tela, sbiadita sul lato esterno, piu' scura nelle pieghe, consumata sulle creste, sporca verso il polso
    outer = 0.5 + 0.5 * np.cos(2 * np.pi * (phi - phi_out))
    var = 1.0 + 0.09 * fbm(P / 0.03, 4, seed=1) + 0.05 * slub
    col = CANVAS[None, :] * var[:, None]
    gray = col.mean(1, keepdims=True)
    col = lerp(col, gray * 1.25, 0.22 * outer)
    ridge = np.clip(smooth(-0.010, -0.045, cav) * smooth(-0.3, 0.4, fbm(P / 0.006, 3, seed=3)), 0, 1)
    col = lerp(col, col.mean(1, keepdims=True) * 1.45 + 0.01, 0.40 * ridge)
    valley = smooth(0.006, 0.040, cav)
    near_cuff = smooth(0.11, 0.0, s) * (0.5 + 0.5 * smooth(-0.3, 0.5, fbm(P / 0.012, 3, seed=4)))
    under = 0.5 + 0.5 * np.cos(2 * np.pi * (phi - phi_in))
    mud = smooth(0.42, 0.55, fbm(P / 0.004, 3, seed=8)) * smooth(0.18, 0.02, s) * (0.4 + 0.6 * under)
    grime = np.clip(0.55 * valley + 0.45 * near_cuff + 0.6 * groove, 0, 1)
    col *= (1.0 - 0.35 * grime)[:, None]
    col = lerp(col, np.array([0.045, 0.036, 0.026]), 0.65 * mud)
    col = lerp(col, PATCH[None, :] * (1.0 + 0.12 * fbm(P / 0.006, 3, seed=9) + 0.05 * nap)[:, None], patch)
    col = lerp(col, col * 1.6 + 0.015, 0.5 * hem_edge)
    col = lerp(col, SLEEVE_THREAD[None, :] * (1.0 + 0.1 * slub)[:, None], np.clip(thread, 0, 1))
    blood = smooth(0.34, 0.46, fbm(P / 0.014, 4, seed=21)) * smooth(0.0, 0.3, fbm(P / 0.05, 2, seed=22)) * smooth(0.26, 0.10, s)
    blood = np.clip(blood + 0.7 * smooth(0.52, 0.60, fbm(P / 0.0025, 2, seed=23)) * smooth(0.2, 0.05, s) * smooth(0.0, 0.4, outer), 0, 1)
    col = lerp(col, BLOOD[None, :] * 1.2, 0.72 * blood)

    sm = 0.17 + 0.05 * ridge - 0.05 * grime + 0.02 * slub
    sm = lerp(sm, 0.10, patch)
    sm = lerp(sm, 0.22, np.clip(thread, 0, 1))
    sm = lerp(sm, 0.40, 0.6 * blood)
    ao = 1.0 - 0.40 * smooth(0.006, 0.05, cav) - 0.35 * np.clip(groove, 0, 1)

    nrm = S.normals(h)
    save(S, 'manica_libera', col, nrm, ao, sm)
    print(f'[texture] manica: gomito a {sE:.3f} m, giro interno {phi_in:.3f}, toppa {patch.mean() * 100:.1f}%, {time.time() - t0:.1f} s')


def detail():
    """La saia della giacca (Textures/Hotel/giacca_base.png, ripetibile) in grigio attorno a 0,5: dettaglio moltiplicato per 2."""
    src = Image.open(TEX / 'Hotel' / 'giacca_base.png').convert('RGB')
    a = np.asarray(src, np.float64) / 255.0
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    g = 0.5 + (lum / lum.mean() - 1.0) * 0.5 * 0.8
    g = np.clip(g, 0, 1)
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray((np.repeat(g[..., None], 3, 2) * 255 + 0.5).astype(np.uint8)).save(OUT / 'tela_dettaglio_base.png', optimize=True)
    print(f'[texture] tela_dettaglio_base.png {src.size[0]}x{src.size[1]}')


if __name__ == '__main__':
    want = set(sys.argv[1:]) or {'guanto', 'manica', 'dettaglio'}
    if 'guanto' in want:
        glove()
    if 'manica' in want:
        sleeve()
    if 'dettaglio' in want:
        detail()
