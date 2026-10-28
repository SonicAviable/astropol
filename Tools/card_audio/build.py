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

def tools(src, out):
    """Общие помощники сборки: пути к исходникам, загрузка с кэшем, запись вариантов «Имя_N.ogg»."""
    os.makedirs(out, exist_ok=True)
    K = lambda p: os.path.join(src, p)
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

    def piece(path, k, dur, rate=1.0, thr=-20, gap=0.25):
        """k-е событие записи (по атакам), длительность dur."""
        x = L(path, rate)
        ev = [i / SR for i in onsets(x, thr, gap)]
        return norm(fade(cut(x, ev[k % len(ev)], dur), 0.001, 0.3))

    return K, BSB, L, emit, piece, made


def build_hits(src, out):
    """
    Урон и гибель карт. Без лёгкого «жестяного» металла (кастрюли, банки, тонкие пластины звенят высоко
    и звучат игрушечно): вес даёт тело — короткие низкие взрывы и хлопки ниже по высоте, суббас, грунт.
    Металл только тяжёлый (стальные ворота, полосы) — на полоктавы-октаву ниже и под ФНЧ, чтобы не звенел.
    """
    K, BSB, L, emit, piece, made = tools(src, out)
    rng = np.random.default_rng(11)
    R = lambda lst, i: lst[i % len(lst)]
    bangs = [norm(L(K(f'oga_bang/{n}.ogg'))) for n in ('cannon_01', 'cannon_02', 'cannon_03', 'cannon_04', 'cannon_05', 'bang_01', 'bang_03', 'bang_06', 'bang_10')]
    thumps = [norm(L(BSB(n), r)) for n in ('1807', '1808') for r in (0.62, 0.75, 0.9)]      # короткие низкие взрывы
    gate = [piece(BSB(n), k, 0.7, 0.6) for n in ('2358', '2359', '2360') for k in range(3)]  # стальные ворота, ниже
    stones = [highpass(piece(BSB('1022'), k, 0.9), 350) for k in range(10)]
    dirt = [highpass(piece(BSB('1305'), k, 0.4), 120) for k in range(12)]                  # лопата в грунт
    gore = [lowpass(piece(BSB(n), k, 0.35), 3000) for n in ('2593', '2594', '2595', '2596') for k in range(2)]
    bones = [lowpass(piece(BSB(n), k, 0.4), 4000) for n in ('1408', '1409') for k in range(2)]
    steel = [norm(lowpass(cut(L(BSB(n), 0.7), 0.0, 1.6), 2200)) for n in ('1771', '1772', '1774', '1776', '1778', '0235')]
    thunder = L(BSB('3115'))
    rumble = [norm(lowpass(fade(cut(thunder, t, 2.0), 0.03, 0.6), 900)) for t in (1.35, 2.05, 3.35, 3.65)]

    # --- попадание в технику/сооружение: глухой удар по броне — хлопок, низкий взрыв, тяжёлая сталь, суббас, крошка
    metal = []
    for i in range(10):
        crack = norm(lowpass(cut(R(bangs, i), 0.0, 0.25), 5000))
        parts = [(crack, 0.55, 0), (lowpass(R(thumps, i), 2500), 0.9, 0.002), (lowpass(R(gate, i * 2), 1800), 0.45, 0.004),
                 (sub(130 - 6 * (i % 4), 45, 0.35, 0.07), 0.7, 0), (R(stones, i), 0.12 + 0.05 * rng.random(), 0.04 + 0.03 * rng.random())]
        metal.append(soft(reverb(fade(mix(*parts), 0.001, 0.35), 0.1, 0.8, 2500), 1.5))
    emit('ImpactMetal', metal)

    # --- попадание в пехоту: глухой удар в грунт/тело — земля, приглушённый влажный шлепок, низкий толчок
    flesh = []
    for i in range(10):
        thud = norm(lowpass(cut(R(bangs, i + 3), 0.0, 0.2), 1400))
        parts = [(R(dirt, i), 0.8, 0), (thud, 0.55, 0.001), (R(gore, i), 0.25, 0.006),
                 (sub(110, 50, 0.25, 0.05), 0.6, 0), (lowpass(R(thumps, i + 1), 1500), 0.35, 0.002)]
        flesh.append(soft(fade(mix(*parts), 0.001, 0.35), 1.4))
    emit('ImpactFlesh', flesh)

    # --- гибель: тяжёлый обвал — низкий взрыв, сталь ворот на октаву ниже, оседающие обломки и грунт, раскат, суббас
    down = []
    for i in range(6):
        parts = [(lowpass(R(thumps, i), 2000), 0.9, 0), (lowpass(R(gate, i * 2 + 1), 1500), 0.55, 0.02),
                 (R(steel, i), 0.25, 0.12), (R(rumble, i), 0.4, 0.04), (sub(75, 28, 0.8, 0.25), 0.85, 0),
                 (R(bones, i), 0.18, 0.03)]
        for j in range(3):
            parts.append((R(stones, i * 3 + j), 0.2, 0.18 + 0.17 * j + 0.05 * rng.random()))
            parts.append((R(dirt, i * 2 + j), 0.22, 0.1 + 0.2 * j))
        down.append(soft(reverb(fade(mix(*parts), 0.001, 0.4), 0.14, 1.4, 2500), 1.5))
    emit('UnitDown', down)
    return made


