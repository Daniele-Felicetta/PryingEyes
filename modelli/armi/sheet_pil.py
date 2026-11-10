"""Foglio di contatto: una vista per arma (render/<nome>_<vista>.png) in una griglia, con il nome sotto.
Uso:  python sheet_pil.py <vista> <nome ...>   ->  render/foglio_<vista>.png   (vista: lato, 34, giocatore, ads)"""
import os, sys
from PIL import Image, ImageDraw

DIR = os.path.dirname(os.path.abspath(__file__))
view = sys.argv[1]
names = sys.argv[2:]
cols = 2 if len(names) <= 4 else 3
W, H = 760, 430
rows = (len(names) + cols - 1) // cols
sheet = Image.new('RGB', (cols * W, rows * H), (12, 12, 14))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    p = os.path.join(DIR, 'render', f'{n}_{view}.png')
    if not os.path.exists(p):
        continue
    im = Image.open(p).convert('RGB')
    im.thumbnail((W, H - 22))
    x, y = (i % cols) * W, (i // cols) * H
    sheet.paste(im, (x + (W - im.width) // 2, y))
    d.text((x + 10, y + H - 20), n, fill=(210, 210, 200))
out = os.path.join(DIR, 'render', f'foglio_{view}.png')
sheet.save(out)
print(out, sheet.size)
