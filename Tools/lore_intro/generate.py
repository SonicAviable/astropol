#!/usr/bin/env python3
"""
Пролог «Великое Молчание» — видео о лоре игры, идёт при запуске сразу после логотипа (IntroVideo).

Результат: Assets/StreamingAssets/Intro/lore_ru.mp4 и lore_en.mp4 — 1920×1080, 30 к/с, H.264 + AAC.
Картинка одна на оба языка (иллюстрации игры с медленным наездом, наплывы, виньетка), голос и субтитры — свои.

Источники:
  • голос — ElevenLabs eleven_v3 (Tools/elevenlabs_tts.py): русский — проверка распознаванием, до 5 дублей;
    английский — один дубль (распознавание в общем модуле только русское);
  • музыка — ElevenLabs Music, одна дорожка на оба языка, под голосом приглушается (sidechain);
  • кадры — Assets/Resources/UI/Events, Menu/menu_bg (иллюстрации игры).
Всё кешируется в ~/.cache/astropol_elevenlabs: правка одной фразы перегенерирует только её.

Длительность сцены = самая длинная из двух озвучек фразы + пауза, так что видео у языков одинаковое по длине.

Запуск из корня репозитория:
    python3 Tools/lore_intro/generate.py            # всё
    python3 Tools/lore_intro/generate.py --preview  # только кадры-превью сцен (без звука и видео)
"""
import hashlib
import json
import math
import os
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import elevenlabs_tts as tts  # noqa: E402

RES = "Assets/Resources"
OUT = "Assets/StreamingAssets/Intro"
CACHE = os.path.expanduser("~/.cache/astropol_elevenlabs")
W, H, FPS = 1920, 1080, 30
XFADE = 1.0          # наплыв между сценами, с
LEAD, TAIL = 1.2, 5.0  # тишина до первой фразы и музыка после последней
FONT_TITLE = RES + "/Fonts/Spectral-SemiBold.ttf"
FONT_MONO = RES + "/Fonts/IBMPlexMono-Medium.ttf"

# Титры перед прологом (с): «Студия SmartBiz представляет» на чёрном, затем «ASTROPOLITY» над проступающей галактикой
STUDIO_IN, STUDIO_OUT = 0.6, 5.2
TITLE_IN, TITLE_OUT = 5.9, 12.2
GALAXY_IN = 6.4        # первая сцена (галактика) проявляется под названием
FIRST_LINE = 13.0      # первая фраза рассказчика
CARDS = {
    "ru": ("СТУДИЯ", "SmartBiz", "представляет", "ПРОЛОГ  ·  ВЕЛИКОЕ МОЛЧАНИЕ"),
    "en": ("STUDIO", "SmartBiz", "presents", "PROLOGUE  ·  THE GREAT SILENCE"),
}
GOLD = (231, 193, 112)
CREAM = (236, 229, 211)
FONT = RES + "/Fonts/PTSerif-Regular.ttf"
FONT_IT = RES + "/Fonts/PTSerif-Italic.ttf"

# Голос рассказчика: (голос, режиссёрский тег v3)
VOICE_RU = ("gleb", "deep, slow, solemn cinematic narration, quiet awe")
VOICE_EN = ("george", "deep, slow, solemn cinematic narration, quiet awe")