def build_fx(src, out):
    """
    Приказы и эффекты. Без «аркадных» пиликалок: настоящие инструменты и предметы, переработанные как в кино —
    гонги и тибетские чаши ниже по высоте (удар и «магический» звон), тарелки задом наперёд (нарастающий подвод
    к удару), стекло, разряды, огонь, пар; плюс суббас и зал.
    """
    K, BSB, L, emit, piece, made = tools(src, out)
    R = lambda lst, i: lst[i % len(lst)]
    rev = lambda x: x[::-1].copy()

    def hit(n, k=0, dur=2.0, rate=1.0, thr=-20, gap=0.6):
        """k-й удар записи (по атакам)."""
        return piece(BSB(n), k, dur, rate, thr, gap)

    def seg(n, t0, dur, rate=1.0, fin=0.02, fout=0.3):
        """Отрезок сплошной записи (гул, пар, треск) с началом t0 по кругу длины записи."""
        x = L(BSB(n), rate)
        t0 = t0 % max(0.01, len(x) / SR - dur - 0.05)
        return norm(fade(cut(x, t0, dur, 0), fin, fout))

    def swell(x, dur):
        """Удар задом наперёд: звон нарастает и «втягивается» в конце — подвод к удару длиной dur."""
        y = rev(fade(x[:int(dur * SR)], 0.001, 0.05))
        return fade(y, 0.02, 0.004)

    def bend(x, r0, r1):
        """Плавное изменение высоты по ходу звука (r — скорость воспроизведения: <1 — ниже)."""
        m = int(len(x) / ((r0 + r1) / 2))
        pos = np.cumsum(np.linspace(r0, r1, m))
        pos = pos[pos < len(x) - 1]
        return np.interp(pos, np.arange(len(x)), x)

    def finish(parts, wet=0.2, length=1.6, damp=4000, drive=1.3, fout=0.35):
        return soft(reverb(fade(mix(*parts), 0.002, fout), wet, length, damp), drive)

    gongS = ['1484', '1485']                      # гонг сильный
    bowls = ['2553', '2554', '2555', '1110']     # тибетская чаша, удар
    crash = ['2311', '2316']                      # тарелка 16"
    finger = ['2274', '2275']                     # тарелочки
    chimes = ['2082', '2084']                     # подвесные колокольчики
    whoosh = ['1796', '1798', '0128']

    # --- приказ: подвод тарелкой задом наперёд → удар гонга ниже по высоте, суббас, звон чаши, «вжух» к удару
    cast = []
    for i in range(4):
        sw = highpass(swell(hit(R(crash, i), i, 1.0, 0.9 + 0.08 * (i % 2)), 0.5), 500)
        t0 = len(sw) / SR
        parts = [(sw, 0.55, 0),
                 (norm(highpass(hit(R(whoosh, i), i, 0.5), 200)), 0.3, max(0.0, t0 - 0.3)),
                 (lowpass(hit(R(gongS, i), i, 2.2, 0.72 + 0.04 * i), 2600), 0.85, t0),
                 (hit(R(bowls, i), i, 2.4, 0.86 + 0.05 * i), 0.4, t0 + 0.01),
                 (sub(88, 34, 0.8, 0.22), 0.8, t0)]
        cast.append(finish(parts, 0.26, 2.0))
    emit('FxCast', cast)

    # --- показ карты крупным планом: тяжёлый удар — гонг на октаву ниже под ФНЧ, суббас, низкая чаша, шипение тарелки
    show = []
    for i in range(4):
        parts = [(lowpass(hit(R(gongS, i + 1), i, 1.6, 0.5 + 0.04 * i), 1400), 1.0, 0),
                 (sub(72, 26, 1.0, 0.3), 0.9, 0),
                 (hit(R(bowls, i + 2), i, 1.8, 0.74), 0.22, 0.01),
                 (highpass(hit('2323', i, 1.2, 0.85), 2500), 0.12, 0.0)]
        show.append(finish(parts, 0.22, 1.8, 3000, 1.4))
    emit('FxShowcase', show)

    # --- «ВАШ ХОД», «РАСКРЫТИЕ»: короткий подвод, две чаши созвучием, тарелочки, лёгкий толчок
    sting = []
    for i in range(2):
        sw = highpass(swell(hit(R(crash, i + 1), i, 0.6, 1.1), 0.18), 1200)
        t0 = len(sw) / SR
        parts = [(sw, 0.45, 0),
                 (hit('2554', i, 2.0, 1.0), 0.6, t0), (hit('2553', i, 2.0, 1.5), 0.4, t0 + 0.005),
                 (hit(R(finger, i), i, 1.2, 1.0), 0.3, t0), (sub(110, 50, 0.35, 0.1), 0.4, t0)]
        sting.append(finish(parts, 0.3, 1.8))
    emit('FxStinger', sting)

    # --- усиление: звон чаши задом наперёд (нарастает) → тарелочки, колокольчики и чаша выше по высоте
    buff = []
    for i in range(6):
        # не выше чем на треть октавы: выше чаши и тарелочки звенят игрушечно
        r = 1.08 + 0.06 * (i % 3)
        sw = highpass(swell(hit(R(bowls, i), i, 1.0, r), 0.3), 300)
        t0 = len(sw) / SR
        parts = [(sw, 0.6, 0), (lowpass(hit(R(finger, i), i, 1.0, 0.85 + 0.05 * (i % 2)), 7000), 0.35, t0),
                 (lowpass(highpass(hit(R(chimes, i), i, 1.2, 0.9), 1500), 7000), 0.18, t0 + 0.01),
                 (hit(R(bowls, i + 1), i, 1.2, r), 0.45, t0), (sub(120, 60, 0.3, 0.08), 0.25, t0)]
        buff.append(finish(parts, 0.2, 1.4, 5000, 1.2))
    emit('FxBuff', buff)

    # --- ремонт брони: поющая чаша выше по высоте с мягким нарастанием, россыпь колокольчиков и звон стекла
    heal = []
    for i in range(6):
        parts = [(seg('1109', 0.7 * i, 1.1, 1.15 + 0.05 * (i % 3), 0.22, 0.5), 0.6, 0),
                 (lowpass(highpass(hit(R(chimes, i), i, 1.0, 0.95), 2000), 8000), 0.22, 0.04),
                 (hit('1335', i, 0.8, 1.15), 0.22, 0.05)]
        heal.append(finish(parts, 0.2, 1.4, 5000, 1.2))
    emit('FxHeal', heal)

    # --- щит: гул трансформатора нарастает, подвод тарелкой, удар низкой чаши и короткий разряд
    shield = []
    for i in range(4):
        sw = highpass(swell(hit(R(crash, i), i + 2, 0.6, 1.2), 0.32), 1500)
        t0 = len(sw) / SR
        parts = [(seg('2118', 0.9 * i, 0.95, 1.0, 0.2, 0.45), 0.45, 0), (sw, 0.4, 0),
                 (hit('2555', i, 1.6, 0.8 + 0.04 * i), 0.5, t0), (fade(hit('0134', i, 0.15, 1.4), 0.001, 0.5), 0.2, t0)]
        shield.append(finish(parts, 0.22, 1.4))
    emit('FxShieldUp', shield)

    # --- ослабление / подавление: мягкий гонг, «уходящий» вниз по высоте, втянутый «вжух», низкий толчок
    debuff = []
    for i in range(6):
        g = lowpass(bend(hit('1482', i, 1.3, 0.85 + 0.04 * (i % 3)), 1.0, 0.7), 2400)
        parts = [(norm(g), 0.8, 0.05), (lowpass(rev(hit(R(whoosh, i), i, 0.4)), 1500), 0.35, 0),
                 (sub(64, 28, 0.5, 0.16), 0.35, 0.05)]
        debuff.append(finish(parts, 0.2, 1.4, 3000))
    emit('FxDebuff', debuff)

    # --- щит разбит: взрыв стекла и зеркала, ёлочный шар ниже по высоте, разряд, суббас
    shatter = []
    for i in range(4):
        # стекло на полтона-тон ниже и без самых верхов — тяжелее и не режет ухо
        parts = [(lowpass(hit('0148', i, 1.2, 0.9), 9000), 0.8, 0), (lowpass(hit('0387', i, 1.2, 0.8), 8000), 0.6, 0.01),
                 (hit('1121', i, 0.9, 0.7), 0.4, 0.03),
                 (hit('0134', i, 0.45), 0.5, 0), (sub(92, 34, 0.5, 0.15), 0.7, 0)]
        shatter.append(finish(parts, 0.16, 1.4, 5000, 1.4))
    emit('FxShatter', shatter)

    # --- карта сгорает: «вуп» бензинового пламени и треск того же огня, когда он разгорелся
    burn = []
    for i in range(6):
        # «вуп» — только первая вспышка записи (дальше одиночные щелчки, по пику они дают слишком тихий звук)
        parts = [(hit('1340', 0, 0.9, 0.84 + 0.05 * i, -18, 0.3), 0.9, 0),
                 (lowpass(highpass(seg('1340', 2.0 + 1.1 * i, 0.9), 1500), 9000), 0.3, 0.05)]
        burn.append(finish(parts, 0.1, 0.8, 4000, 1.6, 0.5))
    emit('FxBurn', burn)

    # --- псионика: поющая чаша на октаву ниже двумя близкими голосами (биения), чаша задом наперёд, гул
    psi = []
    for i in range(4):
        a = seg('1109', 1.0 * i, 1.7, 0.5, 0.3, 0.5)
        b = seg('1109', 1.0 * i + 0.4, 1.7, 0.515, 0.3, 0.5)
        sw = swell(hit(R(bowls, i), i, 1.6, 0.6), 1.2)
        parts = [(a, 0.5, 0), (b, 0.45, 0), (sw, 0.5, 0.2), (seg('0064', 0.8 * i, 1.6, 0.8, 0.3, 0.5), 0.2, 0)]
        psi.append(finish(parts, 0.35, 2.4, 3500))
    emit('FxPsi', psi)

    # --- споры: шипение пара нарастает, бульканье ниже по высоте, приглушённо
    spores = []
    for i in range(3):
        parts = [(lowpass(seg('0227', 1.5 * i, 1.4, 0.9, 0.35, 0.5), 3500), 0.6, 0),
                 (lowpass(seg('1074', 0.8 * i, 1.2, 0.6, 0.2, 0.5), 2000), 0.5, 0.1),
                 (lowpass(hit(R(whoosh, i), i, 0.6, 0.8), 1500), 0.3, 0)]
        spores.append(finish(parts, 0.2, 1.4, 3000, 1.2, 0.4))
    emit('FxSpores', spores)

    # --- сектор взят: созвучие двух чаш, мягкий гонг, тарелка и толчок — светлый «аккорд победы»
    cap = []
    for i in range(2):
        parts = [(hit('2553', i, 2.4, 1.0), 0.55, 0), (hit('2554', i, 2.4, 1.335), 0.45, 0.01),
                 (hit('1482', i, 2.0, 1.0), 0.35, 0), (highpass(hit(R(crash, i), i, 1.6), 2000), 0.22, 0),
                 (sub(100, 40, 0.5, 0.14), 0.5, 0)]
        cap.append(finish(parts, 0.3, 2.0))
    emit('FxCapture', cap)

    # --- сектор потерян: тяжёлый гонг на октаву ниже под ФНЧ, низкая чаша, длинный суббас
    lost = []
    for i in range(2):
        parts = [(lowpass(hit(R(gongS, i), i + 1, 2.2, 0.55), 1500), 1.0, 0), (hit('2555', i, 2.0, 0.6), 0.3, 0.02),
                 (sub(62, 24, 1.1, 0.35), 0.85, 0)]
        lost.append(finish(parts, 0.3, 2.2, 2500, 1.4))
    emit('FxLost', lost)
    return made


