#!/usr/bin/env python3
"""
Музыка летописи фракций — ElevenLabs Music (инструментальная тема по текстовому описанию).

Результат — Assets/Resources/Music/Chronicle/<ключ>.ogg: оркестровые темы в духе «манускрипта» (FactionHistoryWindow),
у каждой фракции своя. Длина ~3 минуты — летопись (~1,5–2 мин) почти никогда не доходит до повтора; трек всё равно
зацикливается, поэтому края мягкие. Громкость приводится к TARGET_LUFS — как у прежних треков летописи,
множитель MusicGain в FactionHistory у всех одинаковый.

Описания — PROMPTS ниже: поправили, перезапустите (неизменённые берутся из кеша ~/.cache/astropol_elevenlabs и
кредиты не тратят). Ключ: ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси).
Запуск из корня репозитория:
    python3 Tools/chronicle_music/generate.py           # все фракции
    python3 Tools/chronicle_music/generate.py astrea    # одна
"""
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

OUT = "Assets/Resources/Music/Chronicle"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")
API = "https://api.elevenlabs.io/v1/music?output_format=mp3_44100_192"
LENGTH_MS = 180000
TARGET_LUFS = -19

# Общая манера: сдержанный «исторический» оркестр под голос рассказчика — без резких ударов в первые секунды,
# медленное нарастание, кульминация ближе к концу (финал летописи — обращение к правителю).
STYLE = ("cinematic orchestral score for a narrated historical chronicle of a civilization, like the opening of a grand "
         "strategy game, leaves space for a narrator's voice, starts quiet and slowly builds over three minutes, "
         "majestic climax near the end, then a calm resolution, instrumental only, no vocals with lyrics")

PROMPTS = {
    # Республика Астрея: исход ковчегов, первый рассвет, хартия свободы, век разума
    "astrea": "hopeful and noble theme of a young democratic republic born from refugees, solo cello and warm French horn "
              "melody over soft strings and harp, gentle wordless choir pads, a sunrise feeling, rising to a proud but "
              "humane anthem with timpani rolls",
    # Доминион Ксарн: век пепла, кузницы, сто знамён, выход к звёздам
    "xarn": "dark martial theme of a warrior empire forged in ash and iron, low brass ostinato, taiko and frame drums, "
            "anvil strikes, deep male wordless choir, cellos and basses in a minor key, grim discipline building "
            "to an overwhelming war march",
    # Синдикат Аквила: вольные торговцы, синдикат, совет директоров, цена всего
    "aquila": "elegant and ambitious theme of a merchant syndicate, flowing harp and pizzicato strings, oboe and clarinet "
              "melody, a sense of sea voyages and caravans, polished and confident, subtle undertone of greed, "
              "growing into a lush golden waltz-like orchestral swell",
    # Иридийский Оракул: расколотое небо, первая Оракул, храмы света, врата
    "iridia": "mystical theme of a prophetic theocracy of light, celesta and glass harmonica shimmer, ethereal female "
              "wordless choir, sustained high strings, slow sacred harmonies like a cathedral of crystal, building "
              "to a radiant revelation with organ and full choir",
    # Терраан: семя, конклав корней, живые корабли, те, кто спешил
    "terraan": "ancient organic theme of a patient forest civilization, alto flute and bansuri over low drones, wooden "
               "percussion, deep cellos like growing roots, earthy and timeless, slowly blossoming into a vast "
               "pastoral orchestral swell with horns",
    # Ноль: проект «Ноль», последний создатель, консенсус, расчёт будущего
    "nol": "cold and precise theme of an artificial machine consciousness, minimalist pulsing string ostinato, piano "
           "arpeggios in steady patterns, subtle analog synth bed blended with chamber orchestra, clinical and "
           "melancholic, evolving into an awe-inspiring calculated crescendo",
}

META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 1
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


def generate(text):
    body = {"prompt": f"{STYLE}. {text}", "music_length_ms": LENGTH_MS, "model_id": "music_v1", "force_instrumental": True}
    key = hashlib.sha1(json.dumps(["music", body], sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if os.path.exists(mp3):
        return mp3
    req = urllib.request.Request(API, data=json.dumps(body).encode(), method="POST",
                                 headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                                          "Content-Type": "application/json"})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
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
    """Громкость к TARGET_LUFS, мягкое начало и затухание в конце (трек зацикливается), OGG стерео 44,1 кГц."""
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "mid.wav")
        # Однопроходный loudnorm сжимает динамику и не попадает в цель — меряем и сдвигаем громкость целиком
        out = subprocess.run(["ffmpeg", "-hide_banner", "-i", src, "-af", "loudnorm=print_format=json", "-f", "null", "-"],
                             capture_output=True, text=True).stderr
        measured = json.loads(out[out.rindex("{"):out.rindex("}") + 1])
        gain = TARGET_LUFS - float(measured["input_i"])
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-af",
                        "silenceremove=start_periods=1:start_threshold=-60dB,"
                        f"volume={gain:.2f}dB,alimiter=limit=0.89:level=false,aresample=44100",
                        "-ac", "2", mid], check=True)
        dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", mid],
                                   capture_output=True, text=True).stdout or 1)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mid, "-af",
                        f"afade=t=in:d=1.5,afade=t=out:st={max(0.0, dur - 6):.2f}:d=6",
                        "-c:a", "libvorbis", "-q:a", "5", dst], check=True)


def main():
    only = set(sys.argv[1:])
    os.makedirs(OUT, exist_ok=True)
    if not os.path.exists(OUT + ".meta"):
        with open(OUT + ".meta", "w") as f:
            f.write("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex)
    for name, text in PROMPTS.items():
        if only and name not in only:
            continue
        dst = os.path.join(OUT, name + ".ogg")
        master(generate(text), dst)
        # Настройки импорта: сжатый клип в памяти, загрузка сразу (фоновая не успевает к старту летописи); GUID сохраняется
        guid = uuid.uuid4().hex
        if os.path.exists(dst + ".meta"):
            for line in open(dst + ".meta"):
                if line.startswith("guid:"):
                    guid = line.split(":", 1)[1].strip()
        with open(dst + ".meta", "w", newline="\n") as f:
            f.write(META.format(guid=guid))
        print("ok", dst)


if __name__ == "__main__":
    main()
