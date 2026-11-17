"""
Takes the absolute path of the machine out of a binary FBX.

Blender writes the path of the .blend it exported from into a property string of every FBX (C:\\Users\\<name>\\...\\work\\<id>_hp.blend).
The repository is public and the models are committed, so export.py runs this on every FBX it writes. The strings of a binary FBX are
length-prefixed, so the path is replaced by a relative one of exactly the same length (padded with spaces): nothing else moves.

Also usable by hand:  python scrub_fbx.py path/to/model.fbx [more.fbx ...]   (prints how many paths it replaced in each file)
"""
import re
import sys

PATH = re.compile(rb'[A-Za-z]:[\\/](?:[^\\/\x00\r\n"<>|*?:]+[\\/])*[^\\/\x00\r\n"<>|*?:]+\.blend')
RELATIVE = b'modelli/personaggi/work/'


def scrub(path):
    """Replaces every absolute .blend path in the file; returns how many it replaced."""
    with open(path, 'rb') as f:
        data = f.read()

    def repl(m):
        old = m.group(0)
        name = re.split(rb'[\\/]', old)[-1]
        new = RELATIVE + name
        if len(new) > len(old):
            new = name[:len(old)]
        return new.ljust(len(old), b' ')

    out, n = PATH.subn(repl, data)
    if n:
        with open(path, 'wb') as f:
            f.write(out)
    return n


if __name__ == '__main__':
    for p in sys.argv[1:]:
        print(p, scrub(p))