# Сцены: картинка, движение камеры (зум от→до, сдвиг центра от→до в долях кадра), пауза после фразы, ru, en
# Движение: (z0, z1, (x0, y0), (x1, y1)) — z — во сколько раз кадр меньше картинки по ширине (1 = вся ширина)
SCENES = [
    ("Menu/menu_bg", (1.0, 1.18, (0.5, 0.5), (0.55, 0.47)), 1.2,
     "Шестьдесят тысяч лет назад галактика пела.",
     "Sixty thousand years ago, the galaxy sang."),
    ("UI/Events/Wonder_Gate", (1.12, 1.0, (0.5, 0.5), (0.5, 0.5)), 0.7,
     "Народ, который мы зовём Строителями, связал звёзды вратами и зажигал солнца там, где их не хватало.",
     "The people we call the Builders bound the stars together with gates, and lit new suns wherever suns were lacking."),
    ("UI/Events/HeartVisitors", (1.0, 1.15, (0.5, 0.55), (0.5, 0.42)), 0.8,
     "Миллиарды голосов сливались в одну песню. Они думали, что так будет всегда.",
     "Billions of voices merged into a single song. They believed it would last forever."),
    ("UI/Events/Rift", (1.05, 1.3, (0.5, 0.5), (0.5, 0.5)), 1.0,
     "Но из-за края галактики пришёл Слушающий. Он находил миры по их голосу — и после него они замолкали навсегда.",
     "But from beyond the edge of the galaxy came the Listener. It found worlds by their voices — and once it passed, they fell silent forever."),
    ("UI/Events/Derelict", (1.0, 1.12, (0.42, 0.5), (0.58, 0.5)), 0.7,
     "Строители не смогли с ним сражаться. И тогда они выбрали тишину.",
     "The Builders could not fight it. And so they chose silence."),
    ("UI/Events/Heart", (1.15, 1.0, (0.5, 0.6), (0.5, 0.5)), 0.6,
     "Они погасили звёзды, заперли врата и спрятали свои миры в тени глушителей.",
     "They put out their stars, sealed their gates, and hid their worlds in the shadow of great dampers."),
    ("UI/Events/Leviathan", (1.0, 1.15, (0.45, 0.5), (0.55, 0.45)), 0.7,
     "А у руин оставили стражей — чтобы никто больше не запел слишком громко.",
     "And by their ruins they left guardians — so that no one would ever sing too loudly again."),
    ("UI/Events/PrecursorVault", (1.0, 1.2, (0.5, 0.5), (0.5, 0.55)), 1.6,
     "Шестьдесят тысяч лет галактика молчала.",
     "For sixty thousand years, the galaxy was silent."),
    ("UI/Events/ArkDawn", (1.0, 1.12, (0.5, 0.5), (0.45, 0.5)), 0.5,
     "Теперь у звёзд снова новые голоса.",
     "Now the stars have new voices once again."),
    ("UI/Events/Ark", (1.0, 1.1, (0.45, 0.5), (0.55, 0.5)), 0.2,
     "Беглецы двенадцати ковчегов.",
     "The refugees of the twelve arks."),
    ("UI/Events/LegionReview", (1.0, 1.1, (0.5, 0.5), (0.5, 0.45)), 0.2,
     "Легионы Ксарн.",
     "The legions of Xarn."),
    ("UI/Events/Shareholders", (1.0, 1.1, (0.55, 0.5), (0.45, 0.5)), 0.2,
     "Торговцы Аквилы.",
     "The merchants of Aquila."),
    ("UI/Events/OracleVisions", (1.0, 1.1, (0.5, 0.45), (0.5, 0.55)), 0.2,
     "Провидицы Иридии.",
     "The seers of Iridia."),
    ("UI/Events/Wonder_Tree", (1.0, 1.1, (0.5, 0.55), (0.5, 0.45)), 0.2,
     "Древний лес Тэрра-ан.",
     "The ancient forest of Terra'an."),
    ("UI/Events/Consensus", (1.0, 1.1, (0.5, 0.5), (0.5, 0.5)), 0.6,
     "И бесстрастный разум Ноля.",
     "And the dispassionate mind of Zero."),
    ("UI/Events/PrecursorCore", (1.0, 1.15, (0.5, 0.5), (0.5, 0.42)), 0.5,
     "Они находят руины Строителей и принимают их за сокровища.",
     "They find the ruins of the Builders and mistake them for treasure."),
    ("UI/Events/Battlefield", (1.0, 1.15, (0.55, 0.5), (0.45, 0.5)), 0.7,
     "Они строят флоты, спорят и воюют — и поют громче, чем когда-либо.",
     "They build fleets, they argue, they go to war — and they sing louder than ever before."),
    ("UI/Events/PrecursorSignal", (1.0, 1.2, (0.6, 0.5), (0.62, 0.45)), 1.0,
     "А где-то на краю древний пульсар отсчитывает последние такты.",
     "And somewhere at the edge, an ancient pulsar counts down the final beats."),
    ("Menu/menu_bg", (1.25, 1.0, (0.5, 0.5), (0.5, 0.5)), 0.0,
     "Галактика снова поёт. Чья песня прозвучит последней?",
     "The galaxy sings again. Whose song will be the last?"),
]

