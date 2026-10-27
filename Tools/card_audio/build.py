"""
Звуки карточного боя: нарезка и сведение из CC0-библиотек (см. README.md рядом).

    python3 Tools/card_audio/build.py <папка_с_исходниками> Assets/Resources/Audio/Sfx

Каждый звук — несколько вариантов «Имя_N.ogg» (моно, 44.1 кГц); имя совпадает со значением Sfx,
SFXManager подхватывает записи сам. Сведение «как в больших играх»: у выстрела — сам выстрел и хвост
отражений, у тяжёлого оружия — добавочный суббас-толчок, у взрыва — треск, тело, суббас, обломки и зал,
у карты — бумага плюс глухой удар о стол. Всё нормируется по пику, мягко ограничивается и обрезается тишиной.
"""
import os, sys, glob, subprocess, uuid
import numpy as np

SR = 44100
RNG = np.random.default_rng(7)


# ------------------------------------------------------------------ ввод-вывод

def load(path, rate=1.0):
    """Файл → моно float32 44.1 кГц; rate < 1 — ниже и длиннее (как замедленная запись)."""
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    x = np.frombuffer(raw, dtype=np.float32).astype(np.float64)
    if rate != 1.0:
        n = int(len(x) / rate)
        x = np.interp(np.arange(n) * rate, np.arange(len(x)), x)
    return x


def save(path, x, q=5):
    x = np.asarray(x, dtype=np.float32)
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-f', 'f32le', '-ar', str(SR), '-ac', '1', '-i', '-',
                    '-c:a', 'libvorbis', '-q:a', str(q), path], input=x.tobytes(), check=True)


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
    quality: 0.8
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


# ------------------------------------------------------------------ обработка

def env(x, win=0.002):
    n = max(1, int(win * SR))
    return np.sqrt(np.convolve(x * x, np.ones(n) / n, mode='same'))


def onsets(x, thr_db=-24, gap=0.04):
    e = env(x)
    thr = e.max() * 10 ** (thr_db / 20)
    above = e > thr
    idx = np.flatnonzero(above[1:] & ~above[:-1]) + 1
    out, last = [], -1e9
    for i in idx:
        if i - last > gap * SR:
            out.append(i)
            last = i
    return out


def cut(x, t0, dur, pre=0.004):
    a = max(0, int((t0 - pre) * SR))
    return x[a:a + int(dur * SR)].copy()


def fade(x, fin=0.002, fout=0.25):
    x = x.copy()
    n = len(x)
    i = min(n, int(fin * SR))
    if i > 0: x[:i] *= np.linspace(0, 1, i)
    o = min(n, int(fout * n)) if fout < 1 else min(n, int(fout * SR))
    if o > 0: x[-o:] *= np.linspace(1, 0, o) ** 2
    return x


def trim_tail(x, db=-60):
    e = env(x, 0.01)
    keep = np.flatnonzero(e > e.max() * 10 ** (db / 20))
    if len(keep) == 0: return x
    end = min(len(x), keep[-1] + int(0.02 * SR))
    return fade(x[:end], 0.001, 0.08 if end > 0.2 * SR else 0.3)


def norm(x, peak_db=-1.0):
    p = np.abs(x).max()
    return x if p < 1e-9 else x * (10 ** (peak_db / 20) / p)


def soft(x, drive=1.6):
    """Мягкое ограничение: громче и плотнее без цифрового клиппинга."""
    return np.tanh(x * drive) / np.tanh(drive)


def mix(*parts):
    """mix((сигнал, громкость, задержка_с), ...)"""
    n = max(int(d * SR) + len(s) for s, g, d in parts)
    out = np.zeros(n)
    for s, g, d in parts:
        i = int(d * SR)
        out[i:i + len(s)] += s * g
    return out


def lowpass(x, hz):
    """Однополюсный ФНЧ (дважды — крутизна 12 дБ/окт)."""
    a = np.exp(-2 * np.pi * hz / SR)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i in range(len(x)):
            acc = (1 - a) * x[i] + a * acc
            y[i] = acc
        x = y
    return x


def highpass(x, hz):
    return x - lowpass(x, hz)


def sub(f0=90.0, f1=38.0, dur=0.45, decay=0.16):
    """Суббас-толчок: синус с падающей частотой и быстрым затуханием — «удар в грудь» у тяжёлого оружия."""
    t = np.arange(int(dur * SR)) / SR
    f = f1 + (f0 - f1) * np.exp(-t / 0.05)
    ph = 2 * np.pi * np.cumsum(f) / SR
    e = np.exp(-t / decay) * np.minimum(1, t / 0.002)
    return np.sin(ph) * e


