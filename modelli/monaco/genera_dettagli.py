"""Texture di dettaglio per il monaco, generate da zero con NumPy e Pillow.

Uso: python -I modelli/monaco/genera_dettagli.py <cartella texture Unity>
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "hotel"))
from genera_texture import F, fbm, normals, perlin, sample, smooth, warped


def write(out, name, data):
    Image.fromarray((np.clip(data, 0, 1) * 255 + 0.5).astype(np.uint8)).save(out / (name + ".png"))


def linen(out):
    n = 512
    rng = np.random.default_rng(248)
    y, x = np.mgrid[0:n, 0:n].astype(F) / n
    warp = x * 32 + 0.18 * perlin(n, 13, rng, cells_y=4)
    weft = y * 32 + 0.21 * perlin(n, 5, rng, cells_y=15)
    crossing = (np.floor(warp).astype(int) + np.floor(weft).astype(int)) % 2 == 0
    thread_x = np.sin(np.pi * (warp % 1)) ** 0.88
    thread_y = np.sin(np.pi * (weft % 1)) ** 0.94
    weave = np.where(crossing, thread_x * (0.66 + 0.34 * thread_y), thread_y * (0.66 + 0.34 * thread_x))
    slub = fbm(n, 20, 4, rng) - 0.5
    wear = warped(n, rng, 3, 5, 0.26) - 0.5
    lint = smooth(0.52, 0.76, perlin(n, 103, rng, cells_y=67) * 0.5 + 0.5)
    albedo = 0.5 + (weave - 0.61) * 0.10 + slub * 0.22 + wear * 0.49 + lint * 0.09
    height = weave * 0.075 + slub * 0.14 + lint * 0.032
    write(out, "monaco_tela_detail", np.repeat(albedo[..., None], 3, -1))
    write(out, "monaco_tela_detail_normal", normals(height, 4.2))


def iris(out):
    # Stessa proiezione frontale e isola posteriore di make_bake_uv(..., 'eye').
    n = 2048
    rng = np.random.default_rng(249)
    y, x = (np.mgrid[0:n, 0:n].astype(F) + 0.5) / n
    px, py = (x - 0.5) / 0.48, (0.5 - y) / 0.48
    r = np.hypot(px, py)
    angle = np.arctan2(py, px)
    angular = angle / (2 * np.pi) + 0.5
    texture = fbm(n, 12, 5, rng)
    radial_warp = sample(texture, angular * n, r * n * 0.40) - 0.5
    fiber = sample(fbm(n, 38, 5, rng), angular * n + radial_warp * 16, r * n * 0.22)
    fine = (np.cos(angle * 263 + radial_warp * 18 + r * 18) * 0.5 + 0.5) ** 4
    scallop = (np.cos(angle * 49 + radial_warp * 9) * 0.5 + 0.5) * 0.029
    collar = np.exp(-((r - 0.37 - scallop) / 0.058) ** 2)
    amber = np.array((0.59, 0.40, 0.20), F)
    blue = np.array((0.43, 0.43, 0.36), F)
    hue = blue + (amber - blue) * collar[..., None]
    limb = smooth(0.595, 0.664, r)
    iris_rgb = hue * (0.55 + 0.75 * fiber + fine * 0.13)[..., None]
    iris_rgb *= (1 - limb * 0.75)[..., None]
    pink = smooth(0.72, 1.0, r)
    sclera = np.array((0.48, 0.47, 0.42), F) + np.array((0.02, -0.17, -0.16), F) * pink[..., None]
    veins = smooth(0.92, 0.988, 1 - np.abs(perlin(n, 27, rng))) * pink * smooth(0.42, 0.67, texture)
    sclera *= (0.76 + texture * 0.33 - veins * 0.20)[..., None]
    ring = (1 - smooth(0.654, 0.686, r))[..., None]
    rgb = sclera * (1 - ring) + iris_rgb * ring
    pupil = smooth(0.269 + radial_warp * 0.012, 0.294 + radial_warp * 0.014, r)[..., None]
    rgb = rgb * pupil + 0.015 * (1 - pupil)
    back = np.hypot(x - 0.94, y - 0.06) < 0.059
    rgb[back] = (0.34, 0.18, 0.15)
    write(out, "monaco_iride_base", rgb)


if __name__ == "__main__":
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    linen(out)
    iris(out)
