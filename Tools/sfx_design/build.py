"""
Сборка звуков стратегического слоя в Assets/Resources/Audio/Sfx/<Имя>_N.ogg (SFXManager подхватывает их сам).

    python3 Tools/sfx_design/build.py                 # все звуки
    python3 Tools/sfx_design/build.py UiClick Anomaly # только указанные
    python3 Tools/sfx_design/build.py --preview DIR   # только wav в DIR, без записи в игру

Громкость: каждый звук выравнивается по максимальной моментальной громкости (BS.1770, окно 400 мс)
к задуманной громкости «в игре» (DESIGN) с учётом SfxDef.Volume. Общий уровень привязан к среднему
прежних звуков (reference_lufs.json — снят один раз с того, что звучало в игре), чтобы не сбить баланс с музыкой.
Нужны numpy, scipy и ffmpeg с libvorbis.
"""
import json
import zlib
import os
import subprocess
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp  # noqa: E402
from sounds import SOUNDS  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Audio", "Sfx")
REF = os.path.join(HERE, "reference_lufs.json")

# Мастеринг по категориям: насыщение (плотность), верхняя граница, «склейка»
MASTER = {
    "ui": dict(drive=1.05, lp=17500, glue=False),
    "note": dict(drive=1.1, lp=17000, glue=True),
    "world": dict(drive=1.3, lp=17000, glue=True),
    "sting": dict(drive=1.2, lp=16500, glue=True),
}
# Сколько дБ можно «срезать» ограничителем ради громкости (оружию — плотность, щелчкам — сохранить атаку)
# и ширина стерео (звуки мира панорамируются игрой — слишком широкая стереобаза им мешает)
HEADROOM = {"ui": 3.5, "note": 2.5, "world": 6.0, "sting": 3.0}
WIDTH = {"ui": 0.85, "note": 1.0, "world": 0.6, "sting": 1.0}
# Громкость «в игре» (LUFS, макс. моментальная, с учётом SfxDef.Volume) — продуманный баланс:
# касания тише всего, щелчки и вкладки — тихо, окна и приказы — чуть громче, события — заметно, стингеры — крупно.
DESIGN = {
    "UiHover": -38, "TextTick": -39, "MapHover": -37,
    "UiClick": -32, "UiTab": -32, "OrderQueue": -32, "TimeSpeed": -31, "BuildQueued": -30,
    "UiConfirm": -29, "UiBack": -30, "WindowOpen": -29, "WindowClose": -30, "PanelSlide": -30, "HoloScan": -30,
    "DealPlace": -29, "DealRemove": -30, "DealAccepted": -26, "AlertPop": -27, "AlertDismiss": -31,
    "UiDenied": -28, "UiDanger": -26, "TimePause": -29, "TimeResume": -29,
    "SystemSelect": -29, "SelectMilitary": -28, "SelectScience": -29, "SelectConstructor": -28,
    "OrderMove": -28, "OrderStop": -29, "ResearchStart": -29,
    "NotifyInfo": -26, "NotifySuccess": -24, "NotifyWarning": -24, "NotifyDanger": -22, "RedAlert": -20,
    "Bankrupt": -21, "BudgetWarning": -25, "AlertUrgent": -22,
    "SurveyComplete": -24, "OutpostBuilt": -24, "Anomaly": -23, "BuildComplete": -25, "ResearchComplete": -22,
    "SystemEnter": -27, "SystemExit": -28, "FtlJump": -24, "FtlArrive": -25, "ShipLaunched": -24,
    "WeaponKinetic": -25, "WeaponEnergy": -25, "WeaponMissile": -25, "HitShield": -26, "HitArmor": -26,
    "ExplosionSmall": -23, "ExplosionLarge": -20, "StarbaseDown": -17, "Disengage": -25, "FtlFail": -23,
    "ColonyFounded": -20, "BattleStart": -18, "BattleVictory": -19, "BattleDefeat": -19, "WarDeclared": -17,
    "PeaceSigned": -20, "PactSigned": -21, "SiegeStart": -19, "SystemCaptured": -19, "SystemLost": -19,
    "GameVictory": -18, "GameDefeat": -18, "GameStart": -18,
}


def def_volumes():
    """SfxDef.Volume из SfxLibrary.cs: S(Sfx.Имя, громкость, …)."""
    import re
    src = open(os.path.join(ROOT, "Assets", "Scripts", "Core", "SfxLibrary.cs"), encoding="utf-8").read()
    return {m.group(1): float(m.group(2).rstrip("f")) for m in re.finditer(r"S\(Sfx\.(\w+),\s*([0-9.]+f?)", src)}