# Субтитры без ударений и служебных дефисов произношения
SUB_RU = {"Тэрра-ан": "Тэрра'ан"}

MUSIC_PROMPT = (
    "epic dark cinematic orchestral prologue for a space strategy game trailer, a story about an ancient galactic "
    "civilization that went silent. Opens with a lonely ethereal female wordless voice and celesta over deep space "
    "drones, grows into slow mournful strings and low brass, a hushed eerie middle section with sparse piano and "
    "distant choir, then a building rhythmic pulse of taiko drums and ostinato strings as new civilizations rise, "
    "a powerful majestic climax with full choir near the end, and a quiet suspenseful ending on a single held note. "
    "Leaves space for a narrator. Instrumental only, no lyrics")


# ======================================================================== Голос

def voice_en(text, out_wav):
    """Английская фраза: один дубль eleven_v3 с кешем (без проверки распознаванием)."""
    voice, direction = VOICE_EN
    body = {"text": f"[{direction}] {text}", "model_id": tts.MODEL, "voice_settings": {"stability": 0.5}}
    vid = tts.VOICES.get(voice, voice)
    key = hashlib.sha1(json.dumps([vid, tts.FORMAT, body, 0], ensure_ascii=False, sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if not os.path.exists(mp3):
        data = tts._request(f"/text-to-speech/{vid}?output_format={tts.FORMAT}", json.dumps({**body, "seed": 1000}).encode(),
                            {"Content-Type": "application/json", "Accept": "audio/mpeg"})
        with open(mp3 + ".part", "wb") as f:
            f.write(data)
        os.replace(mp3 + ".part", mp3)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", mp3, "-ac", "1", "-ar", "44100", out_wav], check=True)


STUDIO = ("highpass=f=70,deesser=i=0.3,equalizer=f=120:t=q:w=1:g=2,equalizer=f=350:t=q:w=1.5:g=-1.5,"
          "equalizer=f=3200:t=q:w=1.4:g=1.5,acompressor=threshold=-20dB:ratio=2.5:attack=6:release=150:makeup=1.5,"
          "aecho=0.85:0.55:60|130|240:0.14|0.09|0.05,"
          "silenceremove=start_periods=1:start_threshold=-50dB,areverse,"
          "silenceremove=start_periods=1:start_threshold=-50dB,areverse")


def duration(path):
    return float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                                capture_output=True, text=True).stdout.strip() or 0)


def make_voices(tmp):
    """Озвучить все фразы. Возвращает {lang: [(wav, длительность)]}."""
    res = {"ru": [], "en": []}
    bad = []
    for i, sc in enumerate(SCENES):
        ru, en = sc[3], sc[4]
        raw_ru, raw_en = os.path.join(tmp, f"ru_{i}_raw.wav"), os.path.join(tmp, f"en_{i}_raw.wav")
        tts.synth(ru, VOICE_RU[0], raw_ru, direction=VOICE_RU[1], log=bad)
        voice_en(en, raw_en)
        for lang, raw in (("ru", raw_ru), ("en", raw_en)):
            wav = os.path.join(tmp, f"{lang}_{i}.wav")
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", raw, "-af", STUDIO, wav], check=True)
            res[lang].append((wav, duration(wav)))
        print(f"  [{i + 1}/{len(SCENES)}] ru {res['ru'][-1][1]:.1f} c · en {res['en'][-1][1]:.1f} c")
    if bad:
        print("Неточные дубли (проверьте на слух):\n" + "\n".join(bad))
    return res


