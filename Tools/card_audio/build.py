"""
Звуки карточного боя: нарезка и сведение из CC0-записей (см. README.md рядом).

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
    """
    Только настоящие записи: никакой синтетики в бою. Взрыв = реальный хлопок (пушечный салют, дробовик
    на октаву ниже) + раскат (запись грома и дальнего взрыва) + суббас + осыпающиеся камни и металл;
    выстрел = запись оружия + хвост отражений + звон гильз; приказ = треск рации.
    """
    os.makedirs(out, exist_ok=True)
    K = lambda p: os.path.join(src, p)
    FA = K('firearm/Prepared SFX Library/')
    BSB = lambda n: K('bsb/' + n + ('.flac' if os.path.exists(K('bsb/' + n + '.flac')) else '.wav'))
    made = []
    cache = {}

    def L(path, rate=1.0):
        key = (path, rate)
        if key not in cache: cache[key] = load(path, rate)
        return cache[key]

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

    def events(x, thr=-20, gap=0.25):
        return [i / SR for i in onsets(x, thr, gap)]

    def piece(path, k, dur, rate=1.0, thr=-20, gap=0.25):
        """k-е событие записи (по атакам), длительность dur."""
        x = L(path, rate)
        ev = events(x, thr, gap)
        return norm(fade(cut(x, ev[k % len(ev)], dur), 0.001, 0.3))

    def shots(path, idx, dur, rate=1.0):
        x = L(FA + path, rate)
        o = onsets(x)
        return [norm(cut(x, o[i] / SR, dur)) for i in idx if i < len(o)]

    casings556 = [norm(L(BSB(str(i)))) for i in range(1361, 1367)]
    casings762 = [norm(L(BSB(str(i)))) for i in range(1367, 1372)]
    thunder = L(BSB('3115'))
    rumble = [norm(lowpass(fade(cut(thunder, t, 2.6), 0.03, 0.6), 1400)) for t in (1.35, 2.05, 3.35, 3.65)]
    farboom = [norm(L(BSB('1023'))), norm(L(BSB('1806')))]
    stones = [piece(BSB('1022'), k, 0.9) for k in range(10)]
    steel = [norm(L(BSB(n))) for n in ('1771', '1772', '1774', '1776', '1778', '0235')]
    bangs = [norm(L(K(f'oga_bang/{n}.ogg'))) for n in ('cannon_01', 'cannon_02', 'cannon_03', 'cannon_04', 'cannon_05', 'bang_01', 'bang_03', 'bang_06', 'bang_10')]
    R = lambda lst, i: lst[i % len(lst)]

    # --- винтовка: одиночный выстрел + отражения + звон гильзы о бетон
    rifle = shots('AK-47/C_28P.wav', [0, 1, 2, 3], 1.0) + shots('AR-15/D_32P.wav', [0, 1], 1.0) + shots('SKS/U_14P.wav', [0, 1, 2, 3, 4], 1.0)
    rifle += shots('Tikka/W_29P.wav', [0, 1], 1.0) + shots('Model 1894/L_23P.wav', [0, 1], 1.0) + shots('Arisaka/E_25P.wav', [0, 1], 1.0)
    rifle += shots('Marlin 336/I_22P.wav', [0, 1], 1.0) + shots('1917/B_24P.wav', [0, 1], 1.0)
    rifle += [norm(L(BSB('2853'))), norm(L(BSB('2854')))]
    emit('GunRifle', [soft(mix((fade(s, 0.001, 0.45), 1.0, 0), (R(casings556 + casings762, i), 0.06 + 0.06 * RNG.random(), 0.28 + 0.12 * RNG.random())), 1.3)
                      for i, s in enumerate(rifle)])

    # --- очередь: 3–5 патронов + россыпь гильз
    def burst(path, start_idx, n, tail=0.7):
        x = L(FA + path)
        o = onsets(x)
        if start_idx + n - 1 >= len(o): return None
        t0, t1 = o[start_idx] / SR, o[start_idx + n - 1] / SR
        return norm(fade(cut(x, t0, t1 - t0 + tail), 0.001, 0.35)), (t1 - t0) / max(1, n - 1), n
    raw = [burst('AK-47/C_29P.wav', 0, 4), burst('AK-47/C_29P.wav', 4, 4), burst('AK-47/C_27P.wav', 0, 5),
           burst('Carl Gustav M45/G_33P.wav', 0, 4), burst('Carl Gustav M45/G_33P.wav', 4, 3), burst('PPSh/P_16P.wav', 0, 5),
           burst('AK-47/C_27P.wav', 10, 5), burst('PPSh/P_34P.wav', 0, 6), burst('PPSh/P_34P.wav', 9, 5),
           burst('Carl Gustav M45/G_35P.wav', 0, 4), burst('Carl Gustav M45/G_35P.wav', 10, 5), burst('PPSh/P_32P.wav', 2, 4)]
    autos = []
    for i, b in enumerate([r for r in raw if r is not None]):
        sig, step, n = b
        parts = [(sig, 1.0, 0)]
        for j in range(n):
            parts.append((R(casings762, i + j), 0.08, 0.3 + j * step + 0.03 * RNG.random()))
        autos.append(soft(mix(*parts), 1.3))
    emit('GunAuto', autos)

    # --- снайпер: тяжёлый выстрел, долгое эхо, затем щелчок затвора
    sn = shots('Mosin Nagant/M_21P.wav', [0, 1, 2, 3], 1.6) + shots('Savage 10 .300 Blackout/T_27P.wav', [0, 1], 1.6)
    sn.append(norm(cut(L(BSB('0397')), 0.98, 1.8)))
    emit('GunSniper', [soft(mix((fade(s, 0.001, 0.5), 1.0, 0), (sub(110, 50, 0.3, 0.07), 0.3, 0),
                                (norm(L(BSB(str(1988 + i % 4)))), 0.16, 0.75), (R(casings762, i), 0.07, 1.05)), 1.4)
                       for i, s in enumerate(sn)])

    # --- танковое орудие / турель: пушечный хлопок + дробовик на октаву ниже + суббас + раскат + лязг
    heavy = []
    for i, (path, k) in enumerate([('Mossberg/N_30P.wav', 0), ('Mossberg/N_30P.wav', 1), ('Nova/O_21P.wav', 0), ('Nova/O_21P.wav', 1)]):
        x = L(FA + path, rate=0.55)
        o = onsets(x)
        body = norm(cut(x, o[k] / SR, 1.6))
        bang = norm(lowpass(R(bangs, i), 5000))
        parts = [(bang, 0.8, 0), (body, 0.9, 0.002), (sub(90, 32, 0.8, 0.25), 0.85, 0),
                 (R(rumble, i), 0.35, 0.05), (highpass(R(steel, i), 300), 0.12, 0.08)]
        heavy.append(soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.12, 1.6), 1.6))
    emit('GunHeavy', heavy)

    # --- миномёт / гаубица: глухой выстрел вдалеке
    mort = []
    for i in range(4):
        b = norm(lowpass(R(bangs, i + 2), 1600))
        mort.append(soft(reverb(fade(mix((b, 1.0, 0), (sub(70, 30, 0.6, 0.2), 0.8, 0), (R(rumble, i + 1), 0.3, 0.06)), 0.001, 0.5), 0.18, 1.8), 1.5))
    emit('GunMortar', mort)

    # --- снаряд на подлёте: настоящий «вжух» воздуха, ниже и с нарастанием (без мультяшного свиста)
    rush = []
    for i, n in enumerate(('1795', '1796', '1800', '1801')):
        w = norm(L(BSB(n), 0.55))
        tt = np.linspace(0, 1, len(w))
        rush.append(fade(lowpass(w, 3500) * (0.3 + 0.7 * tt ** 1.5), 0.05, 0.1))
    emit('ShellWhistle', rush, peak=-6.0)

    # --- энергооружие (плазма/рельса): сухой треск выстрела + электрический разряд
    zap_src = L(BSB('0134'))
    zaps = [norm(fade(cut(zap_src, t, 0.5), 0.001, 0.5)) for t in events(zap_src, -10, 0.3)[:6]]
    fence = [piece(BSB('1252'), k, 0.6) for k in range(6)]
    las = []
    for i in range(5):
        crack = highpass(R(rifle, i + 3), 1200)
        las.append(soft(reverb(mix((crack, 0.7, 0), (R(zaps, i), 0.8, 0.003), (R(fence, i), 0.35, 0.01), (sub(150, 60, 0.2, 0.05), 0.35, 0)), 0.12, 0.9), 1.4))
    emit('GunLaser', las)
    emit('ElecZap', [soft(mix((R(zaps, i), 1.0, 0), (R(fence, i + 2), 0.5, 0.05)), 1.3) for i in range(5)])

    # --- огнемёт: «вуп» воспламенения бензина + рёв горелки
    fl = []
    torch = L(BSB('0930'))
    for i, (n, t) in enumerate((('1341', 0.25), ('1340', 0.25), ('1340', 1.65))):
        ign = norm(cut(L(BSB(n)), t, 1.1))
        roar = norm(cut(torch, 2.85 + i * 0.4, 1.1))
        fl.append(fade(mix((ign, 1.0, 0), (roar, 0.55, 0.05)), 0.01, 0.35))
    emit('GunFlame', fl, peak=-2.0)

    # --- взрывы: хлопок ниже на кварту-октаву + дальний взрыв + раскат + суббас + камни и металл
    def boom(i, big):
        bang = norm(R(bangs, i) if not big else lowpass(R(bangs, i), 3000))
        body = norm(L(FA + ('Mossberg/N_30P.wav' if i % 2 else 'Nova/O_21P.wav'), (0.38 + 0.04 * (i % 3)) if big else (0.55 + 0.05 * (i % 3))))
        o = onsets(body)
        body = norm(cut(body, o[0] / SR, 1.8))
        parts = [(bang, 0.85, 0), (body, 0.8, 0.002), (R(farboom, i), 0.5 if big else 0.3, 0.01),
                 (R(rumble, i), 0.75 if big else 0.4, 0.06), (sub(75, 26, 1.1 if big else 0.6, 0.38 if big else 0.18), 1.0 if big else 0.6, 0)]
        for j in range(4 if big else 2):
            parts.append((highpass(R(stones, i * 3 + j), 250), 0.22 if big else 0.15, 0.3 + 0.22 * j + 0.06 * RNG.random()))
        if big: parts.append((highpass(R(steel, i), 400), 0.12, 0.45))
        return soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.22 if big else 0.14, 2.2 if big else 1.3), 1.8)
    emit('Boom', [boom(i, True) for i in range(8)])
    emit('BoomSmall', [boom(i + 2, False) for i in range(9)])
    emit('BoomFar', [norm(lowpass(mix((R(farboom, i), 1.0, 0), (R(rumble, i), 0.8, 0.05)), 900 - 100 * i)) for i in range(4)], peak=-3.0)

    # --- попадания: металл (стальные полосы, лом), пехота (удар + земля), щит (электрический треск)
    emit('ImpactMetal', [soft(mix((norm(cut(R(steel, i), 0.05, 0.6)), 0.8, 0),
                                   (norm(L(K(f'impact-sounds/Audio/impactMetal_heavy_00{(i * 3) % 5}.ogg'), 0.9 + 0.05 * (i % 4))), 0.55, 0),
                                   (norm(L(K(f'impact-sounds/Audio/impactPlate_heavy_00{i % 5}.ogg'))), 0.3 if i >= 5 else 0.0, 0.003),
                                   (sub(160, 70, 0.2, 0.04), 0.3, 0)), 1.4) for i in range(10)])
    emit('ImpactFlesh', [soft(mix((norm(L(K(f'impact-sounds/Audio/impactPunch_{"heavy" if i < 5 else "medium"}_00{i % 5}.ogg'))), 0.9, 0),
                                   (norm(L(K(f'impact-sounds/Audio/impactSoft_heavy_00{(i * 2) % 5}.ogg'))), 0.35, 0.002),
                                   (highpass(R(stones, i), 400), 0.35, 0.004)), 1.3) for i in range(10)])
    hum = L(BSB('0064'))
    emit('ImpactShield', [soft(reverb(mix((R(fence, i), 0.9, 0), (R(zaps, i + 2), 0.6, 0.02),
                                           (fade(norm(cut(hum, 1.0 + i, 0.9)), 0.05, 0.6), 0.35, 0)), 0.2, 1.0), 1.3) for i in range(5)])

    # --- гибель: тяжёлый удар, падающий металл, осыпь камней
    down = []
    for i in range(5):
        parts = [(norm(L(K(f'impact-sounds/Audio/impactMetal_heavy_00{(i + 2) % 5}.ogg'), 0.8)), 0.8, 0),
                 (R(steel, i + 1), 0.6, 0.03), (sub(70, 30, 0.6, 0.2), 0.7, 0)]
        for j in range(3):
            parts.append((highpass(R(stones, i * 2 + j), 300), 0.25, 0.2 + 0.18 * j))
        down.append(soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.14, 1.4), 1.5))
    emit('UnitDown', down)

    # --- юнит готов к бою: затвор / заряжание; техника: дизель; сооружение/дрон: пневматика и сервопривод
    emit('GunCock', [norm(L(BSB(str(n)))) for n in (1984, 1985, 1986, 1987, 1988, 1989, 1990, 1991)], peak=-3.0)
    eng = []
    for i, (n, t) in enumerate((('0967', 1.4), ('0967', 3.0), ('1145', 0.75), ('1145', 2.4))):
        e = norm(cut(L(BSB(n)), t, 1.6))
        eng.append(fade(e, 0.08, 0.5))
    emit('EngineRev', eng, peak=-3.0)
    srv = []
    for i in range(4):
        srv.append(mix((norm(L(BSB(str(1491 + i % 3)))), 0.9, 0), (norm(L(BSB(('2911', '2919', '2929')[i % 3]))), 0.5, 0.05)))
    emit('Servo', srv, peak=-3.0)

    # --- рация: короткий треск эфира (захват сектора, начало хода) — 10 разных кусков эфира
    sweep = L(BSB('2618'))
    radio = []
    for i, (n, t, d) in enumerate((('0313', 0.15, 0.9), ('0313', 1.95, 0.9), ('0313', 0.75, 0.7), ('0313', 2.9, 0.8),
                                   ('0312', 2.5, 1.0), ('0312', 3.3, 0.9), ('0312', 0.2, 0.8), ('0312', 1.3, 0.9),
                                   ('2618', 1.2, 0.7), ('2618', 3.5, 0.8))):
        r = highpass(lowpass(norm(cut(L(BSB(n)), t, d)), 3400 + 400 * (i % 3)), 300 + 50 * (i % 2))
        radio.append(fade(r, 0.005, 0.3))
    emit('Radio', radio, peak=-4.0)

    # --- карта на стол = приказ уходит по рации. Двенадцать разных передач: обычная с «роджер-бипом»,
    #     без тонов (двойной щелчок тангенты), цифровой пакет данных, сильные помехи с пропаданием,
    #     двойное «пи-пи» подтверждения, обрывок свипа частот. Всё — через полосу рации с перегрузом.
    def radio_chain(x, lo=300, hi=3400, drive=2.5):
        return highpass(lowpass(soft(x, drive), hi), lo)

    def tone(f, dur, amp=1.0):
        t = np.arange(int(dur * SR)) / SR
        e = np.minimum(1, t / 0.004) * np.minimum(1, (dur - t) / 0.006)
        return np.sin(2 * np.pi * f * t) * e * amp

    def hiss(dur, decay):
        t = np.arange(int(dur * SR)) / SR
        return RNG.standard_normal(len(t)) * np.exp(-t / decay)

    def click():
        return highpass(hiss(0.012, 0.002), 2000)

    statics = [norm(cut(L(BSB(n)), t, d)) for n, t, d in (('0313', 0.15, 0.45), ('0313', 1.95, 0.45), ('0312', 2.5, 0.5), ('0312', 3.3, 0.45),
                                                          ('0313', 0.75, 0.6), ('0312', 1.3, 0.55))]
    order_tx = []
    for i in range(12):
        kind = i % 6
        k = 0.92 + 0.16 * RNG.random()                 # разброс высоты тонов — разные станции
        st = R(statics, i + (i // 6) * 3)              # вторая дюжина — другие куски эфира
        if i >= 6: st = st[::-1].copy() if i % 2 else st * (0.6 + 0.4 * np.abs(np.sin(np.linspace(0, 9, len(st)))))
        parts = [(click(), 0.6, 0), (hiss(0.06 + 0.06 * RNG.random(), 0.03), 0.35, 0.01)]
        if kind == 0:     # обычная передача: эфир, «рукопожатие» линии, «роджер-бип»
            parts += [(st * np.linspace(1, 0.6, len(st)), 0.45, 0.03), (tone(1250 * k, 0.045), 0.32, 0.10),
                      (tone(1870 * k, 0.045), 0.32, 0.15), (tone(2400 * k, 0.07), 0.28, 0.40), (hiss(0.16, 0.05), 0.4, 0.47)]
        elif kind == 1:   # без тонов: эфир и двойной щелчок тангенты в конце
            parts += [(st, 0.55, 0.03), (click(), 0.55, 0.42), (click(), 0.45, 0.48), (hiss(0.12, 0.04), 0.35, 0.5)]
        elif kind == 2:   # цифровой пакет: быстрая морзянка двух частот
            seq = [1200 * k if RNG.random() < 0.5 else 2100 * k for _ in range(9)]
            for j, f in enumerate(seq): parts.append((tone(f, 0.028), 0.26, 0.08 + j * 0.031))
            parts += [(st[:int(0.25 * SR)], 0.3, 0.38), (hiss(0.14, 0.04), 0.35, 0.6)]
        elif kind == 3:   # сильные помехи: эфир рвётся, в конце низкий бип
            gate = (np.sin(np.linspace(0, 40 * np.pi * (0.8 + 0.4 * RNG.random()), len(st))) > -0.2).astype(float)
            parts += [(st * gate, 0.7, 0.03), (hiss(0.3, 0.12), 0.25, 0.05), (tone(900 * k, 0.09), 0.3, 0.45), (hiss(0.14, 0.04), 0.4, 0.56)]
        elif kind == 4:   # подтверждение «пи-пи»
            parts += [(st[:int(0.3 * SR)], 0.35, 0.03), (tone(2000 * k, 0.05), 0.32, 0.30), (tone(2000 * k, 0.05), 0.32, 0.38),
                      (hiss(0.12, 0.04), 0.35, 0.46)]
        else:             # обрывок свипа частот и короткий бип
            sw = highpass(lowpass(norm(cut(sweep, 0.6 + RNG.random() * 6.0, 0.35)), 3800), 300)
            parts += [(sw, 0.5, 0.03), (tone(1600 * k, 0.06), 0.3, 0.38), (hiss(0.14, 0.04), 0.35, 0.46)]
        lo, hi = (300, 3400) if i % 3 else (450, 2800)  # иногда рация «уже» и хрипит сильнее
        order_tx.append(fade(radio_chain(mix(*parts), lo, hi, 2.0 + 1.5 * RNG.random()), 0.001, 0.15))
    emit('RadioOrder', order_tx, peak=-3.0)
    cancel = []
    for i in range(6):
        k = 0.92 + 0.16 * RNG.random()
        parts = [(click(), 0.6, 0), (hiss(0.08, 0.025), 0.35, 0.01)]
        if i % 2 == 0: parts += [(tone(1870 * k, 0.05), 0.3, 0.07), (tone(1250 * k, 0.07), 0.3, 0.13)]
        else: parts += [(tone(1100 * k, 0.1), 0.3, 0.08)]
        parts.append((hiss(0.14, 0.04), 0.4, 0.22))
        cancel.append(fade(radio_chain(mix(*parts)), 0.001, 0.2))
    emit('RadioCancel', cancel, peak=-4.0)
    emit('OrderCast', [mix((R(radio, i), 0.9, 0), (highpass(lowpass(norm(cut(sweep, 0.85 + i * 1.3, 0.7)), 4000), 300), 0.45, 0.12),
                           (norm(L(BSB(str(1984 + i)))), 0.5, 0.65)) for i in range(4)], peak=-3.0)

    # --- карты: настоящие карты (Kenney Casino) и удар о стол; переворот — короткий «вжух» воздуха
    C = lambda n: norm(L(K(f'casino-audio/Audio/{n}.ogg')))
    W = [norm(L(BSB(n))) for n in ('1795', '1796', '1797', '1798', '1799', '1800', '1801', '1802')]
    emit('CardDraw', [C(f'card-slide-{i}') for i in range(1, 9)], peak=-4.0)
    emit('CardHover', [highpass(C(f'card-slide-{i}')[:int(0.14 * SR)], 1500) for i in (2, 4, 6)], peak=-12.0)
    emit('CardPick', [C(f'card-shove-{i}') for i in (1, 2, 3, 4)], peak=-5.0)
    emit('CardPlace', [mix((C(f'card-place-{1 + i % 4}'), 1.0, 0), (norm(L(K(f'impact-sounds/Audio/impactSoft_medium_00{(i * 3) % 5}.ogg'))), 0.4 + 0.1 * (i % 3), 0.002))
                       for i in range(8)], peak=-3.0)
    emit('CardSlam', [soft(reverb(mix((C(f'card-place-{1 + i % 4}'), 0.7, 0),
                                      (norm(L(K(f'impact-sounds/Audio/impactPunch_heavy_00{i % 5}.ogg'))), 0.9, 0.003),
                                      (highpass(R(stones, i + 4), 400), 0.2, 0.01),
                                      (sub(85 + 6 * (i % 3), 36, 0.55, 0.18), 0.9, 0)), 0.12, 1.1), 1.5) for i in range(8)])
    emit('CardFlip', [mix((C(f'card-fan-{1 + i % 2}'), 0.7, 0), (highpass(R(W, i + 2), 500), 0.35, 0)) for i in range(6)], peak=-5.0)
    emit('Whoosh', [lowpass(R(W, i), 6000) for i in range(8)], peak=-4.0)

    # --- фон боя: ветер над полем (петля), поверх него код изредка пускает BoomFar
    amb_dir = os.path.join(os.path.dirname(out.rstrip('/')), 'CardBattle')
    os.makedirs(amb_dir, exist_ok=True)
    wind = L(BSB('0595'))
    n, xf = int(34 * SR), int(3 * SR)
    a = wind[int(5 * SR):int(5 * SR) + n + xf].copy()
    loop = a[:n].copy()
    ramp = np.linspace(0, 1, xf)
    loop[:xf] = a[:xf] * ramp + a[n:n + xf] * (1 - ramp)
    amb = os.path.join(amb_dir, 'battle_wind.ogg')
    save(amb, norm(lowpass(loop, 5000), -6.0), 3)
    if not os.path.exists(amb + '.meta'):
        with open(amb + '.meta', 'w') as f:
            f.write(META.replace('loadType: 0', 'loadType: 2').format(guid=uuid.uuid4().hex))
    print('files:', len(made) + 1)


if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2])