def targets(ref):
    """Громкость файла: DESIGN − громкость в SfxDef; общий сдвиг — чтобы среднее совпало с прежним (баланс с музыкой)."""
    vol = def_volumes()
    g = {n: 20 * np.log10(max(1e-3, vol.get(n, 0.5))) for n in SOUNDS}
    old_ingame = [ref[n] + g[n] for n in SOUNDS if n in ref]
    design = [DESIGN[n] for n in SOUNDS if n in ref]
    shift = float(np.mean(old_ingame) - np.mean(design))
    return {n: DESIGN[n] + shift - g[n] for n in SOUNDS}, shift


def read_audio(path):
    r = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(dsp.SR), "-"],
                       capture_output=True, check=True)
    return np.frombuffer(r.stdout, dtype=np.float32).reshape(-1, 2).T.astype(float)


def write_ogg(path, x):
    with tempfile.NamedTemporaryFile(suffix=".f32", delete=False) as f:
        f.write(np.ascontiguousarray(x.T, dtype=np.float32).tobytes())
        tmp = f.name
    try:
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(dsp.SR), "-ac", "2", "-i", tmp,
                        "-c:a", "libvorbis", "-q:a", "7", path], check=True)
    finally:
        os.unlink(tmp)


def write_wav(path, x):
    import wave
    y = (np.clip(x, -1, 1) * 32767).astype(np.int16).T.tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(dsp.SR); w.writeframes(y)


def reference(old_synth_dir=None):
    """Громкость прежних звуков: запись из игры, иначе рендер прежнего синтеза (old_synth_dir/<Имя>.wav)."""
    if os.path.exists(REF):
        return json.load(open(REF))
    ref = {}
    for name in SOUNDS:
        rec = os.path.join(OUT, name + "_0.ogg")
        src = rec if os.path.exists(rec) else (os.path.join(old_synth_dir, name + ".wav") if old_synth_dir else None)
        if src and os.path.exists(src):
            ref[name] = round(dsp.loudness_max(read_audio(src)), 2)
    json.dump(ref, open(REF, "w"), ensure_ascii=False, indent=1, sort_keys=True)
    return ref


def render(name, v):
    fn, _, cat = SOUNDS[name]
    rng = dsp.Rng(zlib.crc32(name.encode()) % 100000 * 31 + v * 7919 + 11)
    x = fn(rng)
    return dsp.master(x, **MASTER[cat])


def main(argv):
    preview = None
    if "--preview" in argv:
        i = argv.index("--preview"); preview = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
        os.makedirs(preview, exist_ok=True)
    old = None
    if "--old" in argv:
        i = argv.index("--old"); old = argv[i + 1]; argv = argv[:i] + argv[i + 2:]
    names = argv or list(SOUNDS)
    ref = reference(old)
    tg, shift = targets(ref)
    print(f"общий сдвиг к прежнему уровню: {shift:+.1f} дБ")
    for name in names:
        _, variants, cat = SOUNDS[name]
        target = tg[name]
        for v in range(variants):
            x = render(name, v)
            w = WIDTH[cat]
            if w != 1.0:
                m, sd = (x[0] + x[1]) * 0.5, (x[0] - x[1]) * 0.5 * w
                x = np.vstack([m + sd, m - sd])
            # два прохода: усиление к цели → ограничитель (не больше HEADROOM дБ срезки) → уточнение
            for _ in range(2):
                need = target - dsp.loudness_max(x)
                x = x * dsp.db(need)
                pk = 20 * np.log10(np.abs(x).max() + 1e-12)
                over = pk - (-0.8)
                if over > 0:
                    allow = min(over, HEADROOM[cat])
                    x = dsp.limit(x * dsp.db(-(over - allow)), -0.8, 0.04)
            pk = np.abs(x).max()
            if pk > dsp.db(-0.7):
                x *= dsp.db(-0.8) / pk
            if preview:
                write_wav(os.path.join(preview, f"{name}_{v}.wav"), x)
            else:
                write_ogg(os.path.join(OUT, f"{name}_{v}.ogg"), x)
        # лишние старые варианты этого имени убираем
        if not preview:
            k = variants
            while os.path.exists(os.path.join(OUT, f"{name}_{k}.ogg")):
                os.remove(os.path.join(OUT, f"{name}_{k}.ogg"))
                meta = os.path.join(OUT, f"{name}_{k}.ogg.meta")
                if os.path.exists(meta):
                    os.remove(meta)
                k += 1
        print(f"{name:18s} ×{variants}  {target:6.1f} LUFS", flush=True)


if __name__ == "__main__":
    main(sys.argv[1:])
