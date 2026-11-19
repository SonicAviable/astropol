#!/usr/bin/env python3
"""
Озвучка летописей фракций (вступление перед партией, FactionHistoryWindow).

Фразы берутся прямо из Assets/Scripts/Core/FactionHistory.cs — это единственный источник текста:
поправили фразу там — перезапустите скрипт. Ударения в именах и редких словах — словарь STRESS ниже
(«+» перед ударной гласной).

Синтез — ElevenLabs (Tools/elevenlabs_tts.py, голоса — ELEVEN ниже); с флагом --vosk — Vosk TTS (нейросетевая русская модель vosk-model-tts-ru-0.9-multi, лицензия Apache 2.0,
5 голосов: female_0..2 = 0..2, male_0..1 = 3..4). У каждой фракции свой рассказчик и своя обработка
через ffmpeg: тёплый «трейлерный» голос (EQ, компрессия, зал), для Ксарн ниже и тяжелее, для Иридии —
эфирный хор, для «Ноля» — машинный блеск. Громкость выравнивается к −16 LUFS.

Ещё скрипт синтезирует кинематографичные звуки хроники: удар (sfx_impact) и нарастание (sfx_riser).

Результат — Assets/Resources/Audio/Chronicle/<ключ>_<глава>_<фраза>.ogg (глава 0..3, финал — f).

Нужно: python3 -m venv venv && venv/bin/pip install vosk-tts numpy; ffmpeg.
Модель (~750 МБ) скачается сама при первом запуске.
Запуск из корня репозитория:
    venv/bin/python Tools/chronicle_voice/generate.py            # всё
    venv/bin/python Tools/chronicle_voice/generate.py xarn nol   # только эти фракции
    venv/bin/python Tools/chronicle_voice/generate.py xarn_1_0   # одна фраза
    venv/bin/python Tools/chronicle_voice/generate.py --sfx      # только звуки
    venv/bin/python Tools/chronicle_voice/generate.py --stress   # проверка ударений
    venv/bin/python Tools/chronicle_voice/generate.py --vosk     # офлайн-синтез Vosk вместо ElevenLabs
"""
import os
import re
import subprocess
import sys
import tempfile
import wave

import numpy as np

SRC = "Assets/Scripts/Core/FactionHistory.cs"
OUT = "Assets/Resources/Audio/Chronicle"
MODEL = "vosk-model-tts-ru-0.9-multi"
TARGET_LUFS = -16.0

# Обработка голоса: общий «киношный» тракт и добавки по фракциям
WARM = ("highpass=f=70,equalizer=f=160:t=q:w=1.1:g=2.5,equalizer=f=420:t=q:w=1.5:g=-1.5,"
        "equalizer=f=3300:t=q:w=1.3:g=2.5,equalizer=f=9000:t=q:w=1:g=1.5,"
        "acompressor=threshold=-22dB:ratio=3.2:attack=4:release=140:makeup=2")
HALL = "aecho=0.85:0.55:45|95|160:0.16|0.10|0.06"


def pitch(f):
    """Сдвиг высоты голоса без изменения темпа (f < 1 — ниже)."""
    return f"asetrate=22050*{f},aresample=22050,atempo={1 / f:.4f}"


NARRATORS = {
    # ключ: (голос, скорость речи, цепочка ffmpeg)
    "astrea":  (2, 0.92, f"{WARM},{HALL}"),
    "xarn":    (3, 0.88, f"{pitch(0.93)},{WARM},equalizer=f=110:t=q:w=1:g=2.5,aecho=0.85:0.5:60|130:0.18|0.1"),
    "aquila":  (4, 0.95, f"{WARM},aecho=0.8:0.4:30|70:0.12|0.06"),
    "iridia":  (0, 0.88, f"{WARM},chorus=0.8:0.9:22|31:0.14|0.1:0.25|0.2:1.2|0.9,"
                         "aecho=0.85:0.6:70|150|260:0.22|0.14|0.08"),
    "terraan": (1, 0.85, f"{pitch(0.95)},{WARM},aecho=0.85:0.6:80|170|300:0.2|0.12|0.07"),
    "nol":     (4, 0.90, f"{pitch(0.97)},{WARM},chorus=0.6:0.9:9|14:0.32|0.26:0.6|0.5:2.1|1.7,"
                         "aecho=0.8:0.45:18|36:0.2|0.1"),
}