# ======================================================================== Музыка

def make_music(length_s):
    body = {"prompt": MUSIC_PROMPT, "music_length_ms": int(length_s * 1000), "model_id": "music_v1",
            "force_instrumental": True}
    key = hashlib.sha1(json.dumps(["music", body], sort_keys=True).encode()).hexdigest()
    os.makedirs(CACHE, exist_ok=True)
    mp3 = os.path.join(CACHE, key + ".mp3")
    if os.path.exists(mp3):
        return mp3
    req = urllib.request.Request("https://api.elevenlabs.io/v1/music?output_format=mp3_44100_192",
                                 data=json.dumps(body).encode(), method="POST",
                                 headers={"xi-api-key": os.getenv("ELEVENLABS_API_KEY", "proxy-injected"),
                                          "Content-Type": "application/json"})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=900) as r:
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


# ======================================================================== Раскладка

def timeline(voices):
    """Начало каждой сцены и фразы. Сцены идут встык с наплывом XFADE."""
    t = FIRST_LINE
    starts, speech = [], []
    for i, sc in enumerate(SCENES):
        d = max(voices["ru"][i][1], voices["en"][i][1])
        scene_start = GALAXY_IN if i == 0 else t - XFADE * 0.5
        starts.append(max(0.0, scene_start))
        speech.append((t, d))
        t += d + sc[2] + XFADE * 0.5
    total = t + TAIL
    ends = [starts[i + 1] + XFADE if i + 1 < len(starts) else total for i in range(len(starts))]
    return starts, ends, speech, total


# ======================================================================== Картинка

_img_cache = {}


def source(path):
    if path not in _img_cache:
        im = Image.open(os.path.join(RES, path + ".jpg")).convert("RGB")
        # кадр 16:9 из картинки (иллюстрации 2000×1116 — почти 16:9)
        w, h = im.size
        if w / h > 16 / 9:
            nw = int(h * 16 / 9)
            im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
        else:
            nh = int(w * 9 / 16)
            im = im.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
        # запас разрешения для наезда
        im = im.resize((int(W * 1.3), int(H * 1.3)), Image.LANCZOS)
        _img_cache[path] = im
    return _img_cache[path]


def ease(x):
    return 0.5 - 0.5 * math.cos(math.pi * min(1.0, max(0.0, x)))


def scene_frame(i, u):
    """Кадр сцены i в момент u (0..1 от её длительности): медленный наезд и сдвиг."""
    path, (z0, z1, c0, c1) = SCENES[i][0], SCENES[i][1]
    im = source(path)
    sw, sh = im.size
    e = ease(u) * 0.5 + u * 0.5
    z = z0 + (z1 - z0) * e
    cx = c0[0] + (c1[0] - c0[0]) * e
    cy = c0[1] + (c1[1] - c0[1]) * e
    cw, ch = sw / z, sh / z
    x0 = min(max(cx * sw - cw / 2, 0), sw - cw)
    y0 = min(max(cy * sh - ch / 2, 0), sh - ch)
    # аффинное преобразование с субпиксельной точностью — без дрожания при медленном наезде
    return im.transform((W, H), Image.AFFINE, (cw / W, 0, x0, 0, ch / H, y0), resample=Image.BICUBIC)


def vignette():
    y, x = np.mgrid[0:H, 0:W]
    d = np.sqrt(((x - W / 2) / (W / 2)) ** 2 + ((y - H / 2) / (H / 2)) ** 2)
    v = np.clip(1.0 - 0.55 * np.clip(d - 0.45, 0, None) ** 1.6, 0.35, 1.0)
    # затемнение снизу под субтитры (как в кино): нижняя треть плавно темнеет до 45%
    g = np.clip((y / H - 0.62) / 0.38, 0, 1)
    v = v * (1.0 - 0.55 * g ** 1.3)
    return v[..., None].astype(np.float32)


