#!/usr/bin/env python3
"""
Общий синтез речи через ElevenLabs (Text to Speech API) для генераторов озвучки (Tools/*_voice).

Ключ: переменная ELEVENLABS_API_KEY (в облачной сессии Claude ключ подставляет сетевой прокси —
переменная не нужна). Готовые MP3 кешируются в ~/.cache/astropol_elevenlabs по хешу
(голос, модель, настройки, текст), так что повторный запуск не тратит кредиты на неизменённые фразы.
"""
import hashlib
import json
import os
import subprocess
import time
import urllib.error
import urllib.request

API = "https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format=mp3_44100_128"
MODEL = "eleven_multilingual_v2"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")

# Стандартные (premade) голоса — доступны через API на любом тарифе
VOICES = {
    "sarah": "EXAVITQu4vr4xnSDxMaL",    # зрелый, уверенный женский
    "lily": "pFZP5JQG7iQjIQuC4Bku",     # бархатный женский
    "alice": "Xb7hH8MSUJpSbSDYk0k2",    # чёткий женский
    "river": "SAz9YHcvj6GT2YYXdXww",    # нейтральный
    "george": "JBFqnCBsd6RMkjVDRZzb",   # тёплый рассказчик
    "adam": "pNInz6obpgDQGcFmaJgB",     # властный, твёрдый
    "daniel": "onwK4e9ZLuTAKqWW03F9",   # ровный диктор
    "brian": "nPczCjzI2devNBz1zQrb",    # глубокий, спокойный
    "eric": "cjVigY5qzO86Huf0OWal",     # мягкий, доверительный
    "bill": "pqHfZKP75CvOlQylNhV4",     # мудрый, зрелый
}


def synth(text, voice, out_wav, speed=1.0, stability=0.5, style=0.0, sr=44100,
          previous_text=None, next_text=None):
    """Синтезирует text голосом voice (имя из VOICES или voice_id) в моно WAV out_wav с частотой sr."""
    vid = VOICES.get(voice, voice)
    body = {
        "text": text,
        "model_id": MODEL,
        "voice_settings": {"stability": stability, "similarity_boost": 0.75, "style": style,
                           "use_speaker_boost": True, "speed": max(0.7, min(1.2, speed))},
    }
    if previous_text:
        body["previous_text"] = previous_text
    if next_text:
        body["next_text"] = next_text
    key = hashlib.sha1(json.dumps([vid, body], ensure_ascii=False, sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if not os.path.exists(mp3):
        req = urllib.request.Request(
            API.format(voice=vid), data=json.dumps(body).encode(), method="POST",
            headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                     "Content-Type": "application/json", "Accept": "audio/mpeg"})
        for attempt in range(4):
            try:
                with urllib.request.urlopen(req, timeout=120) as r:
                    data = r.read()
                break
            except urllib.error.HTTPError as e:
                if e.code in (429, 500, 502, 503) and attempt < 3:
                    time.sleep(2 ** (attempt + 1))
                    continue
                raise RuntimeError(f"ElevenLabs {e.code}: {e.read().decode(errors='replace')[:300]}") from None
        with open(mp3 + ".part", "wb") as f:
            f.write(data)
        os.replace(mp3 + ".part", mp3)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mp3, "-ac", "1", "-ar", str(sr), out_wav],
                   check=True)
    return out_wav
