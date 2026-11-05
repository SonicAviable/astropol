"""
Движок саунд-дизайна: слои, фильтры, пространство, сатурация, мастеринг.
Всё — numpy/scipy, 48 кГц, стерео (массив формы (2, n)).

Принципы «без мультяшности»:
  • тональные слои — неармонические моды (стекло, металл, пластина), а не чистые синусы с глиссандо;
  • атака — физическая: короткий шумовой транзиент + резонанс корпуса;
  • низ — суббас с падением высоты и мягким насыщением, а не «бум» синусом;
  • пространство — свёртка с синтетическими импульсами (комната, зал, космос), частотно-зависимое затухание;
  • мастеринг — мягкое насыщение, компрессия, ограничитель по пику с передискретизацией.
"""
import numpy as np
from scipy import signal

SR = 48000


class Rng:
    def __init__(self, seed):
        self.g = np.random.default_rng(seed)

    def u(self, a=0.0, b=1.0):
        return float(self.g.uniform(a, b))

    def j(self, x, amt):
        """Значение x с разбросом ±amt (доля)."""
        return x * (1.0 + self.g.uniform(-amt, amt))

    def white(self, n):
        return self.g.standard_normal(n)


def N(sec):
    return max(1, int(round(sec * SR)))


def buf(sec):
    return np.zeros((2, N(sec)))


def t_axis(n):
    return np.arange(n) / SR


# ============================== огибающие ==============================

def env_exp(n, attack, decay, curve=1.0):
    """Атака (линейно-синусная) и экспоненциальный спад: decay — время до −60 дБ."""
    t = t_axis(n)
    a = np.ones(n)
    if attack > 0:
        k = t < attack
        a[k] = np.sin(0.5 * np.pi * t[k] / attack) ** 2
    d = np.exp(-6.9 * np.maximum(0, t - attack) / max(1e-4, decay)) ** curve
    return a * d


def env_adsr(n, a, d, s, r, hold=None):
    t = t_axis(n)
    total = n / SR
    hold = total - r if hold is None else hold
    e = np.zeros(n)
    k = t < a
    e[k] = np.sin(0.5 * np.pi * t[k] / max(a, 1e-4)) ** 2
    k = (t >= a) & (t < a + d)
    e[k] = 1 - (1 - s) * ((t[k] - a) / max(d, 1e-4))
    k = (t >= a + d) & (t < hold)
    e[k] = s
    k = t >= hold
    e[k] = s * np.exp(-6.9 * (t[k] - hold) / max(r, 1e-4))
    return e


def fade(x, fin=0.002, fout=0.01):
    n = x.shape[-1]
    a, b = min(N(fin), n // 4), min(N(fout), n // 2)
    w = np.ones(n)
    if a > 1:
        w[:a] = np.linspace(0, 1, a) ** 2
    if b > 1:
        w[-b:] *= np.linspace(1, 0, b) ** 2
    return x * w


# ============================== фильтры ==============================

def _biquad(kind, f, q=0.707, gain_db=0.0):
    f = float(np.clip(f, 10, SR * 0.49))
    w = 2 * np.pi * f / SR
    cw, sw = np.cos(w), np.sin(w)
    al = sw / (2 * q)
    A = 10 ** (gain_db / 40)
    if kind == "lp":
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == "hp":
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == "bp":
        b = [al, 0, -al]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == "peak":
        b = [1 + al * A, -2 * cw, 1 - al * A]; a = [1 + al / A, -2 * cw, 1 - al / A]
    elif kind == "lowshelf":
        sa = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * cw + sa), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sa)]
        a = [(A + 1) + (A - 1) * cw + sa, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sa]
    elif kind == "highshelf":
        sa = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * cw + sa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sa)]
        a = [(A + 1) - (A - 1) * cw + sa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sa]
    else:
        raise ValueError(kind)
    b = np.array(b) / a[0]; a = np.array(a) / a[0]
    return np.concatenate([b, a])[None, :]


def filt(x, kind, f, q=0.707, gain_db=0.0, order=1):
    """Статический биквад (order — сколько каскадов) по последней оси."""
    sos = np.repeat(_biquad(kind, f, q, gain_db), order, axis=0)
    return signal.sosfilt(sos, x, axis=-1)


