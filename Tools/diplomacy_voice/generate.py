#!/usr/bin/env python3
"""
Озвучка правителей за столом переговоров (DiplomacyModal).

Фразы берутся прямо из Assets/Scripts/Rendering/DiplomacyLines.cs — поправили фразу там, перезапустите скрипт
(неизменённые фразы берутся из кеша и кредиты не тратят). У каждого правителя свой голос ElevenLabs (eleven_v3),
отличный от рассказчика летописи, и свой тег; к нему добавляется настроение реплики (KIND_MOOD).
Обработка — чистый голос «по каналу связи»: де-эссер, компрессия, лёгкое сужение полосы и пространство.
Ударения — словарь STRESS из Tools/chronicle_voice/generate.py.

Результат — Assets/Resources/Audio/Diplomacy/<фракция>_<Kind>_<номер>.ogg (DiplomacyLines.Get запоминает путь,
DiplomacyModal.Say проигрывает).

Запуск из корня репозитория:
    python3 Tools/diplomacy_voice/generate.py                 # всё
    python3 Tools/diplomacy_voice/generate.py xarn nol        # только эти правители
    python3 Tools/diplomacy_voice/generate.py xarn_Accept_0   # одна фраза
"""
import importlib.util
import os
import re
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import elevenlabs_tts  # noqa: E402

_spec = importlib.util.spec_from_file_location("chronicle", os.path.join(HERE, "..", "chronicle_voice", "generate.py"))
chronicle = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(chronicle)

SRC = "Assets/Scripts/Rendering/DiplomacyLines.cs"
OUT = "Assets/Resources/Audio/Diplomacy"

COMM = ("highpass=f=95,deesser=i=0.35,equalizer=f=250:t=q:w=1.2:g=1,equalizer=f=2800:t=q:w=1.3:g=2,"
        "lowpass=f=11500,acompressor=threshold=-20dB:ratio=2.8:attack=5:release=130:makeup=1.5")

RULERS = {
    # массив в DiplomacyLines.cs: (ключ, голос, характер для eleven_v3, цепочка ffmpeg)
    "Xarn":    ("xarn", "roderich", "gruff, rough, menacing",
                f"{COMM},equalizer=f=120:t=q:w=1:g=2,aecho=0.8:0.45:35|70:0.10|0.05"),
    "Astrea":  ("astrea", "marina", "warm, diplomatic", f"{COMM},aecho=0.8:0.4:30|60:0.07|0.04"),
    "Aquila":  ("aquila", "pavel", "charming, persuasive, sly", f"{COMM},aecho=0.8:0.4:25|50:0.06|0.03"),
    "Iridia":  ("iridia", "moriana", "mysterious, knowing",
                f"{COMM},chorus=0.85:0.9:24|33:0.07|0.05:0.25|0.2:1.2|0.9,aecho=0.85:0.55:60|130:0.12|0.07"),
    "Terraan": ("terraan", "marina_soul", "ancient, calm, slow",
                f"{COMM},aecho=0.85:0.55:70|150|260:0.11|0.07|0.04"),
    "Nol":     ("nol", "lexa", "cold, emotionless, precise",
                f"{COMM},chorus=0.6:0.9:9|14:0.20|0.16:0.6|0.5:2.1|1.7,aecho=0.8:0.45:18|36:0.12|0.06"),
}

KIND_MOOD = {
    "GreetFriendly": "friendly", "GreetNeutral": "businesslike", "GreetHostile": "cold, hostile",
    "AtWar": "threatening", "Accept": "satisfied", "Reject": "dismissive", "Gift": "pleased",
    "Counter": "firm", "NoDeal": "dismissive", "OfferTrade": "businesslike", "OfferTreaty": "diplomatic",
    "Demand": "threatening", "OfferPeace": "weary, serious", "OfferPact": "measured", "FirstContact": "intrigued",
}

LOC = re.compile(r'Loc\.T\("((?:[^"\\]|\\.)*)"\)')


def parse_lines():
    """{массив: [(Kind, [фразы])]} из DiplomacyLines.cs."""
    src = open(SRC, encoding="utf-8").read()
    data = {}
    for name in RULERS:
        a = src.index(f"string[][] {name} =")
        block = src[a:src.index("};", a)]
        data[name] = [(m.group(1), [s.replace('\\"', '"') for s in LOC.findall(m.group(2))])
                      for m in re.finditer(r"/\*\s*(\w+)\s*\*/\s*new\[\]\s*\{(.*)", block)]
    return data


def voice_text(s):
    s = s.replace("«", "").replace("»", "")
    for k, v in sorted(chronicle.STRESS.items(), key=lambda kv: -len(kv[0])):
        s = re.sub(r"(?<![\w+'])" + re.escape(k) + r"(?![\w'])", v, s)
    return re.sub(r"\s+", " ", s).strip()


def main():
    keys = set(sys.argv[1:])
    os.makedirs(OUT, exist_ok=True)
    bad = []
    with tempfile.TemporaryDirectory() as tmp:
        for name, kinds in parse_lines().items():
            key, voice, character, chain = RULERS[name]
            for kind, lines in kinds:
                mood = character if key == "nol" else f"{character}, {KIND_MOOD.get(kind, '')}".strip(", ")
                for i, line in enumerate(lines):
                    fid = f"{key}_{kind}_{i}"
                    if keys and key not in keys and fid not in keys:
                        continue
                    raw = os.path.join(tmp, "raw.wav")
                    elevenlabs_tts.synth(voice_text(line), voice, raw, direction=mood, log=bad)
                    out = os.path.join(OUT, fid + ".ogg")
                    chronicle.render(raw, chain, out)
                    print(f"{out}  ←  {line}")
    if bad:
        print("\nНеточные дубли (лучший из попыток) — проверьте на слух:\n" + "\n".join(bad))


if __name__ == "__main__":
    main()