# ElevenLabs (eleven_v3): у каждой фракции свой русский голос из библиотеки, режиссёрский тег и чистая
# «студийная» обработка — де-эссер, мягкая компрессия, присутствие и своё пространство (без питч-шифта:
# характер даёт сам актёр). Ключ: (голос из Tools/elevenlabs_tts.py VOICES, тег v3, цепочка ffmpeg)
STUDIO = ("highpass=f=75,deesser=i=0.35,equalizer=f=200:t=q:w=1:g=1.5,equalizer=f=350:t=q:w=1.5:g=-1.5,"
          "equalizer=f=3200:t=q:w=1.4:g=1.5,equalizer=f=11000:t=q:w=1:g=1,"
          "acompressor=threshold=-21dB:ratio=2.5:attack=6:release=150:makeup=1.5")
ELEVEN = {
    "astrea":  ("victoria", "warm, hopeful, cinematic narration",
                f"{STUDIO},aecho=0.85:0.5:40|85:0.10|0.05"),
    "xarn":    ("nester", "grim, low voice, slow and heavy",
                f"{STUDIO},equalizer=f=110:t=q:w=1:g=2,aecho=0.85:0.5:55|120:0.12|0.07"),
    "aquila":  ("egor", "confident, smooth, slightly ironic",
                f"{STUDIO},aecho=0.8:0.4:30|65:0.08|0.04"),
    "iridia":  ("elen", "mysterious, dreamy, unhurried",
                f"{STUDIO},chorus=0.85:0.9:24|33:0.08|0.06:0.25|0.2:1.2|0.9,"
                "aecho=0.85:0.6:70|150|260:0.16|0.10|0.06"),
    "terraan": ("tatyana", "ancient, slow, deep and earthy",
                f"{STUDIO},aecho=0.85:0.6:80|170|300:0.14|0.09|0.05"),
    "nol":     ("gleb", "cold, emotionless, precise",
                f"{STUDIO},chorus=0.6:0.9:9|14:0.22|0.18:0.6|0.5:2.1|1.7,aecho=0.8:0.45:18|36:0.14|0.07"),
}

# Ударения и произношение («+» перед ударной гласной). Проверка: generate.py --stress — все слова летописей
# с ударением, которое прочитает модель (ударная гласная заглавная); «*» — омограф (у слова в словаре модели
# несколько произношений, выбрано самое частое — проверьте по смыслу), «!» — слова нет в словаре (ударение
# не будет поставлено вовсе — допишите его сюда).
STRESS = {
    "Тэрра'ан": "Тэрра-+ан",
    "Астреей": "Астр+еей", "Астрея": "Астр+ея", "Астреи": "Астр+еи",
    "Аквила": "Акв+ила", "Аквилы": "Акв+илы", "Аквилу": "Акв+илу",
    "Иридии": "Ир+идии", "Иридия": "Ир+идия", "Иридией": "Ир+идией", "Иридийцы": "Ирид+ийцы",
    "Ворх": "В+орх", "Ксарн": "Кс+арн", "ксарны": "кс+арны", "Ксарны": "Кс+арны",
    "Оракул": "Ор+акул", "Оракулы": "Ор+акулы",
    "Консенсус": "Консенс+ус", "Консенсуса": "Консенс+уса",
    "Синдикат": "Синдик+ат", "Синдикату": "Синдик+ату",
    "мицелия": "миц+елия", "биокоры": "биокор+ы", "гиперкоридоры": "гиперкорид+оры",
    "ковчегов": "ковч+егов", "органиков": "орг+аников", "полгалактики": "полгал+актики",
    "вычислителей": "вычисл+ителей", "чревах": "чр+евах", "Хартию": "Х+артию", "Хартия": "Х+артия",
    "провидиц": "пров+идиц", "грядущих": "гряд+ущих", "древа": "др+ева", "древо": "др+ево",
    "шелест": "ш+елест", "Председатель": "Председ+атель", "директор": "дир+ектор",
    "Хранительница": "Хран+ительница", "Аватар": "Ават+ар", "переменная": "перем+енная",
    "заживляли": "заживл+яли", "оплели": "оплел+и", "оплетали": "оплет+али", "опутали": "опут+али",
    # Омографы: словарь модели выбирает самое частое произношение без учёта смысла — задаём по контексту
    "дыму": "дым+у", "звезды": "звезд+ы", "краю": "кра+ю", "Купите": "К+упите", "другом": "др+угом",
    "Запомню": "Запомн+ю", "Согласны": "Согл+асны", "мицелий": "миц+елий",
    "легионы": "леги+оны", "Доминион": "Домини+он", "Доминиона": "Домини+она",
}


