#!/usr/bin/env python3
"""
Звуки интерфейса — ElevenLabs Sound Effects (звук по описанию), единый стиль «голографического» sci-fi интерфейса.

Результат — Assets/Resources/Audio/Sfx/<Sfx>_<N>.ogg: SFXManager берёт записи по имени значения Sfx и чередует
варианты случайно (без повтора подряд). Громкость в игре задаёт SfxLibrary (Volume), сами файлы — с пиком −1 dBFS,
как прежние записи.

В игре сейчас только выбранные на прослушивании звуки (PROMPTS); другие кандидаты можно добавить сюда же.
Неизменённые описания берутся из кеша ~/.cache/astropol_elevenlabs и кредиты не тратят.
Ключ: ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси).
Запуск из корня репозитория:
    python3 Tools/ui_sfx/generate.py              # всё
    python3 Tools/ui_sfx/generate.py UiClick      # один звук
"""
import glob
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

OUT = "Assets/Resources/Audio/Sfx"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")
API = "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_192"
PEAK_DB = -1.0

STYLE = ("premium sci-fi strategy game user interface sound, sleek holographic, clean, polished, subtle, "
         "high quality, dry, no music")
PROMPTS = {
    # Sfx: [(описание, длительность, с)] — каждый пункт даёт вариант
    "UiClick": [
        ("Single short crisp soft digital click, tactile glassy tick", 0.5),
        ("Short soft holographic button press, gentle muted click with tiny airy tail", 0.5),
    ],
    "NotifyInfo": [
        ("Neutral notification ping, soft clean bell-like digital tone with airy tail", 1.0),
        ("Incoming message notification, two gentle soft digital pings", 1.0),
    ],
    "PanelSlide": [
        ("Side panel sliding in, short smooth airy digital swoosh", 0.6),
        ("Quick soft holographic slide whoosh with subtle shimmer", 0.6),
    ],
}

META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.7
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def body_of(text, seconds):
    return {"text": f"{text}, {STYLE}", "duration_seconds": seconds, "prompt_influence": 0.6}


def cache_path(body):
    return os.path.join(CACHE, hashlib.sha1(json.dumps(["sfx", body], sort_keys=True).encode()).hexdigest() + ".mp3")


def generate(text, seconds):
    body = body_of(text, seconds)
    mp3 = cache_path(body)
    if os.path.exists(mp3):
        return mp3
    os.makedirs(CACHE, exist_ok=True)
    req = urllib.request.Request(API, data=json.dumps(body).encode(), method="POST",
                                 headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                                          "Content-Type": "application/json"})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
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


def peak_db(path):
    r = subprocess.run(["ffmpeg", "-i", path, "-af", "volumedetect", "-f", "null", "-"], capture_output=True, text=True)
    return float(r.stderr.split("max_volume:")[1].split("dB")[0])


def master(src, dst):
    """Тишина по краям прочь, пик −1 dBFS, короткий спад в конце, OGG стерео 44,1 кГц."""
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "mid.wav")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-af",
                        "silenceremove=start_periods=1:start_threshold=-50dB,"
                        "areverse,silenceremove=start_periods=1:start_threshold=-55dB,"
                        "afade=t=in:d=0.02,areverse,afade=t=in:d=0.002",
                        "-ac", "2", "-ar", "44100", mid], check=True)
        gain = PEAK_DB - peak_db(mid)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mid, "-af", f"volume={gain:.2f}dB",
                        "-c:a", "libvorbis", "-q:a", "6", dst], check=True)


def main():
    only = set(sys.argv[1:])
    for name, variants in PROMPTS.items():
        if only and name not in only:
            continue
        for old in glob.glob(os.path.join(OUT, name + "_*.ogg")):
            if int(old.rsplit("_", 1)[1].split(".")[0]) >= len(variants):
                os.remove(old)
                if os.path.exists(old + ".meta"):
                    os.remove(old + ".meta")
        for i, (text, seconds) in enumerate(variants):
            dst = os.path.join(OUT, f"{name}_{i}.ogg")
            master(generate(text, seconds), dst)
            if not os.path.exists(dst + ".meta"):
                with open(dst + ".meta", "w") as f:
                    f.write(META.format(guid=uuid.uuid4().hex))
            print("ok", dst)


if __name__ == "__main__":
    main()