def sweep(x, kind, f0, f1, q=0.707, curve="exp", block=128, order=1, fq=None):
    """Фильтр с плавно меняющейся частотой (f0 → f1 по длине сигнала) или по кривой fq(t)."""
    x = np.atleast_2d(x)
    n = x.shape[-1]
    out = np.zeros_like(x)
    zi = np.zeros((order, x.shape[0], 2)) if x.shape[0] > 1 else np.zeros((order, 2))
    zi = np.zeros((x.shape[0], order, 2))
    for s in range(0, n, block):
        e = min(n, s + block)
        tt = (s + e) * 0.5 / n
        if fq is not None:
            f = fq(tt)
        elif curve == "exp":
            f = f0 * (f1 / f0) ** tt
        else:
            f = f0 + (f1 - f0) * tt
        sos = np.repeat(_biquad(kind, f, q), order, axis=0)
        for ch in range(x.shape[0]):
            out[ch, s:e], zi[ch] = signal.sosfilt(sos, x[ch, s:e], zi=zi[ch])
    return out


def dc(x):
    return filt(x, "hp", 18, 0.6)


# ============================== источники ==============================

def noise(rng, sec, color="white", stereo=True):
    n = N(sec)
    ch = 2 if stereo else 1
    w = np.stack([rng.white(n) for _ in range(ch)])
    if color == "pink":
        b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
        a = [1, -2.494956002, 2.017265875, -0.522189400]
        w = signal.lfilter(b, a, w, axis=-1) * 3.5
    elif color == "brown":
        w = signal.lfilter([1], [1, -0.995], w, axis=-1) * 0.08
    return w if stereo else w[0]


def osc(freq, n, wave="sine", phase=0.0, pw=0.5):
    """freq — число или массив длины n (Гц)."""
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    ph = phase + 2 * np.pi * np.cumsum(f) / SR
    if wave == "sine":
        return np.sin(ph)
    p = (ph / (2 * np.pi)) % 1.0
    if wave == "saw":
        # полиBLEP — без алиасинга
        y = 2 * p - 1
        dt = f / SR
        y -= _blep(p, dt)
        return y
    if wave == "tri":
        return 2 * np.abs(2 * p - 1) - 1
    if wave == "square":
        dt = f / SR
        y = np.where(p < pw, 1.0, -1.0)
        y += _blep(p, dt) - _blep((p + 1 - pw) % 1.0, dt)
        return y
    raise ValueError(wave)


def _blep(p, dt):
    y = np.zeros_like(p)
    k = p < dt
    t = p[k] / dt[k]
    y[k] = t + t - t * t - 1
    k = p > 1 - dt
    t = (p[k] - 1) / dt[k]
    y[k] = t * t + t + t + 1
    return y


def glide(f0, f1, n, curve="exp", shape=1.0):
    t = np.linspace(0, 1, n) ** shape
    return f0 * (f1 / f0) ** t if curve == "exp" else f0 + (f1 - f0) * t


# ============================== микширование ==============================

def add(dst, src, t0=0.0, gain=1.0, pan=0.0):
    """Положить src (моно или стерео) в dst с момента t0; pan −1..1 (равномощная панорама)."""
    s = N(t0)
    if s >= dst.shape[1]:
        return dst
    src = np.atleast_2d(src)
    if src.shape[0] == 1:
        th = (pan + 1) * np.pi / 4
        src = np.vstack([src[0] * np.cos(th), src[0] * np.sin(th)]) * np.sqrt(2)
    elif pan != 0.0:
        th = (pan + 1) * np.pi / 4
        src = np.vstack([src[0] * np.cos(th) * np.sqrt(2), src[1] * np.sin(th) * np.sqrt(2)])
    e = min(dst.shape[1], s + src.shape[1])
    dst[:, s:e] += src[:, : e - s] * gain
    return dst


def db(x):
    return 10 ** (x / 20)


def widen(x, amount=0.3, haas_ms=0.0):
    """M/S-расширение и (опционально) задержка Хааса правого канала."""
    m = (x[0] + x[1]) * 0.5
    s = (x[0] - x[1]) * 0.5 * (1 + amount)
    y = np.vstack([m + s, m - s])
    if haas_ms > 0:
        d = N(haas_ms / 1000)
        y[1] = np.concatenate([np.zeros(d), y[1][:-d]])
    return y