def wrap(draw, text, font, maxw):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if draw.textlength(t, font=font) <= maxw or not cur:
            cur = t
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def subtitle_layer(text, italic=False):
    """Субтитр: кремовый текст с мягкой тенью, по центру внизу. Возвращает RGBA-слой кадра."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    font = ImageFont.truetype(FONT_IT if italic else FONT, 44)
    d = ImageDraw.Draw(layer)
    lines = wrap(d, text, font, W * 0.66)
    lh = 58
    y = H - 110 - lh * len(lines)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ds = ImageDraw.Draw(shadow)
    for k, ln in enumerate(lines):
        tw = d.textlength(ln, font=font)
        x = (W - tw) / 2
        ds.text((x, y + k * lh + 2), ln, font=font, fill=(0, 0, 0, 255), stroke_width=6, stroke_fill=(0, 0, 0, 255))
        d.text((x, y + k * lh), ln, font=font, fill=(236, 229, 211, 255), stroke_width=1, stroke_fill=(20, 16, 10, 200))
    shadow = shadow.filter(ImageFilter.GaussianBlur(9))
    shadow = Image.alpha_composite(shadow, shadow)
    return Image.alpha_composite(shadow, layer)


def tracked(draw, cx, text, fnt, spacing, fill, y):
    """Текст с разрядкой (spacing px между буквами) по центру относительно cx."""
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + spacing * (len(text) - 1)
    x = cx - total / 2
    for ch, w in zip(text, widths):
        draw.text((x, y), ch, font=fnt, fill=fill)
        x += w + spacing
    return total


def card_alpha(t, t0, t1, fin=1.4, fout=1.2):
    if t < t0 or t > t1:
        return 0.0
    return ease(min(1.0, (t - t0) / fin, (t1 - t) / fout))


_fonts = {}


def font(path, size):
    k = (path, size)
    if k not in _fonts:
        _fonts[k] = ImageFont.truetype(path, size)
    return _fonts[k]


def fade(layer, a):
    arr = np.asarray(layer).astype(np.float32)
    arr[..., 3] *= a
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def glow(layer, radius, strength):
    g = layer.filter(ImageFilter.GaussianBlur(radius))
    arr = np.asarray(g).astype(np.float32)
    arr[..., 3] = np.clip(arr[..., 3] * strength, 0, 255)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


_gold_grad = None


def gold_gradient(y0, h):
    """Золото по высоте букв: светлое сверху, тёмное снизу."""
    global _gold_grad
    if _gold_grad is None:
        yy = np.clip((np.arange(H, dtype=np.float32) - y0) / h, 0, 1)[:, None]
        top, bottom = np.array([252, 236, 190], np.float32), np.array([190, 140, 62], np.float32)
        col = top[None, :] * (1 - yy) + bottom[None, :] * yy
        _gold_grad = np.repeat(col[:, None, :], W, axis=1)
    return _gold_grad


def title_card(lang, t):
    """Титры: «Студия SmartBiz представляет», затем «ASTROPOLITY». None — титров в этот момент нет."""
    studio, name, presents, eyebrow = CARDS[lang]
    a1 = card_alpha(t, STUDIO_IN, STUDIO_OUT)
    a2 = card_alpha(t, TITLE_IN, TITLE_OUT, fin=1.8, fout=1.6)
    if a1 <= 0.0 and a2 <= 0.0:
        return None
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = W / 2, H / 2
    if a1 > 0.0:
        k = (t - STUDIO_IN) / (STUDIO_OUT - STUDIO_IN)       # разрядка медленно «раскрывается»
        tracked(d, cx, studio, font(FONT_MONO, 22), 14 + 6 * k, GOLD + (255,), cy - 96)
        tracked(d, cx, name, font(FONT_TITLE, 96), 4 + 5 * k, CREAM + (255,), cy - 66)
        lw = 150 + 40 * k
        d.line([(cx - lw, cy + 62), (cx - 14, cy + 62)], fill=GOLD + (150,), width=1)
        d.line([(cx + 14, cy + 62), (cx + lw, cy + 62)], fill=GOLD + (150,), width=1)
        d.polygon([(cx, cy + 56), (cx + 6, cy + 62), (cx, cy + 68), (cx - 6, cy + 62)], fill=GOLD + (230,))
        tracked(d, cx, presents, font(FONT_IT, 34), 1.5, (200, 192, 172, 255), cy + 80)
        layer = fade(layer, a1)
        return Image.alpha_composite(glow(layer, 14, 0.5), layer)
    k = (t - TITLE_IN) / (TITLE_OUT - TITLE_IN)
    ty = cy - 128
    tl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    tracked(ImageDraw.Draw(tl), cx, "ASTROPOLITY", font(FONT_TITLE, 150), 16 + 10 * k, (255, 255, 255, 255), ty)
    arr = np.asarray(tl).astype(np.float32)
    arr[..., :3] = gold_gradient(ty + 40, 140)
    tl = Image.fromarray(arr.astype(np.uint8), "RGBA")
    lw = 300 + 120 * k
    d.line([(cx - lw, cy + 74), (cx - 16, cy + 74)], fill=GOLD + (170,), width=1)
    d.line([(cx + 16, cy + 74), (cx + lw, cy + 74)], fill=GOLD + (170,), width=1)
    d.polygon([(cx, cy + 67), (cx + 7, cy + 74), (cx, cy + 81), (cx - 7, cy + 74)], fill=GOLD + (240,))
    tracked(d, cx, eyebrow, font(FONT_MONO, 22), 8, (214, 205, 182, 235), cy + 100)
    layer = fade(Image.alpha_composite(layer, tl), a2)
    return Image.alpha_composite(glow(layer, 22, 0.75), layer)


def render(lang, starts, ends, speech, total, audio, out):
    vig = vignette()
    subs = [None] * len(SCENES)
    for i, sc in enumerate(SCENES):
        if lang == "ru":
            txt = sc[3]
            for a, b in SUB_RU.items():
                txt = txt.replace(a, b)
            subs[i] = subtitle_layer(txt, italic=(i == len(SCENES) - 1))
        else:
            subs[i] = subtitle_layer(sc[4], italic=(i == len(SCENES) - 1))
    n = int(math.ceil(total * FPS))
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS),
           "-i", "-", "-i", audio, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "slow", "-crf", "25", "-tune", "film",
           "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-c:a", "aac", "-b:a", "192k", "-shortest", out]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(n):
        t = f / FPS
        frame = np.zeros((H, W, 3), np.float32)
        weight = 0.0
        for i in range(len(SCENES)):
            s, e = starts[i], ends[i]
            if t < s or t > e:
                continue
            a = 1.0
            if i > 0 and t < s + XFADE:
                a = (t - s) / XFADE
            if i == 0:
                a = min(1.0, max(0.0, (t - s) / 3.0))   # галактика проступает из темноты под названием
            if i == len(SCENES) - 1 and t > total - 3.0:
                a = max(0.0, (total - t) / 3.0)  # уход в темноту
            a = ease(a)
            img = np.asarray(scene_frame(i, (t - s) / max(0.01, e - s)), np.float32)
            frame = frame * (1 - a) + img * a if weight > 0 else img * a
            weight = 1.0
        frame *= vig
        out_im = Image.fromarray(np.clip(frame, 0, 255).astype(np.uint8), "RGB")
        card = title_card(lang, t)
        if card is not None:
            out_im = Image.alpha_composite(out_im.convert("RGBA"), card).convert("RGB")
        # субтитр фразы: появляется чуть раньше голоса, держится до конца фразы
        for i, (ts, d) in enumerate(speech):
            a0, a1 = ts - 0.25, ts + d + 0.45
            if a0 <= t <= a1:
                k = min(1.0, (t - a0) / 0.35, (a1 - t) / 0.35)
                layer = subs[i]
                if k < 1.0:
                    al = np.asarray(layer).copy()
                    al[..., 3] = (al[..., 3] * k).astype(np.uint8)
                    layer = Image.fromarray(al, "RGBA")
                out_im = Image.alpha_composite(out_im.convert("RGBA"), layer).convert("RGB")
        p.stdin.write(out_im.tobytes())
        if f % (FPS * 10) == 0:
            print(f"  {lang}: {t:5.1f} / {total:.1f} c")
    p.stdin.close()
    if p.wait() != 0:
        raise RuntimeError("ffmpeg: ошибка кодирования")


def mix(lang, voices, speech, total, music, tmp):
    """Голос по местам + музыка, приглушаемая под голосом. Громкость — около −16 LUFS."""
    vo = os.path.join(tmp, f"vo_{lang}.wav")
    inputs, parts = [], []
    for i, (ts, _) in enumerate(speech):
        inputs += ["-i", voices[lang][i][0]]
        ms = int(ts * 1000)
        parts.append(f"[{i}:a]adelay={ms}|{ms},apad[a{i}]")
    filt = ";".join(parts) + ";" + "".join(f"[a{i}]" for i in range(len(speech))) + \
        f"amix=inputs={len(speech)}:normalize=0,atrim=0:{total:.2f},aformat=channel_layouts=stereo[vo]"
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", *inputs, "-filter_complex", filt, "-map", "[vo]",
                    "-ar", "44100", vo], check=True)
    out = os.path.join(tmp, f"mix_{lang}.wav")
    fade = max(0.0, total - 4.0)
    filt = (f"[1:a]aresample=44100,aformat=channel_layouts=stereo,atrim=0:{total:.2f},volume=-10dB,"
            f"afade=t=in:d=1.5,afade=t=out:st={fade:.2f}:d=4[m];"
            "[0:a]asplit=2[v1][v2];"
            "[m][v1]sidechaincompress=threshold=0.015:ratio=10:attack=30:release=900:makeup=1[md];"
            "[v2]volume=4dB[vl];"
            "[md][vl]amix=inputs=2:normalize=0,loudnorm=I=-18:TP=-1.5:LRA=9[out]")
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", vo, "-i", music, "-filter_complex", filt,
                    "-map", "[out]", "-ar", "44100", out], check=True)
    return out


def preview(path):
    """Кадр из середины каждой сцены — для проверки движения и кадрирования."""
    tiles = [scene_frame(i, 0.5).resize((480, 270)) for i in range(len(SCENES))]
    cols = 4
    rows = math.ceil(len(tiles) / cols)
    sheet = Image.new("RGB", (480 * cols, 270 * rows))
    for k, t in enumerate(tiles):
        sheet.paste(t, ((k % cols) * 480, (k // cols) * 270))
    sheet.save(path, quality=85)
    print("превью:", path)


def main():
    if "--titles" in sys.argv:
        bg = Image.new("RGB", (W, H), (8, 10, 18))
        for lang in ("ru", "en"):
            for t in (3.0, 9.0):
                im = Image.alpha_composite(bg.convert("RGBA"), title_card(lang, t)).convert("RGB")
                pth = os.path.join(tempfile.gettempdir(), f"title_{lang}_{int(t)}.jpg")
                im.save(pth, quality=88)
                print(pth)
        return
    if "--preview" in sys.argv:
        preview(os.path.join(tempfile.gettempdir(), "lore_preview.jpg"))
        return
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        print("Голос:")
        voices = make_voices(tmp)
        starts, ends, speech, total = timeline(voices)
        print(f"Длительность: {total:.1f} c")
        print("Музыка…")
        music = make_music(min(300.0, math.ceil(total) + 4))
        for lang in ("ru", "en"):
            audio = mix(lang, voices, speech, total, music, tmp)
            render(lang, starts, ends, speech, total, audio, os.path.join(OUT, f"lore_{lang}.mp4"))
            print(f"готово: {OUT}/lore_{lang}.mp4")


if __name__ == "__main__":
    main()
