"""Toolkit for realistic, seamlessly repeating PBR materials (numpy + Pillow only, no engine, no game names).

It replaces the effects that read as 'cartoon' in the older generators: cracks that are the borders of a Voronoi diagram, veins that
are iso-lines of one noise (closed rivers), clouds with no structure, features that only make sense as one big unique picture.
What it offers instead:

* ``gauss_field``  - periodic Gaussian random fields with a chosen band of spatial frequencies (cycles per tile), a spectral slope and
  an optional stretch. Natural mottling, stains, grain, brushed or laminated structure; the level sets of such a field have the
  topology of real cracks and veins.
* ``contour``      - thin lines along the zero level of a field with a constant width in pixels whatever the slope of the field, so
  hairline cracks and veins keep their thickness (a plain threshold gives fat blobs where the field is flat).
* ``strokes``      - soft random strokes (scratches, scuffs, heel marks).
* ``voronoi``      - periodic cells with the offset from the nearest point (domed stones, rubble).
* ``normals_mm``   - normal map from a height map in millimetres and the size of a pixel, so relief means the same at any resolution.
* ``write_set``    - the three maps every material ships (colour, normal, mask: R metal, G occlusion, A smoothness).

Everything is periodic by construction (FFT fields, wrapped differences): a texture tiles with no seam. Conventions are those of
``genera_texture.py``: sRGB colour, OpenGL normals (green = up), mask R metallic / G occlusion / A smoothness.
"""
from pathlib import Path

import numpy as np
from PIL import Image

F = np.float32


# ---------------------------------------------------------------- basics
def zs(a):
    """Zero mean, unit variance."""
    a = np.asarray(a, F)
    return ((a - a.mean()) / (a.std() + 1e-9)).astype(F)


def smooth(e0, e1, x):
    """Smoothstep (arguments may be arrays)."""
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0)
    return (t * t * (3.0 - 2.0 * t)).astype(F)


def mix(a, b, t):
    """a + (b - a) * t with t a scalar or an (n, n) field and a, b colours (3,) or images (n, n, 3)."""
    t = np.asarray(t, F)
    if t.ndim == 2:
        t = t[..., None]
    return (np.asarray(a, F) + (np.asarray(b, F) - np.asarray(a, F)) * t).astype(F)


def rgb(c, n):
    """A flat colour as an (n, n, 3) image."""
    return np.broadcast_to(np.array(c, F), (n, n, 3)).copy()


def gauss_field(n, rng, lo=1.0, hi=None, slope=1.0, aniso=(1.0, 1.0)):
    """Periodic Gaussian random field, z-scored (mean 0, deviation 1).

    lo, hi  band of spatial frequencies in cycles per tile (the field has nothing below lo nor above hi);
    slope   amplitude falls as f^-slope: 0 is band-limited white noise (grain), 1 to 1.5 is cloudy;
    aniso   (ax, ay): features are ax times longer along x and ay times longer along y (wood grain, brushed metal, strata).
    """
    hi = n / 2.0 if hi is None else min(hi, n / 2.0)
    w = rng.standard_normal((n, n)).astype(F)
    spec = np.fft.rfft2(w)
    fy = (np.fft.fftfreq(n) * n)[:, None].astype(F)
    fx = (np.fft.rfftfreq(n) * n)[None, :].astype(F)
    f = np.sqrt((fx * aniso[0]) ** 2 + (fy * aniso[1]) ** 2)
    f = np.maximum(f, 1e-3)
    amp = f ** (-slope) / (1.0 + (f / hi) ** 6) / (1.0 + (lo / f) ** 6)
    amp[0, 0] = 0.0
    return zs(np.fft.irfft2(spec * amp.astype(F), s=(n, n)))