def decorrelate(mono, rng, amt=0.5):
    """Моно → стерео: два слегка разных фазовых всепропускающих каскада."""
    out = []
    for _ in range(2):
        y = mono.copy()
        for _k in range(3):
            f = rng.u(300, 4000)
            sos = _biquad("bp", f, rng.u(0.5, 2))
            y = y * (1 - amt * 0.3) + signal.sosfilt(sos, y) * amt * 0.6
        out.append(y)
    return np.vstack(out)


# ============================== насыщение и динамика ==============================

def sat(x, drive=1.5, asym=0.08):
    """Тёплое насыщение: tanh с лёгкой асимметрией (чётные гармоники), компенсация уровня."""
    y = np.tanh(drive * (x + asym)) - np.tanh(drive * asym)
    return y / np.tanh(drive)


def tape(x, drive=1.4):
    """«Плёнка»: подъём верха → насыщение → снятие подъёма, верх мягче, середина плотнее."""
    pre = filt(x, "highshelf", 3000, 0.7, 6)
    y = sat(pre, drive, 0.04)
    return filt(y, "highshelf", 3000, 0.7, -6.5)


def follower(x, attack, release):
    a = np.exp(-1 / (SR * attack)); r = np.exp(-1 / (SR * release))
    lvl = np.abs(x).max(axis=0)
    # быстрая аппроксимация: атака и спад двумя проходами однополюсника
    up = signal.lfilter([1 - a], [1, -a], lvl)
    env = np.maximum(up, 0)
    env = signal.lfilter([1 - r], [1, -r], env)
    return np.maximum(env, up)


def comp(x, thr_db=-18, ratio=3.0, attack=0.004, release=0.12, makeup_db=0.0):
    env = follower(x, attack, release) + 1e-9
    lvl = 20 * np.log10(env)
    over = np.maximum(0, lvl - thr_db)
    g = db(-over * (1 - 1 / ratio) + makeup_db)
    return x * g


def limit(x, ceiling_db=-1.0, release=0.06):
    """Ограничитель по истинному пику (передискретизация ×4), без перегруза."""
    up = signal.resample_poly(x, 4, 1, axis=-1)
    peak = np.abs(up).max(axis=0)
    peak = peak.reshape(-1, 4).max(axis=1)[: x.shape[1]] if peak.size >= x.shape[1] * 4 else np.abs(x).max(axis=0)
    c = db(ceiling_db)
    need = np.minimum(1.0, c / np.maximum(peak, 1e-9))
    # заглядывание вперёд 1.5 мс и плавный возврат
    la = N(0.0015)
    need = np.minimum.accumulate(np.concatenate([need[la:], np.ones(la)])[::-1])[::-1] if False else _lookahead_min(need, la)
    r = np.exp(-1 / (SR * release))
    g = np.empty_like(need)
    cur = 1.0
    for i in range(len(need)):
        v = need[i]
        cur = v if v < cur else v + (cur - v) * r
        g[i] = cur
    return x * g


def _lookahead_min(v, la):
    from scipy.ndimage import minimum_filter1d
    m = minimum_filter1d(v, size=2 * la + 1, mode="nearest")
    return m


# ============================== громкость (BS.1770, упрощённо) ==============================

def _kweight(x):
    pre = np.concatenate([_biquad("highshelf", 1681, 0.707, 4.0)])
    y = signal.sosfilt(pre, x, axis=-1)
    return signal.sosfilt(_biquad("hp", 38, 0.5), y, axis=-1)


def loudness_max(x, win=None):
    """Максимальная моментальная громкость, LUFS. Окно — по длине звука (50–400 мс): у щелчка в 0.1 с
    окно 400 мс занизило бы громкость на 6 дБ, хотя на слух он не тише."""
    y = _kweight(np.atleast_2d(x))
    p = (y ** 2).sum(axis=0)
    if win is None:
        # длина «звучащей» части: где уровень выше −20 дБ от пика (хвост реверберации не в счёт)
        lvl = np.abs(np.atleast_2d(x)).max(axis=0)
        on = np.where(lvl > lvl.max() * 0.1)[0]
        dur = (on[-1] - on[0]) / SR if on.size else 0.4
        win = float(np.clip(dur * 0.6, 0.05, 0.4))
    w = N(win)
    if p.size < w:
        p = np.concatenate([p, np.zeros(w - p.size)])
    c = np.convolve(p, np.ones(w) / w, mode="valid")
    return -0.691 + 10 * np.log10(c.max() + 1e-12)


