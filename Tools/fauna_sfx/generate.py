#!/usr/bin/env python3
"""
Голоса космической фауны — ElevenLabs Sound Effects (звук по текстовому описанию).

Результат — Assets/Resources/Audio/Sfx/Fauna<Вид>_<N>.ogg: SFXManager подхватывает записи по имени значения Sfx
(FaunaRays, FaunaJelly, FaunaSwarm, FaunaCrystal, FaunaWanderer) и выбирает вариант случайно, без повтора подряд.
Звучат при обнаружении вида, при наведении курсора, редкими голосами рядом с камерой (FaunaView.AmbientCall),
при появлении Странника и когда Рой Искр жжёт корабли.

Описания — PROMPTS ниже: поправили, перезапустите (неизменённые берутся из кеша ~/.cache/astropol_elevenlabs и
кредиты не тратят). Ключ: ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси).
Запуск из корня репозитория:
    python3 Tools/fauna_sfx/generate.py              # всё
    python3 Tools/fauna_sfx/generate.py FaunaRays    # один вид
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
TARGET_LUFS = -18

SCIFI = "cinematic sci-fi creature sound, clean, no music, no voice"
PROMPTS = {
    # Вид: [(описание, длительность, с)] — каждый пункт даёт вариант
    "FaunaRays": [
        ("Majestic space manta rays gliding past, deep whale-like song with warm shimmering solar harmonics, ethereal", 6),
        ("Herd of giant solar rays calling to each other, soft melodic whale song, warm glowing harmonic overtones", 5),
        ("Single cosmic manta ray call, long gentle rising whale moan with shimmering golden resonance", 5),
    ],
    "FaunaJelly": [
        ("Giant alien void jellyfish pulsing in space, soft bioluminescent hum, wet gelatinous throbbing, glassy chime tones, eerie and calm", 6),
        ("Translucent space jellyfish drifting, slow breathing pulse, soft wet bubbling, distant glass harmonica tones", 5),
        ("Alien jellyfish swarm glowing, gentle electric hum and soft squelching pulses, dreamy and eerie", 5),
    ],
    "FaunaSwarm": [
        ("Swarm of thousands of tiny electric insect-like creatures, crackling sparks, buzzing and chittering, swarm swelling closer and fading", 6),
        ("Electric spark swarm attacking, aggressive crackling and fizzing, insect buzzing, sharp static zaps", 4),
        ("Cloud of glowing spark creatures circling, chittering electric buzz, popping sparks, rising and falling", 5),
    ],
    "FaunaCrystal": [
        ("Crystalline alien creatures resonating, singing glass harmonics, crystal shards growing and cracking, ringing tonal chimes", 6),
        ("Living crystal lattice answering a pulsar beam, rhythmic glassy pings and resonant ringing echoes", 5),
        ("Crystal creatures chiming, bowed glass tones, delicate cracking and tinkling shards, cold and beautiful", 5),
    ],
    "FaunaWanderer": [
        ("Colossal ancient space leviathan passing by, ultra deep rumbling roar and distant whale-like moan, enormous and awe-inspiring", 8),
        ("Gigantic cosmic whale call echoing across space, infrasonic rumble, slow mournful low moan, huge scale", 8),
        ("Titanic space creature breathing and groaning, deep resonant rumble, distant thunderous call, ominous", 7),
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


def generate(text, seconds):
    body = {"text": f"{text}, {SCIFI}", "duration_seconds": seconds, "prompt_influence": 0.45}
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


def master(src, dst):
    """Тишина по краям прочь, громкость к TARGET_LUFS, мягкие края, OGG стерео 44,1 кГц."""
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "mid.wav")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-af",
                        "silenceremove=start_periods=1:start_threshold=-55dB,"
                        "areverse,silenceremove=start_periods=1:start_threshold=-60dB,areverse,"
                        f"loudnorm=I={TARGET_LUFS}:TP=-1.5:LRA=11,aresample=44100",
                        "-ac", "2", mid], check=True)
        dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", mid],
                                   capture_output=True, text=True).stdout or 1)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mid, "-af",
                        f"afade=t=in:d=0.02,afade=t=out:st={max(0.0, dur - 0.5):.2f}:d=0.5",
                        "-c:a", "libvorbis", "-q:a", "5", dst], check=True)


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