def blur(a, sigma):
    """Periodic Gaussian blur (FFT), sigma in pixels. Works on (n, n) and (n, n, c)."""
    if sigma <= 0:
        return np.asarray(a, F)
    a = np.asarray(a, F)
    if a.ndim == 3:
        return np.stack([blur(a[..., i], sigma) for i in range(a.shape[2])], -1)
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None].astype(F)
    fx = np.fft.rfftfreq(w)[None, :].astype(F)
    g = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy)).astype(F)
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(h, w)).astype(F)


def gradmag(a):
    """Magnitude of the gradient by wrapped central differences (per pixel)."""
    dx = (np.roll(a, -1, 1) - np.roll(a, 1, 1)) * 0.5
    dy = (np.roll(a, -1, 0) - np.roll(a, 1, 0)) * 0.5
    return np.hypot(dx, dy)


def contour(field, width, feather=0.8):
    """Thin line along the zero level of ``field``, ``width`` pixels wide (a scalar or an (n, n) field), 0..1.
    The distance to the zero level is estimated as |f| / |grad f|, which is what keeps the width constant."""
    d = np.abs(field) / (gradmag(field) + 1e-5)
    half = np.asarray(width, F) * 0.5
    return (1.0 - smooth(half - feather, half + feather, d)).astype(F)


def hash2(ix, iy, seed):
    """Deterministic 0..1 value per integer cell."""
    h = (ix.astype(np.int64) * 73856093) ^ (iy.astype(np.int64) * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF).astype(F) / 65535.0


def grid_cells(n, cells):
    """Regular square tiles: (cell x, cell y, local x px, local y px, distance to the nearest tile edge in px)."""
    ts = n / cells
    y, x = np.mgrid[0:n, 0:n].astype(F)
    cx, cy = np.floor(x / ts).astype(int), np.floor(y / ts).astype(int)
    lx, ly = x - cx * ts, y - cy * ts
    edge = np.minimum(np.minimum(lx, ts - lx), np.minimum(ly, ts - ly))
    return cx, cy, lx, ly, edge


def strokes(n, rng, count, length, width, curve=0.0, direction=None, spread=None, weight=(0.4, 1.0)):
    """Soft random strokes (scratches, scuffs, heel marks), 0..1.

    length (min, max) in px; width = Gaussian sigma in px; curve = total bend in radians along a stroke;
    direction = fixed angle in radians (with ``spread`` of random deviation) instead of a random one."""
    acc = np.zeros((n, n), F)
    for _ in range(count):
        x0, y0 = rng.random(2) * n
        a = rng.random() * 2 * np.pi if direction is None else direction + (rng.random() - 0.5) * (spread or 0.0)
        steps = max(3, int(rng.uniform(*length)))
        t = np.linspace(0.0, 1.0, steps)
        ang = a + curve * (t - 0.5) * (rng.random() * 2 - 1)
        xs = (x0 + np.cumsum(np.cos(ang))) % n
        ys = (y0 + np.cumsum(np.sin(ang))) % n
        np.add.at(acc, (ys.astype(int), xs.astype(int)), rng.uniform(*weight) * (0.35 + 0.65 * np.sin(np.pi * t)))
    return np.clip(blur(acc, width) * (2.5 * width), 0.0, 1.0).astype(F)


def voronoi(n, cells, rng, jitter=0.85):
    """Periodic Voronoi: distances to the nearest and second nearest point (in cells), the id of the nearest point and the
    offset (dx, dy, in cells) from it. d2 - d1 is zero on the borders and grows linearly away from them."""
    pts = 0.5 + (rng.random((cells, cells, 2)).astype(F) - 0.5) * jitter
    c = (np.arange(n, dtype=F) + 0.5) * cells / n
    cy, cx = np.meshgrid(c, c, indexing="ij")
    ix, iy = np.floor(cx).astype(int), np.floor(cy).astype(int)
    d1 = np.full((n, n), 9, F)
    d2 = np.full((n, n), 9, F)
    ident = np.zeros((n, n), np.int64)
    ox = np.zeros((n, n), F)
    oy = np.zeros((n, n), F)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            jx, jy = ix + di, iy + dj
            p = pts[jy % cells, jx % cells]
            dx, dy = cx - (jx + p[..., 0]), cy - (jy + p[..., 1])
            d = np.hypot(dx, dy)
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d))
            ident = np.where(closer, (jy % cells) * cells + (jx % cells), ident)
            ox = np.where(closer, dx, ox)
            oy = np.where(closer, dy, oy)
            d1 = np.minimum(d1, d)
    return d1, d2, ident, ox, oy