def voice_text(s):
    s = re.sub(r"\s*—\s*", ", ", s).replace("«", "").replace("»", "")
    for k, v in sorted(STRESS.items(), key=lambda kv: -len(kv[0])):
        s = re.sub(r"(?<![\w+'])" + re.escape(k) + r"(?![\w'])", v, s)
    return re.sub(r"\s+", " ", s).strip()


def parse_lines():
    """{ключ: [(глава, [фразы])]} из FactionHistory.cs."""
    src = open(SRC, encoding="utf-8").read()
    body = src[src.index("Build() => new[]"):]
    tok = re.compile(r'\bF\("(\w+)"|\bC\(|\bFinale\(|Loc\.T\("((?:[^"\\]|\\.)*)"\)')
    data, key, mode, strings = {}, None, None, []

    def flush():
        if key is None or mode is None:
            return
        if mode == "C":
            data[key].append((str(len(data[key])), strings[2:]))
        else:
            data[key].append(("f", list(strings)))

    for m in tok.finditer(body):
        if m.group(1):
            flush(); mode = None; strings = []
            key = m.group(1); data[key] = []
        elif m.group(0) == "C(" or m.group(0) == "Finale(":
            flush(); strings = []
            mode = "C" if m.group(0) == "C(" else "F"
        else:
            strings.append(m.group(2).replace('\\"', '"'))
    flush()
    return data


def run(cmd):
    subprocess.run(cmd, check=True)


def lufs(path):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-i", path, "-af", "ebur128", "-f", "null", "-"],
                       capture_output=True, text=True)
    m = re.findall(r"I:\s+(-?[\d.]+) LUFS", r.stderr)
    return float(m[-1]) if m else -20.0


def render(raw, chain, out):
    """Обработка → выравнивание громкости → OGG 44,1 кГц моно с хвостом под реверберацию."""
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "mid.wav")
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", raw, "-af",
             f"apad=pad_dur=0.6,{chain},aresample=44100", "-ac", "1", mid])
        gain = TARGET_LUFS - lufs(mid)
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", mid, "-af",
             f"volume={gain:.2f}dB,alimiter=limit=0.89:level=false,"
             "silenceremove=start_periods=1:start_threshold=-60dB,"
             "areverse,silenceremove=start_periods=1:start_threshold=-62dB,areverse,"
             "afade=t=in:d=0.01", "-c:a", "libvorbis", "-q:a", "5", out])


def stress_report():
    """Все слова летописей с ударением, которое прочитает модель (см. комментарий к STRESS)."""
    from vosk_tts import Model
    dic = Model(model_name=MODEL).dic
    variants = {}
    for line in open(os.path.join(os.path.dirname(dic_path(MODEL)), "dictionary"), encoding="utf-8"):
        w, _, ph = line.split(maxsplit=2)
        variants.setdefault(w, set()).add(tuple(x for x in ph.split() if x[-1] in "01"))
    vowels = "аеёиоуыэюя"
    seen, out = set(), []
    for chapters in parse_lines().values():
        for _, lines in chapters:
            for line in lines:
                for w in re.split(r"[ ,.?!;:\"()]+", voice_text(line).lower()):
                    if not w or w in seen or sum(c in vowels for c in w) < 2:
                        continue
                    seen.add(w)
                    if "+" in w:
                        out.append(re.sub(r"\+(.)", lambda m: m.group(1).upper(), w))
                        continue
                    if w not in dic:
                        out.append("!" + w)
                        continue
                    stress = [i for i, p in enumerate(x for x in dic[w].split() if x[-1] in "01") if p[-1] == "1"]
                    vi, mark = 0, ""
                    for c in w:
                        if c in vowels:
                            mark += c.upper() if vi in stress else c
                            vi += 1
                        else:
                            mark += c
                    out.append(("*" if len(variants.get(w, ())) > 1 else "") + mark)
    print(" ".join(out))


def dic_path(name):
    for d in (os.getenv("VOSK_MODEL_PATH"), "/usr/share/vosk", os.path.expanduser("~/.cache/vosk")):
        if d and os.path.exists(os.path.join(d, name, "dictionary")):
            return os.path.join(d, name, "dictionary")
    raise FileNotFoundError(name)