def noise_band(dur, centers, width=0.35, color=0.0):
    """Шум с движущейся полосой (свист, «вжух»): centers(t∈0..1) → Гц. STFT-маска, перекрытие-сложение."""
    n = int(dur * SR)
    x = RNG.standard_normal(n + 2048)
    N, H = 2048, 512
    win = np.hanning(N)
    out = np.zeros(n + 2048)
    freqs = np.fft.rfftfreq(N, 1 / SR)
    for s in range(0, n, H):
        fr = np.fft.rfft(x[s:s + N] * win)
        c = centers(min(1.0, s / max(1, n)))
        lf = np.log(np.maximum(freqs, 20) / c)
        m = np.exp(-0.5 * (lf / width) ** 2)
        if color: m *= (np.maximum(freqs, 20) / 1000.0) ** (-color)
        out[s:s + N] += np.fft.irfft(fr * m) * win
    return out[:n] / (np.abs(out).max() + 1e-9)


def reverb(x, wet=0.18, length=1.2, damp=3500.0, predelay=0.012):
    """Зал: свёртка с затухающим шумом (ИХ), приглушённым по верхам."""
    t = np.arange(int(length * SR)) / SR
    ir = RNG.standard_normal(len(t)) * np.exp(-t / (length / 6.5))
    ir = lowpass(ir, damp)
    ir = np.concatenate([np.zeros(int(predelay * SR)), ir])
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    n = len(x) + len(ir)
    nf = 1 << (n - 1).bit_length()
    y = np.fft.irfft(np.fft.rfft(x, nf) * np.fft.rfft(ir, nf), nf)[:n]
    dry = np.concatenate([x, np.zeros(len(ir))])
    return dry + wet * y / (np.abs(y).max() + 1e-9) * np.abs(x).max()


# ------------------------------------------------------------------ сборка звуков

