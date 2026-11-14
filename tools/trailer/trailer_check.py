#!/usr/bin/env python3
"""Checks on a finished trailer file (no ears needed): a contact sheet of the cuts, and the numbers of the soundtrack.

    python tools/trailer/trailer_check.py sheet VIDEO OUT_DIR [--tile 400] [--per 7]     # 3 frames per segment of make_trailer.SEGMENTS
    python tools/trailer/trailer_check.py audio VIDEO OUT_DIR                              # loudness, peaks, per-second level, spectrogram
    python tools/trailer/trailer_check.py info VIDEO                                       # streams, duration, size
"""
import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))


def ffprobe(p):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(p)], capture_output=True, text=True, check=True)
    return json.loads(r.stdout)


def info(video):
    j = ffprobe(video)
    for s in j["streams"]:
        if s["codec_type"] == "video":
            print(f"video {s['codec_name']} {s['width']}x{s['height']} {s['r_frame_rate']} {s.get('pix_fmt')} {s.get('color_space')} frames={s.get('nb_frames')}")
        else:
            print(f"audio {s['codec_name']} {s.get('sample_rate')} Hz ch={s.get('channels')} {s.get('bit_rate')} bps")
    f = j["format"]
    print(f"duration {float(f['duration']):.3f} s   size {int(f['size']) / 1e6:.1f} MB   {int(f['bit_rate']) / 1e6:.2f} Mb/s")


def sheet(video, out, tile=400, per=7):
    from PIL import Image, ImageDraw, ImageFont
    import make_trailer as MT
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    pos, total = MT.layout(MT.SEGMENTS)
    th = tile * 9 // 16
    font = ImageFont.truetype("arial.ttf", 15)
    segs = MT.SEGMENTS
    with tempfile.TemporaryDirectory() as tmp:
        for n in range(0, len(segs), per):
            chunk = segs[n:n + per]
            img = Image.new("RGB", (3 * tile, len(chunk) * th), (12, 12, 14))
            d = ImageDraw.Draw(img)
            for r, s in enumerate(chunk):
                t0 = pos[s.clip]
                for c, t in enumerate((t0 + 0.2, t0 + s.dur / 2, t0 + s.dur - 0.25)):
                    t = min(t, total - 0.05)
                    dst = Path(tmp) / f"{s.clip}_{c}.jpg"
                    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t:.3f}", "-i", str(video), "-frames:v", "1",
                                    "-vf", f"scale={tile}:{th}", str(dst)], check=True)
                    img.paste(Image.open(dst).convert("RGB"), (c * tile, r * th))
                    d.text((c * tile + 4, r * th + th - 18), f"{t:.1f}s", font=font, fill=(255, 220, 120))
                d.rectangle([0, r * th, 8 * len(s.clip) + 12, r * th + 18], fill=(0, 0, 0))
                d.text((4, r * th + 1), s.clip, font=font, fill=(255, 220, 120))
            p = out / f"cuts_{n // per + 1}.jpg"
            img.save(p, quality=86)
            print(p)


def audio(video, out):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import soundfile as sf
    from scipy import signal
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    wav = out / "check_audio.wav"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(video), "-vn", "-ac", "2", "-ar", "48000", str(wav)], check=True)
    y, sr = sf.read(str(wav), always_2d=True)
    print(f"audio {len(y) / sr:.2f} s  peak {20 * np.log10(np.max(np.abs(y)) + 1e-9):.2f} dBFS  DC {np.mean(y):+.5f}")
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(wav), "-af", "ebur128=peak=true", "-f", "null", "-"], capture_output=True, text=True)
    tail = r.stderr.split("Summary:")[-1]
    for line in tail.splitlines():
        line = line.strip()
        if line.startswith(("I:", "LRA:", "Peak:")):
            print("  ", line)
    mono = y.mean(axis=1)
    n = int(sr)
    rms = [20 * np.log10(np.sqrt(np.mean(mono[i:i + n] ** 2)) + 1e-9) for i in range(0, len(mono) - n + 1, n)]
    print("RMS dBFS per second:", " ".join(f"{v:.0f}" for v in rms))
    clip = int(np.sum(np.abs(y) > 0.985))
    print("samples above -0.13 dBFS:", clip)
    f, t, S = signal.spectrogram(mono, sr, nperseg=4096, noverlap=3072)
    fig, ax = plt.subplots(2, 1, figsize=(14, 7), gridspec_kw={"height_ratios": [3, 1]})
    ax[0].pcolormesh(t, f, 10 * np.log10(S + 1e-12), shading="auto", vmin=-110, vmax=-30, cmap="magma")
    ax[0].set_ylim(30, 12000)
    ax[0].set_yscale("log")
    ax[0].set_ylabel("Hz")
    tt = np.arange(len(mono)) / sr
    ax[1].plot(tt[::64], mono[::64], lw=0.4)
    ax[1].set_xlim(0, len(mono) / sr)
    ax[1].set_xlabel("s")
    fig.tight_layout()
    p = out / "audio_check.png"
    fig.savefig(p, dpi=80)
    print(p)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["sheet", "audio", "info"])
    ap.add_argument("video")
    ap.add_argument("out", nargs="?", default="unity_logs/trailer/work/check")
    ap.add_argument("--tile", type=int, default=400)
    ap.add_argument("--per", type=int, default=7)
    a = ap.parse_args()
    if a.cmd == "info":
        info(a.video)
    elif a.cmd == "sheet":
        sheet(a.video, a.out, a.tile, a.per)
    else:
        audio(a.video, a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
