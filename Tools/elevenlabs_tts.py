#!/usr/bin/env python3
"""
Общий синтез речи через ElevenLabs для генераторов озвучки (Tools/*_voice).

Модель по умолчанию — eleven_v3: выразительная речь и режиссёрские теги в квадратных скобках
([grim], [whispers], [warm, hopeful]…), которые модель отыгрывает, но не произносит.
Каждый дубль проверяется распознаванием речи (Scribe): если текст прочитан неточно (проглоченные
или искажённые слова, произнесённый тег), фраза перегенерируется, до TAKES попыток; берётся лучший дубль.

Ударения: «+» перед ударной гласной (как в словарях STRESS генераторов) превращается в знак ударения U+0301.

Ключ: переменная ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси).
Дубли кешируются в ~/.cache/astropol_elevenlabs по хешу запроса — повторный запуск не тратит кредиты.
"""
import difflib
import hashlib
import json
import os
import re
import subprocess
import time
import urllib.error
import urllib.request
import uuid

BASE = "https://api.elevenlabs.io/v1"
MODEL = "eleven_v3"
FORMAT = "mp3_44100_192"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")
TAKES = 5          # максимум дублей на фразу
ACCEPT = 0.96      # доля совпавших слов, при которой дубль принимается сразу

# Голоса. Русские — носители языка из библиотеки ElevenLabs (доступны на платном тарифе);
# английские стандартные оставлены для совместимости.
VOICES = {
    "victoria": "Phc1KhVrCoW3CD0K7bUS",   # тёплый женский, рассказчица
    "nester": "pM78bgjPVk0JXtaEnFoj",     # низкий, с хрипотцой, властный
    "egor": "6A9D8WSMm4rFsg2DWFeE",       # чёткий, командный, «игровой»
    "elen": "TPIitICAZ8CqlGZ81AKm",       # обволакивающий, «золотой и опасный»
    "tatyana": "nkvv6waLzFah2jTNUZGT",    # драматичный, богатый женский
    "gleb": "aJC9di73GYKqRpVCR663",       # глубокий, тёмный, кинематографичный
    "aleksandr": "JMT0QtVym2S8gHCkBqMp",  # документальный диктор
    "lexa": "7VsVlkgkPKhWAimu09u0",       # ровный, бесстрастный женский
    # Правители (дипломатия)
    "evelon": "mOTbMAOniC3yoEvgo4bi",     # угрожающий, энергичный
    "marina": "CsT9glYAgw8L02hu9od2",     # тёплый, выразительный женский
    "pavel": "VlCYpQzyafIQIhmvxR2w",      # обаятельный баритон
    "moriana": "ahuwlnygfGOZ2gufNQh7",    # тёмный, бархатный женский
    "marina_soul": "7CPD49d3y4krj7MKJYOV",  # глубокий, медитативный женский
    "sarah": "EXAVITQu4vr4xnSDxMaL",
    "george": "JBFqnCBsd6RMkjVDRZzb",
    "eric": "cjVigY5qzO86Huf0OWal",
}

ACUTE = "́"


def accent(text):
    """«+а» → «а́» (знак ударения после гласной)."""
    return re.sub(r"\+(\w)", lambda m: m.group(1) + ACUTE, text)


def _words(text):
    text = re.sub(r"\[[^\]]*\]", " ", text).replace(ACUTE, "").replace("+", "").lower().replace("ё", "е").replace("э", "е").replace("'", "").replace("-", "")
    return re.findall(r"[a-zа-я0-9]+", text)


def score(expected, heard):
    """Доля совпадения слов ожидаемого текста и распознанного (0..1)."""
    return difflib.SequenceMatcher(None, _words(expected), _words(heard)).ratio()


def _request(path, data, headers, timeout=180):
    req = urllib.request.Request(BASE + path, data=data, method="POST",
                                 headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                                          **headers})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < 4:
                time.sleep(2 ** (attempt + 1))
                continue
            raise RuntimeError(f"ElevenLabs {e.code}: {e.read().decode(errors='replace')[:300]}") from None
        except (urllib.error.URLError, TimeoutError):
            if attempt < 4:
                time.sleep(2 ** (attempt + 1))
                continue
            raise


def transcribe(path):
    """Распознаёт русскую речь в файле (Scribe), возвращает текст."""
    b = uuid.uuid4().hex
    parts = []
    for k, v in (("model_id", "scribe_v2"), ("language_code", "ru"), ("tag_audio_events", "false")):
        parts.append(f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode())
    with open(path, "rb") as f:
        parts.append(f'--{b}\r\nContent-Disposition: form-data; name="file"; filename="a.mp3"\r\n'
                     f'Content-Type: audio/mpeg\r\n\r\n'.encode() + f.read() + b"\r\n")
    parts.append(f"--{b}--\r\n".encode())
    data = _request("/speech-to-text", b"".join(parts), {"Content-Type": f"multipart/form-data; boundary={b}"})
    return json.loads(data).get("text", "")


def _take(body, vid, take):
    key = hashlib.sha1(json.dumps([vid, FORMAT, body, take], ensure_ascii=False,
                                  sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if not os.path.exists(mp3):
        data = _request(f"/text-to-speech/{vid}?output_format={FORMAT}",
                        json.dumps({**body, "seed": 1000 + take}).encode(),
                        {"Content-Type": "application/json", "Accept": "audio/mpeg"})
        with open(mp3 + ".part", "wb") as f:
            f.write(data)
        os.replace(mp3 + ".part", mp3)
    txt = mp3[:-4] + ".stt.txt"
    if not os.path.exists(txt):
        with open(txt, "w", encoding="utf-8") as f:
            f.write(transcribe(mp3))
    return mp3, open(txt, encoding="utf-8").read()


def synth(text, voice, out_wav, direction="", stability=0.5, sr=44100, model=MODEL, log=None, **_):
    """Синтезирует text голосом voice (имя из VOICES или voice_id) в моно WAV out_wav.

    direction — режиссёрский тег eleven_v3 без скобок («grim, low voice»), ставится перед фразой.
    Возвращает оценку совпадения выбранного дубля с текстом (0..1)."""
    vid = VOICES.get(voice, voice)
    spoken = accent(text)
    if model == "eleven_v3":
        if direction:
            spoken = f"[{direction}] {spoken}"
        # v3 принимает только 0.0 (Creative), 0.5 (Natural), 1.0 (Robust)
        settings = {"stability": min((0.0, 0.5, 1.0), key=lambda v: abs(v - stability))}
    else:
        settings = {"stability": stability, "similarity_boost": 0.75, "use_speaker_boost": True}
    body = {"text": spoken, "model_id": model, "voice_settings": settings}
    best = None
    for take in range(TAKES):
        mp3, heard = _take(body, vid, take)
        s = score(text, heard)
        if best is None or s > best[0]:
            best = (s, mp3, heard)
        if s >= ACCEPT:
            break
    if log is not None and best[0] < ACCEPT:
        log.append(f"{best[0]:.2f}  {text}\n      слышно: {best[2]}")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", best[1], "-ac", "1", "-ar", str(sr), out_wav],
                   check=True)
    return best[0]
