"""Foglio di contatto delle foto fatte in gioco o in editor: tutte le immagini <arma>_<vista>.png di una cartella, ritagliate al centro
e rimpicciolite, con il nome sotto.
Uso:  python sheet_game.py <cartella> <vista> [colonne] [id ...]   ->  <cartella>/foglio_<vista>.jpg
      cartella: hotel_build/armi (ArmiShots) oppure hotel_build/armi_gioco (-guntest); vista: mano, mira, lato, sparo, ricarica"""
import os, sys, glob
from PIL import Image, ImageDraw

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
folder = sys.argv[1] if os.path.isabs(sys.argv[1]) else os.path.join(root, sys.argv[1])
view = sys.argv[2]
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 4
ids = sys.argv[4:]
files = sorted(f for f in glob.glob(os.path.join(folder, f'*_{view}.png')) if not os.path.basename(f).startswith('foglio_'))
if ids:
    files = [f for f in files if os.path.basename(f)[:-len(view) - 5] in ids]
W, H = 560, 315
rows = (len(files) + cols - 1) // cols
sheet = Image.new('RGB', (cols * W, rows * (H + 18)), (10, 10, 12))
d = ImageDraw.Draw(sheet)
for i, f in enumerate(files):
    im = Image.open(f).convert('RGB').resize((W, H), Image.LANCZOS)
    x, y = (i % cols) * W, (i // cols) * (H + 18)
    sheet.paste(im, (x, y))
    d.text((x + 6, y + H + 2), os.path.basename(f)[:-len(view) - 5], fill=(215, 215, 205))
out = os.path.join(folder, f'foglio_{view}.jpg')
sheet.save(out, quality=86)
print(out, sheet.size, len(files))
