#!/usr/bin/env python3
"""
Звуки вступления (IntroWarnings): ElevenLabs Sound Effects → Assets/Resources/Audio/Intro/*.ogg.

  warn_bed    — тихий тёмный эмбиент под экранами предупреждений (зацикливается);
  warn_swell  — мягкое нарастание с глубоким ударом: появляется очередной экран;
  warn_tick   — едва слышный стеклянный тон: экран пропущен клавишей.

Ключ: ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси).
Кеш — ~/.cache/astropol_elevenlabs, повторный запуск кредиты не тратит.
Запуск из корня репозитория:  python3 Tools/intro_sfx/generate.py
"""
import hashlib
import json
import os
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

OUT = "Assets/Resources/Audio/Intro"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")
API = "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_192"

# имя: (описание, длительность, громкость LUFS, затухание в конце, с)
SOUNDS = {
    "warn_bed": ("dark cinematic ambient drone for a game intro, deep soft sub bass pad, faint distant airy shimmer, "
                 "very slow and calm, no melody, no percussion, seamless loop", 22, -28, 0.0),
    "warn_swell": ("soft cinematic reverse swell rising into a deep gentle sub boom, warm long shimmering reverb tail, "
                   "elegant AAA game title card sound", 5, -20, 1.5),
    "warn_tick": ("very soft elegant glassy UI tone, single gentle chime with short airy reverb, minimal", 1.5, -26, 0.4),
}


def generate(text, seconds):
    body = {"text": text, "duration_seconds": seconds, "prompt_influence": 0.5}
    key = hashlib.sha1(json.dumps(["sfx", body], sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if os.path.exists(mp3):
        return mp3
    req = urllib.request.Request(API, data=json.dumps(body).encode(), method="POST",
                                 headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                                          "Content-Type": "application/json"})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=180) as r:
                data = r.read()
            break
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < 4:
                time.sleep(2 ** (attempt + 1))
                continue
            raise RuntimeError(f"ElevenLabs {e.code}: {e.read().decode(errors='replace')[:300]}") from None
    with open(mp3 + ".part", "wb") as f:
        f.write(data)
    os.replace(mp3 + ".part", mp3)
    return mp3


def master(src, dst, lufs, tail):
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "mid.wav")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-af",
                        "silenceremove=start_periods=1:start_threshold=-60dB,"
                        f"loudnorm=I={lufs}:TP=-2:LRA=11,aresample=44100", "-ac", "2", mid], check=True)
        dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", mid],
                                   capture_output=True, text=True).stdout or 1)
        fades = "afade=t=in:d=0.01"
        if tail > 0:
            fades += f",afade=t=out:st={max(0.0, dur - tail):.2f}:d={tail}"
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mid, "-af", fades,
                        "-c:a", "libvorbis", "-q:a", "5", dst], check=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, (text, seconds, lufs, tail) in SOUNDS.items():
        dst = os.path.join(OUT, name + ".ogg")
        master(generate(text, seconds), dst, lufs, tail)
        print("ok", dst)


if __name__ == "__main__":
    main()