def build(src, out):
    """
    Только настоящие записи: никакой синтетики в бою. Взрыв = реальный хлопок (пушечный салют, дробовик
    на октаву ниже) + раскат (запись грома и дальнего взрыва) + суббас + осыпающиеся камни и металл;
    выстрел = запись оружия + хвост отражений + звон гильз; приказ = треск рации.
    """
    K, BSB, L, emit, piece, made = tools(src, out)
    FA = K('firearm/Prepared SFX Library/')

    def events(x, thr=-20, gap=0.25):
        return [i / SR for i in onsets(x, thr, gap)]

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

    # --- попадания и гибель — build_hits; здесь только щит (электрический треск)
    hum = L(BSB('0064'))
    emit('ImpactShield', [soft(reverb(mix((R(fence, i), 0.9, 0), (R(zaps, i + 2), 0.6, 0.02),
                                           (fade(norm(cut(hum, 1.0 + i, 0.9)), 0.05, 0.6), 0.35, 0)), 0.2, 1.0), 1.3) for i in range(5)])

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
    made += build_hits(src, out)
    print('files:', len(made) + 1)


if __name__ == '__main__':
    # третий аргумент «hits» — пересобрать только урон и гибель (ImpactMetal, ImpactFlesh, UnitDown)
    {'hits': build_hits, 'fx': build_fx}.get((sys.argv[3:] or [''])[0], build)(sys.argv[1], sys.argv[2])