def sample(img, xs, ys):
    """Bilinear lookup of a 2D field at fractional pixel coordinates, wrapping around the edges."""
    h, w = img.shape
    x0 = np.floor(xs).astype(np.int64)
    y0 = np.floor(ys).astype(np.int64)
    fx, fy = (xs - x0).astype(F), (ys - y0).astype(F)
    x0 %= w
    y0 %= h
    x1, y1 = (x0 + 1) % w, (y0 + 1) % h
    a = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
    b = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
    return (a * (1 - fy) + b * fy).astype(F)


def stretch(field, angle, along=1.0, across=1.0):
    """Resample a field with its features made ``along`` times longer in the direction ``angle`` (radians) and ``across`` times
    longer across it. Fine for a texture used once (a tile cut from its own stone); a periodic field stays periodic only for
    angle 0 and 90 degrees."""
    n = field.shape[0]
    y, x = np.mgrid[0:n, 0:n].astype(F)
    c, s = np.cos(angle), np.sin(angle)
    xr, yr = x * c + y * s, -x * s + y * c
    return sample(field, xr / along, yr / across)


# ---------------------------------------------------------------- output
def normals_mm(h_mm, mm_per_px, strength=1.0):
    """Normal map (OpenGL, green = up) from a height map in millimetres; ``mm_per_px`` is the size of one pixel.
    Row 0 is the top of the texture (v = 1)."""
    dx = (np.roll(h_mm, -1, 1) - np.roll(h_mm, 1, 1)) * (0.5 / mm_per_px) * strength
    dy = (np.roll(h_mm, -1, 0) - np.roll(h_mm, 1, 0)) * (0.5 / mm_per_px) * strength
    nx, ny, nz = -dx, dy, np.ones_like(h_mm)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


def to8(a):
    return (np.clip(a, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def write_set(out, name, base, h_mm, smoothness, ao, mm_per_px, relief=1.0, metal=None, quiet=False):
    """Writes <name>_base.png, <name>_normal.png and <name>_mask.png (R metallic, G occlusion, A smoothness)."""
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    n = base.shape[0]
    Image.fromarray(to8(base)).save(out / f"{name}_base.png")
    Image.fromarray(to8(normals_mm(h_mm, mm_per_px, relief))).save(out / f"{name}_normal.png")
    mask = np.zeros(base.shape[:2] + (4,), F)
    mask[..., 0] = 0.0 if metal is None else metal
    mask[..., 1] = ao
    mask[..., 3] = smoothness
    Image.fromarray(to8(mask)).save(out / f"{name}_mask.png")
    if not quiet:
        lum = base @ np.array([0.2126, 0.7152, 0.0722], F)
        print(f"{name}: {base.shape[1]}x{base.shape[0]}px  mean sRGB luminance {lum.mean():.3f}  smoothness {np.mean(smoothness):.2f}")


def seam_error(img):
    """How much more the image jumps across the wrap-around edge than between two inside neighbours (about 1.0 = seamless)."""
    a = np.asarray(img, F)
    inner = (np.abs(a[:, 1:] - a[:, :-1]).mean() + np.abs(a[1:] - a[:-1]).mean()) * 0.5
    edge = (np.abs(a[:, 0] - a[:, -1]).mean() + np.abs(a[0] - a[-1]).mean()) * 0.5
    return float(edge / (inner + 1e-9))