def synth_all(keys, vosk=False):
    if vosk:
        from vosk_tts import Model, Synth
        synth = Synth(Model(model_name=MODEL))
    else:
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
        import elevenlabs_tts
    data = parse_lines()
    bad = []
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for key, chapters in data.items():
            spk, rate, chain = NARRATORS[key]
            for ch, lines in chapters:
                for i, line in enumerate(lines):
                    # Отбор: ключ фракции (xarn) или отдельная фраза (xarn_1_0)
                    if keys and key not in keys and f"{key}_{ch}_{i}" not in keys:
                        continue
                    raw = os.path.join(tmp, "raw.wav")
                    txt = voice_text(line)
                    if vosk:
                        synth.synth(txt, raw, speaker_id=spk, speech_rate=rate)
                    else:
                        voice, direction, chain = ELEVEN[key]
                        elevenlabs_tts.synth(txt, voice, raw, direction=direction, log=bad)
                    out = os.path.join(OUT, f"{key}_{ch}_{i}.ogg")
                    render(raw, chain, out)
                    print(f"{out}  ←  {txt}")
    if bad:
        print("\nНеточные дубли (лучший из попыток) — проверьте на слух:\n" + "\n".join(bad))


# ---------------------------------------------------------------- Кинематографичные звуки

SR = 44100


def write_wav(path, x):
    x = np.clip(x / max(1e-9, np.abs(x).max()) * 0.89, -1, 1)
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x.T.reshape(-1) * 32767).astype(np.int16).tobytes())


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc; y[i] = acc
    return y


def reverb(x, seconds=3.5, seed=1):
    rng = np.random.default_rng(seed)
    n = int(SR * seconds); t = np.arange(n) / SR
    out = []
    for ch in range(2):
        ir = rng.standard_normal(n) * np.exp(-t * 6.9 / seconds)
        ir[:int(SR * 0.012)] = 0
        ir = lowpass(ir, 5200)
        size = len(x) + n
        out.append(np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size) / np.sqrt(n) * 30)
    return np.array(out)


def impact():
    """Глубокий кинематографичный удар: суб-«бум» с падением высоты, плотный щелчок и длинный хвост зала."""
    n = int(SR * 2.4); t = np.arange(n) / SR
    f = 34 + 70 * np.exp(-t * 9)
    sub = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 2.2)
    rng = np.random.default_rng(7)
    noise = lowpass(rng.standard_normal(n), 900) * np.exp(-t * 18) * 1.6
    click = lowpass(rng.standard_normal(n), 3000) * np.exp(-t * 70) * 0.8
    dry = sub * 1.1 + noise + click
    wet = reverb(dry * 0.5, 3.8)
    out = np.zeros((2, wet.shape[1]))
    out[:, :n] += dry
    out += wet * 0.35
    fade = np.linspace(1, 0, int(SR * 0.6))
    out[:, -len(fade):] *= fade
    return out


def riser():
    """Нарастание перед финалом: шум и тон, разгоняющиеся к пику за 3 с (заканчивается в момент удара)."""
    n = int(SR * 3.2); t = np.arange(n) / SR
    env = (t / t[-1]) ** 2.6
    rng = np.random.default_rng(3)
    noise = rng.standard_normal(n)
    sweep = lowpass(noise, 1800) * 0.6 + noise * env * 0.25
    tone_f = 110 + 330 * (t / t[-1]) ** 2
    tone = np.sin(2 * np.pi * np.cumsum(tone_f) / SR) * 0.35 + np.sin(2 * np.pi * np.cumsum(tone_f * 1.5) / SR) * 0.18
    dry = (sweep + tone) * env
    wet = reverb(dry * 0.6, 2.2, seed=5)[:, :n]
    out = np.array([dry, dry]) * 0.7 + wet * 0.5
    out[:, -int(SR * 0.04):] *= np.linspace(1, 0, int(SR * 0.04))
    return out


def make_sfx():
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, fn in (("sfx_impact", impact), ("sfx_riser", riser)):
            wav = os.path.join(tmp, name + ".wav")
            write_wav(wav, fn())
            out = os.path.join(OUT, name + ".ogg")
            run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "6", out])
            print(out)


def main():
    args = sys.argv[1:]
    if "--stress" in args:
        stress_report()
        return
    if "--sfx" in args:
        make_sfx()
        return
    vosk = "--vosk" in args
    args = [a for a in args if a != "--vosk"]
    synth_all(set(args), vosk)
    if not args:
        make_sfx()


if __name__ == "__main__":
    main()