# ============================== пространство ==============================

_IR = {}


def make_ir(kind, seed=7):
    """Синтетический импульс: ранние отражения + диффузный хвост с частотно-зависимым затуханием."""
    if (kind, seed) in _IR:
        return _IR[(kind, seed)]
    P = {
        #          длина, RT60 низ, RT60 верх, пред-задержка, ранние, яркость
        "booth": (0.35, 0.22, 0.10, 0.003, 6, 9000),
        "room":  (0.9, 0.55, 0.28, 0.006, 10, 8000),
        "plate": (1.8, 1.5, 0.9, 0.004, 0, 11000),
        "hall":  (3.2, 2.6, 1.3, 0.018, 14, 7000),
        "space": (5.5, 4.8, 1.8, 0.035, 8, 5000),
    }[kind]
    length, rt_lo, rt_hi, pre, er, bright = P
    rng = Rng(seed)
    n = N(length)
    t = t_axis(n)
    ir = np.zeros((2, n))
    # хвост: шум, разбитый на полосы с разным затуханием
    bands = [(20, 300, rt_lo), (300, 2000, (rt_lo + rt_hi) / 2), (2000, 20000, rt_hi)]
    for lo, hi, rt in bands:
        nz = noise(rng, length)
        nz = filt(filt(nz, "hp", lo, 0.7), "lp", min(hi, bright), 0.7)
        ir += nz * np.exp(-6.9 * t / rt)
    # мягкое нарастание диффузии
    ir *= 1 - np.exp(-t / 0.012)
    # ранние отражения
    for k in range(er):
        d = pre + rng.u(0.004, 0.06)
        g = rng.u(0.25, 0.7) * (1 - k / max(1, er)) * 0.9
        i = N(d)
        if i < n:
            ir[0, i] += g * rng.u(0.6, 1.0)
            ir[1, min(n - 1, i + N(rng.u(0, 0.004)))] += g * rng.u(0.6, 1.0)
    p = N(pre)
    ir = np.concatenate([np.zeros((2, p)), ir], axis=1)[:, :n]
    ir /= np.sqrt((ir ** 2).sum() / 2) + 1e-9
    _IR[(kind, seed)] = ir
    return ir


def verb(x, kind="room", wet=0.25, dry=1.0, hp=180, lp=None, tail=1.0):
    """Свёрточная реверберация: возвращает сигнал длиннее входного на хвост."""
    x = np.atleast_2d(x)
    if x.shape[0] == 1:
        x = np.vstack([x[0], x[0]])
    ir = make_ir(kind)
    if tail < 1.0:
        ir = ir[:, : N(ir.shape[1] / SR * tail)]
    src = filt(x, "hp", hp, 0.7) if hp else x
    if lp:
        src = filt(src, "lp", lp, 0.7)
    wl = signal.fftconvolve(src[0], ir[0])
    wr = signal.fftconvolve(src[1], ir[1])
    w = np.vstack([wl, wr]) * 0.18
    out = np.zeros_like(w)
    out[:, : x.shape[1]] += x * dry
    out += w * wet
    return out


def trim(x, thr_db=-70, pad=0.02):
    lvl = np.abs(x).max(axis=0)
    idx = np.where(lvl > db(thr_db))[0]
    if idx.size == 0:
        return x[:, : N(0.05)]
    end = min(x.shape[1], idx[-1] + N(pad))
    return x[:, :end]


def master(x, ceiling=-1.0, drive=1.15, glue=True, lp=17500, hp=24):
    x = dc(x)
    if hp:
        x = filt(x, "hp", hp, 0.6)
    if lp:
        x = filt(x, "lp", lp, 0.6)
    if glue:
        x = comp(x, thr_db=-20, ratio=2.0, attack=0.006, release=0.15)
    pk = np.abs(x).max() + 1e-9
    x = x / pk * 0.9
    x = sat(x, drive, 0.02) * 0.9
    x = limit(x, ceiling)
    x = trim(x)
    return fade(x, 0.0008, 0.03)
