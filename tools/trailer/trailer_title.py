#!/usr/bin/env python3
"""Captions and the title card of the trailer (PIL + numpy + cv2; fonts from C:/Windows/Fonts).

Nothing here knows the edit: make_trailer.py asks for a caption PNG (RGBA, full frame) or for the title frames (raw RGB piped to ffmpeg).

    python tools/trailer/trailer_title.py demo OUT_DIR      # sample captions + title frames, to look at them
"""
import math
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONTS = Path(r"C:\Windows\Fonts")
WARM = (240, 232, 214)
MUTE = (196, 186, 166)
EMBER = (255, 176, 64)


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), int(round(size)))


def smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def blur(img, sigma, ds=4):
    """Gaussian blur done at 1/ds resolution (a wide glow does not need the full frame)."""
    h, w = img.shape[:2]
    small = cv2.resize(img, (max(1, w // ds), max(1, h // ds)), interpolation=cv2.INTER_AREA)
    small = cv2.GaussianBlur(small, (0, 0), max(0.5, sigma / ds))
    out = cv2.resize(small, (w, h), interpolation=cv2.INTER_LINEAR)
    return out if img.ndim == 3 or out.ndim == 2 else out[..., None]


def ease_out(x):
    x = min(1.0, max(0.0, x))
    return 1 - (1 - x) ** 3


# ------------------------------------------------------------------------------------------------ didascalie
def tracked_width(txt, f, track):
    return sum(f.getlength(c) for c in txt) + track * (len(txt) - 1)


def draw_tracked(d, x, y, txt, f, fill, track):
    for c in txt:
        d.text((x, y), c, font=f, fill=fill)
        x += f.getlength(c) + track


def scrim(W, H, top=0.62, peak=0.55):
    """Soft dark band at the bottom of the frame, so a caption reads over a bright scene."""
    ys = (np.arange(H, dtype="float32") / H - top) / (1 - top)
    a = (smooth(ys) * peak * 255).astype("uint8")
    out = np.zeros((H, W, 4), "uint8")
    out[..., 3] = a[:, None]
    return Image.fromarray(out, "RGBA")


def _layer_text(W, H, items):
    """items: [(x, y, text, font, fill, track)] -> (color layer RGBA, mask L) drawn on transparent canvases."""
    col = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    msk = Image.new("L", (W, H), 0)
    dc, dm = ImageDraw.Draw(col), ImageDraw.Draw(msk)
    for x, y, txt, f, fill, track in items:
        draw_tracked(dc, x, y, txt, f, fill + (255,), track)
        draw_tracked(dm, x, y, txt, f, 255, track)
    return col, msk


def _with_shadow(W, H, base, col, msk, radius, strength):
    sh = msk.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: int(min(255, v * strength)))
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.putalpha(sh)
    base = Image.alpha_composite(base, shadow)
    return Image.alpha_composite(base, col)


def caption_center(W, H, main, sub=None, y=0.80):
    """Lower-third callout: letter-spaced capitals (Georgia) and an optional italic line."""
    s = H / 1080.0
    f1 = font("georgia.ttf", 46 * s)
    f2 = font("georgiai.ttf", 27 * s)
    tr1 = 0.22 * 46 * s
    w1 = tracked_width(main.upper(), f1, tr1)
    items = [((W - w1) / 2, H * y, main.upper(), f1, WARM, tr1)]
    if sub:
        w2 = tracked_width(sub, f2, 1.5 * s)
        items.append(((W - w2) / 2, H * y + 70 * s, sub, f2, MUTE, 1.5 * s))
    base = scrim(W, H)
    col, msk = _layer_text(W, H, items)
    return _with_shadow(W, H, base, col, msk, 9 * s, 1.0)


def caption_weapon(W, H, name, cls, grade):
    """Weapon reveal, lower left: name, class and grade (as roman numerals)."""
    s = H / 1080.0
    f1 = font("georgiab.ttf", 62 * s)
    f2 = font("georgia.ttf", 25 * s)
    tr1 = 0.10 * 62 * s
    tr2 = 0.20 * 25 * s
    x = 0.07 * W
    y = H * 0.775
    romans = ["", "I", "II", "III", "IV", "V"]
    line2 = f"{cls}   \u00b7   GRADO {romans[max(0, min(5, int(grade)))]}"
    w2 = tracked_width(line2, f2, tr2)
    base = scrim(W, H, 0.58, 0.62)
    col, msk = _layer_text(W, H, [(x, y, name, f1, WARM, tr1), (x, y + 88 * s, line2, f2, MUTE, tr2)])
    img = _with_shadow(W, H, base, col, msk, 10 * s, 1.0)
    d = ImageDraw.Draw(img)
    d.rectangle([x, y + 76 * s, x + max(w2, 200 * s), y + 78 * s], fill=EMBER + (210,))
    return img


def caption_label(W, H, name, sub=None):
    """Bestiary tag, lower left: the enemy's name over a thin ember rule."""
    s = H / 1080.0
    f1 = font("georgiab.ttf", 44 * s)
    f2 = font("georgia.ttf", 22 * s)
    tr1 = 0.20 * 44 * s
    x = 0.07 * W
    y = H * 0.80
    w1 = tracked_width(name, f1, tr1)
    items = [(x, y, name, f1, WARM, tr1)]
    if sub:
        items.append((x, y + 78 * s, sub, f2, MUTE, 0.2 * 22 * s))
    col, msk = _layer_text(W, H, items)
    img = _with_shadow(W, H, scrim(W, H, 0.64, 0.5), col, msk, 9 * s, 1.0)
    ImageDraw.Draw(img).rectangle([x, y + 66 * s, x + max(w1, 140 * s), y + 68 * s], fill=EMBER + (200,))
    return img


# ------------------------------------------------------------------------------------------------ occhio del titolo
class Eye:
    """The logo eye: an almond that opens, an ember iris that drifts and settles on the viewer, a pupil that dilates."""

    def __init__(self, W, H, cx, cy, a=300, hmax=140):
        self.W, self.H, self.cx, self.cy, self.a, self.hmax = W, H, cx, cy, a, hmax
        self.x0, self.x1 = int(cx - a * 1.5), int(cx + a * 1.5)
        self.y0, self.y1 = int(cy - hmax * 2.4), int(cy + hmax * 2.4)
        ys, xs = np.mgrid[self.y0:self.y1, self.x0:self.x1].astype("float32")
        self.X, self.Y = xs - cx, ys - cy
        rng = np.random.default_rng(4)
        self.noise_phase = rng.random(4) * 6.28

    def render(self, o, dx, dy, dilate, strength):
        """Returns an additive RGB layer (float32 0..1) for the bounding box; o = openness 0..1."""
        X, Y, a, hm = self.X, self.Y, self.a, self.hmax
        u = np.clip(X / a, -1, 1)
        shape = np.clip(1 - u * u, 0, 1) ** 1.15
        hu = hm * o * shape
        hl = hm * o * 0.82 * shape
        inside = (Y >= -hu) & (Y <= hl) & (np.abs(X) < a)
        # distance (approximately perpendicular) to the lids, for the soft edge and the outline
        slope = np.clip(np.abs(2.3 * hm * o * u / a * np.clip(1 - u * u, 0.03, 1) ** 0.15), 0, 6)
        dist_up = (Y + hu) / np.sqrt(1 + slope ** 2)
        dist_lo = (hl - Y) / np.sqrt(1 + slope ** 2)
        d_edge = np.minimum(dist_up, dist_lo)
        alpha = smooth(d_edge / 1.6 + 0.5) * (np.abs(X) < a)
        # ambient occlusion by the lids: darker near the edges
        t = np.clip(np.where(Y < 0, -Y / np.maximum(hu, 1), Y / np.maximum(hl, 1)), 0, 1)
        ao = 1 - 0.62 * t ** 2.2
        # iris
        xi, yi = X - dx, Y - dy
        R = hm * 0.93
        rr = np.sqrt(xi * xi + yi * yi) / R
        th = np.arctan2(yi, xi)
        n1, n2, n3, n4 = self.noise_phase
        fibers = 0.5 + 0.5 * np.cos(th * 52 + 2.6 * np.sin(th * 5 + n1) + 1.7 * np.sin(rr * 9 + n2))
        fibers2 = 0.5 + 0.5 * np.cos(th * 19 + 3 * np.sin(th * 3 + n3))
        ramp_a = smooth((rr - 0.22) / 0.30)
        ramp_b = smooth((rr - 0.45) / 0.50)
        c_amber = np.array(EMBER, "float32") / 255.0
        c_red = np.array((0.70, 0.10, 0.06), "float32")
        c_dark = np.array((0.20, 0.02, 0.02), "float32")
        col = c_amber * (1 - ramp_a[..., None]) + c_red * ramp_a[..., None]
        col = col * (1 - ramp_b[..., None]) + c_dark * ramp_b[..., None]
        col *= (0.70 + 0.30 * fibers[..., None]) * (0.85 + 0.15 * fibers2[..., None])
        col *= smooth((1.04 - rr) / 0.20)[..., None]                        # limbal ring
        iris_mask = smooth((1.0 - rr) * R / 1.6 + 0.5)
        p = 0.30 + 0.12 * dilate
        pup_mask = smooth((p - rr) * R / 1.6 + 0.5)
        col = col * (1 - pup_mask[..., None]) + np.array((0.012, 0.004, 0.004), "float32") * pup_mask[..., None]
        # specular
        sx, sy = xi + 0.30 * R, yi + 0.32 * R
        spec = np.exp(-(sx * sx + sy * sy) / (2 * (0.075 * R) ** 2)) * 0.95
        sx2, sy2 = xi - 0.26 * R, yi - 0.30 * R
        spec += np.exp(-(sx2 * sx2 + sy2 * sy2) / (2 * (0.035 * R) ** 2)) * 0.5
        sclera = np.array((0.085, 0.030, 0.030), "float32")
        base = sclera * (1 - iris_mask[..., None]) + col * iris_mask[..., None]
        base = base * ao[..., None] + spec[..., None] * np.array((1.0, 0.93, 0.85), "float32") * ao[..., None]
        eye = base * alpha[..., None]
        # outline of the lids
        line = np.exp(-(np.minimum(np.abs(dist_up), np.abs(dist_lo)) / 1.7) ** 2) * (np.abs(X) < a) * (inside | (d_edge > -3))
        line = line * smooth(o * 3.0)
        eye = eye + line[..., None] * np.array(WARM, "float32") / 255.0 * 0.85
        # glow
        lum = eye * np.array((1.0, 0.8, 0.55), "float32")
        g1 = blur(lum, 28, 2)
        g2 = blur(lum, 90, 6)
        out = eye + g1 * 0.55 + g2 * 0.85 * np.array((1.0, 0.45, 0.30), "float32")
        return out * strength


def title_frames(W, H, fps, dur, text="PRYING EYES", tagline="Qualcuno ti sta guardando.", footer="IMMAGINI DALLA VERSIONE IN SVILUPPO"):
    """Generator of (W*H*3) uint8 RGB frames of the title card."""
    s = H / 1080.0
    n = int(round(dur * fps))
    cx, cy = W / 2.0, H * 0.355
    eye = Eye(W, H, cx, cy, a=290 * s, hmax=135 * s)
    ft = font("georgiab.ttf", 118 * s)
    ftag = font("georgiai.ttf", 38 * s)
    ffoot = font("georgia.ttf", 17 * s)
    yy, xx = np.mgrid[0:H, 0:W].astype("float32")
    vig = 1 - 0.55 * (((xx - W / 2) / (W * 0.62)) ** 2 + ((yy - H * 0.45) / (H * 0.70)) ** 2)
    vig = np.clip(vig, 0.25, 1)[..., None]
    rng = np.random.default_rng(12)
    ey0, ey1 = 0.50, 1.95                       # eye opens
    tt0, tt1 = 1.95, 3.35                       # title materialises
    tg0, tg1 = 3.35, 4.25                       # tagline
    fo0 = dur - 0.7                             # fade out
    for i in range(n):
        t = i / fps
        # eye
        k = (t - ey0) / (ey1 - ey0)
        o = smooth(k) ** 0.8
        if 0.0 < k < 0.30:                      # a first twitch before it really opens
            o = max(o, 0.20 * math.sin(k / 0.30 * math.pi) ** 2)
        look = smooth((t - 2.0) / 1.4)          # drifts left, then right, then settles on the viewer
        dx = (-34 * math.sin(min(1.0, (t - ey0) / 1.1) * math.pi * 0.5) + 34 * smooth((t - 1.55) / 0.9) - 0.0) * s
        dx = dx * (1 - look)
        dy = (-6 + 6 * smooth((t - 1.4) / 0.8)) * s * (1 - look)
        dil = 0.15 * math.sin(max(0, t - ey0) * 1.3) + 0.55 * smooth((t - tt0) / 0.5) * (1 - smooth((t - tt1 - 0.6) / 1.2))
        strength = smooth((t - ey0) / 0.5)
        frame = np.zeros((H, W, 3), "float32")
        if t > ey0 - 0.01:
            layer = eye.render(o, dx, dy, dil, strength)
            y0, y1, x0, x1 = eye.y0, eye.y1, eye.x0, eye.x1
            yy0, xx0 = max(0, y0), max(0, x0)
            frame[yy0:min(H, y1), xx0:min(W, x1)] += layer[yy0 - y0:min(H, y1) - y0, xx0 - x0:min(W, x1) - x0]
        # title text: drawn on a band only (the rest of the frame stays untouched)
        by0, by1 = int(H * 0.50), int(H * 0.86)
        if t > tt0:
            q = (t - tt0) / (tt1 - tt0)
            a = ease_out(q)
            track = (0.42 - 0.20 * ease_out(q)) * 118 * s
            w = tracked_width(text, ft, track)
            lay = Image.new("L", (W, by1 - by0), 0)
            draw_tracked(ImageDraw.Draw(lay), (W - w) / 2, H * 0.575 - by0, text, ft, 255, track)
            rad = max(0.0, 14 * (1 - ease_out(q * 1.15)))
            if rad > 0.4:
                lay = lay.filter(ImageFilter.GaussianBlur(rad))
            m = np.asarray(lay, "float32") / 255.0 * a
            glow = blur(m, 22 * s, 4)
            frame[by0:by1] += m[..., None] * (np.array(WARM, "float32") / 255.0) * 0.96
            frame[by0:by1] += glow[..., None] * np.array((0.55, 0.10, 0.05), "float32") * 0.9
        if t > tg0:
            q = ease_out((t - tg0) / (tg1 - tg0))
            w = tracked_width(tagline, ftag, 2.0 * s)
            lay = Image.new("L", (W, by1 - by0), 0)
            draw_tracked(ImageDraw.Draw(lay), (W - w) / 2, H * 0.745 - by0, tagline, ftag, 255, 2.0 * s)
            m = np.asarray(lay, "float32") / 255.0 * q
            frame[by0:by1] += m[..., None] * (np.array(MUTE, "float32") / 255.0)
            fy0 = int(H * 0.92)
            wf = tracked_width(footer, ffoot, 0.20 * 17 * s)
            lay = Image.new("L", (W, H - fy0), 0)
            draw_tracked(ImageDraw.Draw(lay), (W - wf) / 2, H * 0.935 - fy0, footer, ffoot, 255, 0.20 * 17 * s)
            m = np.asarray(lay, "float32") / 255.0 * q * 0.42
            frame[fy0:] += m[..., None] * (np.array(MUTE, "float32") / 255.0)
        # frame: vignette, grain, fade
        frame = frame * vig + 0.010
        frame += rng.standard_normal((H, W, 1)).astype("float32") * 0.010
        fade = 1.0 if t < fo0 else float(1 - smooth((t - fo0) / 0.7))
        frame = np.clip(frame * fade, 0, 1)
        yield (frame * 255 + 0.5).astype("uint8").tobytes()


def write_title(path, W, H, fps, dur, crf=12, **kw):
    """Encodes the title frames to an mp4 (bt709, yuv420p) without touching the disk with frames."""
    cmd = ["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(fps), "-i", "-",
           "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p", "-c:v", "libx264", "-preset", "medium", "-crf", str(crf),
           "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-movflags", "+faststart", str(path)]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for fr in title_frames(W, H, fps, dur, **kw):
        p.stdin.write(fr)
    p.stdin.close()
    if p.wait() != 0:
        raise RuntimeError("ffmpeg failed on the title")


def demo(out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    W, H = 1920, 1080
    caption_center(W, H, "Ogni piano è diverso", "chiese, teatri, sanatori").save(out / "cap_center.png")
    caption_weapon(W, H, "CARDINALE .357", "REVOLVER", 2).save(out / "cap_weapon.png")
    bg = Image.new("RGBA", (W, H), (70, 62, 54, 255))
    for name in ("cap_center", "cap_weapon"):
        Image.alpha_composite(bg, Image.open(out / f"{name}.png")).convert("RGB").save(out / f"{name}_on_bg.jpg", quality=90)
    fps = 30
    pick = {int(t * fps) for t in (0.9, 1.3, 1.7, 2.2, 2.8, 3.4, 4.0, 4.6, 5.3)}
    for i, fr in enumerate(title_frames(W, H, fps, 5.5)):
        if i in pick:
            Image.frombytes("RGB", (W, H), fr).save(out / f"title_{i:03d}.jpg", quality=88)
    print("demo written to", out)


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "demo":
        demo(sys.argv[2])
    else:
        print(__doc__)
