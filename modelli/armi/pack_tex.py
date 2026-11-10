"""
Impacchetta le mappe grezze del bake (tex/raw/<arma>_{base,rough,metal,normal,ao}.png) nel formato di Unity URP/Lit:
  <arma>_base.jpg    colore (sRGB)
  <arma>_mask.png    R metallicita', G occlusione, B 0, A levigatezza (= 1 - ruvidezza)  -> _MetallicGlossMap e _OcclusionMap
  <arma>_normal.jpg  normali tangenti (verde in alto)
Uso (python di sistema, ha numpy e PIL):  python pack_tex.py [nome ...]   (senza nomi: tutti quelli in tex/raw)
"""
import os, sys
import numpy as np
from PIL import Image, ImageFilter

DIR = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(DIR, 'tex', 'raw')
OUT = os.path.join(DIR, 'tex')


def load(nm, key):
    return np.asarray(Image.open(os.path.join(RAW, f'{nm}_{key}.png')).convert('RGB'), dtype=np.float32) / 255.0


def blur(a, r):
    return np.asarray(Image.fromarray(a).filter(ImageFilter.GaussianBlur(r)))


def pack(nm):
    base = load(nm, 'base')
    rough = load(nm, 'rough')[..., 0]
    metal = load(nm, 'metal')[..., 0]
    ao = load(nm, 'ao')[..., 0]
    normal = load(nm, 'normal')
    ao = np.clip(ao, 0, 1) ** 0.8
    smooth = 1.0 - np.clip(rough, 0, 1)
    q = lambda a: np.clip(np.round(a * 255.0), 0, 255).astype(np.uint8)
    # la maschera: leggermente sfocata (rumore del bake) e a 5 bit, cosi' il PNG pesa un quarto
    mask = np.zeros(rough.shape + (4,), np.uint8)
    mask[..., 0] = blur(q(metal), 0.6)
    mask[..., 1] = blur(q(ao), 1.0)
    mask[..., 3] = blur(q(smooth), 0.8)
    mask = (mask // 8 * 8).astype(np.uint8)
    mask[..., 2] = 0
    Image.fromarray(q(base)).save(os.path.join(OUT, f'{nm}_base.jpg'), quality=93, subsampling=0)
    Image.fromarray(mask).save(os.path.join(OUT, f'{nm}_mask.png'), optimize=True)
    Image.fromarray(q(normal)).save(os.path.join(OUT, f'{nm}_normal.jpg'), quality=93, subsampling=0)
    sizes = [os.path.getsize(os.path.join(OUT, f'{nm}_{k}')) / 1e6 for k in ('base.jpg', 'mask.png', 'normal.jpg')]
    print(f'[pack] {nm}: base {sizes[0]:.1f} MB, mask {sizes[1]:.1f} MB, normal {sizes[2]:.1f} MB')


if __name__ == '__main__':
    names = sys.argv[1:] or sorted({f.rsplit('_', 1)[0] for f in os.listdir(RAW) if f.endswith('_base.png')})
    for n in names:
        pack(n)