def build(src, out):
    os.makedirs(out, exist_ok=True)
    K = lambda p: os.path.join(src, p)
    FA = K('firearm/Prepared SFX Library/')
    made = []

    def emit(name, variants, peak=-1.0, q=5):
        for old in glob.glob(os.path.join(out, name + '_*.ogg')):
            os.remove(old)
        for i, v in enumerate(variants):
            p = os.path.join(out, f'{name}_{i}.ogg')
            save(p, norm(trim_tail(v), peak), q)
            meta = p + '.meta'
            if not os.path.exists(meta):
                with open(meta, 'w') as f:
                    f.write(META.format(guid=uuid.uuid4().hex))
            made.append(p)
        print(f'{name}: {len(variants)}')

    def shots(path, idx, dur, rate=1.0):
        x = load(FA + path, rate)
        o = onsets(x)
        return [cut(x, o[i] / SR, dur) for i in idx if i < len(o)]

    # --- оружие: одиночные винтовочные выстрелы (АК одиночными, AR-15, СКС)
    rifle = shots('AK-47/C_28P.wav', [0, 1, 2, 3], 1.0) + shots('AR-15/D_32P.wav', [0, 1], 1.0) + shots('SKS/U_14P.wav', [0, 2, 4], 1.0)
    emit('GunRifle', [soft(fade(norm(s), 0.001, 0.45), 1.3) for s in rifle])

    # --- очереди: 3–5 патронов с хвостом (АК, «Карл Густав», ППШ)
    def burst(path, start_idx, n, tail=0.7):
        x = load(FA + path)
        o = onsets(x)
        if start_idx + n - 1 >= len(o): return None
        t0, t1 = o[start_idx] / SR, o[start_idx + n - 1] / SR
        return fade(cut(x, t0, t1 - t0 + tail), 0.001, 0.35)
    bursts = [burst('AK-47/C_29P.wav', 0, 4), burst('AK-47/C_29P.wav', 4, 4), burst('AK-47/C_27P.wav', 0, 5),
              burst('Carl Gustav M45/G_33P.wav', 0, 4), burst('Carl Gustav M45/G_33P.wav', 4, 3), burst('PPSh/P_16P.wav', 0, 5)]
    emit('GunAuto', [soft(norm(b), 1.3) for b in bursts if b is not None])

    # --- снайпер: мощный одиночный (Мосин, .300 Blackout, Марлин) — длинный хвост эха
    sn = shots('Mosin Nagant/M_21P.wav', [0, 1, 2, 3], 1.6) + shots('Savage 10 .300 Blackout/T_27P.wav', [0, 1], 1.6)
    emit('GunSniper', [soft(fade(mix((norm(s), 1.0, 0), (sub(110, 50, 0.3, 0.07), 0.35, 0)), 0.001, 0.5), 1.4) for s in sn])

    # --- тяжёлое орудие (танк, турель): дробовик на октаву ниже + суббас + треск + зал
    heavy = []
    for path, i in [('Mossberg/N_30P.wav', 0), ('Mossberg/N_30P.wav', 1), ('Nova/O_21P.wav', 0), ('Nova/O_21P.wav', 1)]:
        x = load(FA + path, rate=0.62)
        o = onsets(x)
        if i >= len(o): continue
        s = norm(cut(x, o[i] / SR, 1.8))
        crack = norm(load(K(f'sci-fi-sounds/Audio/explosionCrunch_00{i % 5}.ogg')))
        heavy.append(soft(reverb(fade(mix((s, 1.0, 0), (sub(95, 34, 0.7, 0.22), 0.8, 0), (highpass(crack, 900), 0.25, 0.005)), 0.001, 0.45), 0.16, 1.4), 1.6))
    emit('GunHeavy', heavy)

    # --- миномёт / гаубица (навесной): глухой «бумп» и свист снаряда
    mort = []
    for path, i in [('Mossberg/N_26P.wav', 0), ('Model 12/K_22P.wav', 0), ('Nova/O_17P.wav', 0)]:
        x = load(FA + path, rate=0.45)
        o = onsets(x)
        s = lowpass(norm(cut(x, o[i] / SR, 1.4)), 2400)
        mort.append(soft(reverb(fade(mix((s, 1.0, 0), (sub(70, 30, 0.6, 0.2), 0.9, 0)), 0.001, 0.5), 0.2, 1.6), 1.5))
    emit('GunMortar', mort)
    whistle = []
    for k in range(3):
        f0, f1 = 2300 - k * 200, 900 - k * 80
        w = noise_band(0.62, lambda t, f0=f0, f1=f1: f0 * (f1 / f0) ** t, 0.08)
        tt = np.linspace(0, 1, len(w))
        whistle.append(fade(w * (0.25 + 0.75 * tt), 0.08, 0.12))
    emit('ShellWhistle', whistle, peak=-8.0)

    # --- лазер / плазма (Kenney sci-fi): луч + низкий «тук» отдачи
    las = [mix((norm(load(K(f'sci-fi-sounds/Audio/laserLarge_00{i}.ogg'))), 1.0, 0), (sub(140, 60, 0.25, 0.05), 0.4, 0)) for i in range(5)]
    emit('GunLaser', [soft(reverb(fade(l, 0.001, 0.3), 0.14, 0.9), 1.3) for l in las])

    # --- огнемёт: рёв форсунки с атакой и хвостом
    fl = []
    for i in range(3):
        x = norm(load(K(f'sci-fi-sounds/Audio/thrusterFire_00{i}.ogg')))
        s = x[int(0.3 * SR):int(1.3 * SR)]
        hiss = noise_band(1.0, lambda t: 2500.0, 0.9)
        fl.append(fade(mix((s, 1.0, 0), (hiss, 0.12, 0)), 0.03, 0.35))
    emit('GunFlame', fl, peak=-2.0)

    # --- взрывы: треск + тело + суббас + обломки + зал
    def boom(i, big):
        body = norm(load(K(f'sci-fi-sounds/Audio/lowFrequency_explosion_00{i % 2}.ogg'), 0.8 if big else 1.0))
        crack = norm(load(K(f'sci-fi-sounds/Audio/explosionCrunch_00{(i * 2) % 5}.ogg'), 0.9 if big else 1.1))
        alt = norm(load(K('oga_100-CC0-SFX_0/explosion.ogg') if i % 3 == 0 else K(f'oga_sci-fi-sfx/explosion_0{1 + i % 2}.ogg'), 0.85 if big else 1.0))
        debris = []
        for j in range(4 if big else 2):
            d = norm(load(K(f'impact-sounds/Audio/impactMining_00{(i + j) % 5}.ogg'), 0.9 + 0.2 * RNG.random()))
            debris.append((highpass(d, 600), 0.12, 0.25 + 0.18 * j + 0.05 * RNG.random()))
        parts = [(crack, 0.9, 0), (body, 1.0, 0.005), (alt, 0.45, 0.01), (sub(80, 28, 1.0 if big else 0.6, 0.35 if big else 0.18), 1.0 if big else 0.6, 0)] + debris
        return soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.25 if big else 0.15, 2.0 if big else 1.2), 1.8)
    emit('Boom', [boom(i, True) for i in range(4)])
    emit('BoomSmall', [boom(i, False) for i in range(5)])

    # --- попадания
    emit('ImpactMetal', [soft(mix((norm(load(K(f'impact-sounds/Audio/impactMetal_heavy_00{i}.ogg'))), 0.8, 0),
                                   (norm(load(K(f'impact-sounds/Audio/impactPlate_heavy_00{i}.ogg'))), 0.6, 0),
                                   (sub(160, 70, 0.2, 0.04), 0.35, 0)), 1.4) for i in range(5)])
    emit('ImpactFlesh', [soft(mix((norm(load(K(f'impact-sounds/Audio/impactPunch_heavy_00{i}.ogg'))), 0.9, 0),
                                   (norm(load(K(f'impact-sounds/Audio/impactSoft_heavy_00{i}.ogg'))), 0.6, 0.004)), 1.3) for i in range(5)])
    emit('ImpactShield', [reverb(norm(load(K(f'sci-fi-sounds/Audio/forceField_00{i}.ogg'))), 0.2, 1.0) for i in range(5)])

    # --- гибель юнита: тяжёлый удар, лязг, осыпающиеся обломки
    down = []
    for i in range(4):
        hit = norm(load(K(f'impact-sounds/Audio/impactMetal_heavy_00{(i + 2) % 5}.ogg'), 0.8))
        clang = norm(load(K(f'impact-sounds/Audio/impactBell_heavy_00{i}.ogg'), 0.7))
        parts = [(hit, 1.0, 0), (clang, 0.25, 0.02), (sub(70, 30, 0.6, 0.2), 0.7, 0)]
        for j in range(3):
            parts.append((highpass(norm(load(K(f'impact-sounds/Audio/impactMining_00{(i + j) % 5}.ogg'), 1.1)), 800), 0.18, 0.18 + 0.15 * j))
        down.append(soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.18, 1.4), 1.5))
    emit('UnitDown', down)

    # --- карты: бумага + удар о стол
    C = lambda n: norm(load(K(f'casino-audio/Audio/{n}.ogg')))
    emit('CardDraw', [C(f'card-slide-{i}') for i in (1, 3, 5, 7)], peak=-4.0)
    emit('CardHover', [highpass(C(f'card-slide-{i}')[:int(0.14 * SR)], 1500) for i in (2, 4, 6)], peak=-12.0)
    emit('CardPick', [C(f'card-shove-{i}') for i in (1, 2, 3, 4)], peak=-5.0)
    emit('CardPlace', [mix((C(f'card-place-{i}'), 1.0, 0), (norm(load(K(f'impact-sounds/Audio/impactSoft_medium_00{i}.ogg'))), 0.55, 0.002))
                       for i in (1, 2, 3, 4)], peak=-3.0)
    slam = []
    for i in range(4):
        slam.append(soft(reverb(mix((C(f'card-place-{1 + i % 4}'), 0.7, 0),
                                    (norm(load(K(f'impact-sounds/Audio/impactPunch_heavy_00{i}.ogg'))), 0.9, 0.003),
                                    (norm(load(K(f'impact-sounds/Audio/impactMetal_light_00{i}.ogg'))), 0.25, 0.01),
                                    (sub(85, 36, 0.55, 0.18), 0.9, 0)), 0.15, 1.1), 1.5))
    emit('CardSlam', slam)
    flip = []
    for i in range(3):
        wh = noise_band(0.32, lambda t, i=i: 600 * (5.0 + i) ** t, 0.5, 0.3)
        tt = np.linspace(0, 1, len(wh))
        wh *= np.sin(np.pi * tt) ** 1.5
        flip.append(mix((C(f'card-fan-{1 + i % 2}'), 0.6, 0), (wh, 0.5, 0)))
    emit('CardFlip', flip, peak=-5.0)
    order = []
    for i in range(3):
        rise = noise_band(0.5, lambda t, i=i: 400 * (8.0 + i) ** t, 0.25)
        tt = np.linspace(0, 1, len(rise))
        rise *= tt ** 2
        ping = norm(load(K(f'interface-sounds/Audio/maximize_00{3 + i}.ogg')))
        field = norm(load(K(f'sci-fi-sounds/Audio/forceField_00{i}.ogg')))
        order.append(reverb(mix((rise, 0.35, 0), (ping, 0.7, 0.45), (field, 0.35, 0.45)), 0.22, 1.2))
    emit('OrderCast', order, peak=-3.0)
    whoosh = []
    for i in range(3):
        wh = noise_band(0.7, lambda t, i=i: 250 * (10.0 + 3 * i) ** np.sin(np.pi * t), 0.45, 0.4)
        tt = np.linspace(0, 1, len(wh))
        whoosh.append(wh * np.sin(np.pi * tt) ** 2)
    emit('Whoosh', whoosh, peak=-4.0)
    print('files:', len(made))


if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2])
