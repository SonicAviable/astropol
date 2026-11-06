"""Гул звёздной системы: несколько вариантов бесшовных петель (стерео, 44.1 кГц)."""
import sys, subprocess, numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
DUR = 30.0
XF = 4.0
N = int(SR * DUR)
NT = int(SR * (DUR + XF))
t = np.arange(NT) / SR
rng = np.random.default_rng(7)


def lfo(rate, lo=0.0, hi=1.0, seed=None):
    """Плавная случайная кривая: точки раз в 1/rate с, кубическая интерполяция."""
    r = np.random.default_rng(seed)
    n = int(DUR + XF) * max(rate, 0.05) + 4
    pts = r.random(int(n) + 4)
    x = np.linspace(0, DUR + XF, len(pts))
    from scipy.interpolate import CubicSpline
    y = CubicSpline(x, pts)(t)
    y = (y - y.min()) / (y.max() - y.min() + 1e-9)
    return lo + (hi - lo) * y


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, btype="high", fs=SR, output="sos"), x)


def white(seed):
    return np.random.default_rng(seed).standard_normal(NT)


def brown(seed):
    b = np.cumsum(white(seed))
    return hp(b, 15) / 300.0


def pink(seed):
    w = white(seed)
    b, a = [0.049922035, -0.095993537, 0.050612699, -0.004408786], [1, -2.494956002, 2.017265875, -0.522189400]
    return signal.lfilter(b, a, w)


def drone(f0, partials, detune, seed, decay=1.3):
    out = np.zeros(NT)
    r = np.random.default_rng(seed)
    for k in range(1, partials + 1):
        f = f0 * k * (1 + r.uniform(-0.002, 0.002)) + r.uniform(-detune, detune)
        env = lfo(0.15, 0.35, 1.0, seed * 31 + k)
        ph = r.uniform(0, 2 * np.pi)
        vib = 0.15 * np.sin(2 * np.pi * r.uniform(0.03, 0.09) * t + ph)
        out += env * np.sin(2 * np.pi * f * t + ph + vib) / k ** decay
    return out


def stereo(fn):
    """fn(seed) -> моно; левый и правый с разными семенами — широкая картина."""
    return np.stack([fn(1), fn(2)], axis=1)


# ---------------- Варианты ----------------

def v_solar(s):
    """Солнечный гул: тёплый низкий дрон 55 Гц с биениями и рокотом плазмы."""
    d = drone(55, 10, 0.35, 10 + s)
    rumble = lp(brown(20 + s), 180) * lfo(0.1, 0.5, 1, 30 + s)
    air = bp(pink(40 + s), 300, 1600) * 0.16 * lfo(0.08, 0.2, 1, 50 + s)
    return d * 0.5 + rumble * 1.6 + air


def v_plasma(s):
    """Плазма: дышащие полосы шума, как корона звезды, и редкие треск-вспышки."""
    pn = pink(60 + s)
    out = np.zeros(NT)
    for i, (lo, hi) in enumerate([(60, 120), (110, 220), (200, 420), (380, 800), (750, 1500)]):
        out += bp(pn, lo, hi, 3) * lfo(0.12 + i * 0.03, 0.05, 1, 70 + s * 10 + i) / (1 + i * 0.6)
    out += lp(brown(80 + s), 90) * 1.2
    r = np.random.default_rng(90 + s)
    imp = np.zeros(NT)
    idx = r.integers(0, NT, int(DUR * 6))
    imp[idx] = r.uniform(-1, 1, len(idx))
    crack = bp(imp, 1800, 6000) * 2.5 * lfo(0.2, 0, 1, 95 + s) ** 2
    return out * 0.9 + crack


def v_helio(s):
    """Голос Солнца: облако медленных акустических мод (как записи SOHO/NASA), мерцает."""
    r = np.random.default_rng(100 + s)
    out = np.zeros(NT)
    for k in range(70):
        f = np.exp(r.uniform(np.log(70), np.log(900)))
        env = lfo(r.uniform(0.05, 0.3), 0, 1, 1000 + s * 100 + k) ** 3
        out += env * np.sin(2 * np.pi * f * t + r.uniform(0, 6.28)) * (90 / f) ** 0.8
    out *= 0.25
    out += drone(41, 5, 0.2, 110 + s) * 0.25
    out += lp(brown(120 + s), 150) * 0.8
    return out


def v_deep(s):
    """Глубокий космос: инфранизкий гул, едва слышный шелест сверху — сдержанно."""
    f = 33 + 0.6 * np.sin(2 * np.pi * 0.05 * t + s)
    ph = 2 * np.pi * np.cumsum(f) / SR
    sub = np.sin(ph) + 0.35 * np.sin(2 * ph + 0.4) + 0.12 * np.sin(3 * ph)
    sub *= lfo(0.07, 0.6, 1, 130 + s)
    rum = lp(brown(140 + s), 110) * 1.4
    whisper = bp(white(150 + s), 3500, 9000) * 0.012 * lfo(0.1, 0, 1, 160 + s)
    return sub * 0.45 + rum + whisper


def v_pulse(s):
    """Пульсар-лайт: гул звезды с медленным «дыханием» и металлическим резонансом."""
    d = drone(48, 8, 0.25, 170 + s, decay=1.1)
    breath = 0.55 + 0.45 * np.sin(2 * np.pi * 0.18 * t) ** 2
    nz = pink(180 + s)
    D = int(SR / 72)
    a = np.zeros(D + 1); a[0] = 1; a[D] = -0.92
    comb = signal.lfilter([1.0], a, nz)
    comb = bp(comb, 70, 1400) * 0.02
    return (d * 0.45 + comb) * breath + lp(brown(190 + s), 140) * 0.9


VARIANTS = {"solar": v_solar, "plasma": v_plasma, "helio": v_helio, "deep": v_deep, "pulse": v_pulse}


def render(name, fn, out_dir):
    y = np.stack([fn(0), fn(1)], axis=1)
    # левый/правый частично смешаны — не «разъезжается» в наушниках
    y = np.stack([0.8 * y[:, 0] + 0.2 * y[:, 1], 0.2 * y[:, 0] + 0.8 * y[:, 1]], axis=1)
    y = hp(y.T, 20).T
    # бесшовная петля: хвост равномощно накладывается на начало
    X = int(SR * XF)
    a = np.sqrt(np.linspace(0, 1, X))[:, None]
    head = y[:X] * a + y[N:N + X] * np.sqrt(1 - a ** 2)
    y = np.concatenate([head, y[X:N]])
    rms = np.sqrt(np.mean(y ** 2))
    y *= 10 ** (-20 / 20) / rms
    pk = np.max(np.abs(y))
    if pk > 0.89:
        y *= 0.89 / pk
    wav = f"{out_dir}/{name}.wav"
    wavfile.write(wav, SR, (y * 32767).astype(np.int16))
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libmp3lame", "-q:a", "3", f"{out_dir}/{name}.mp3"], check=True)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", f"{out_dir}/{name}.ogg"], check=True)
    print(name, "rms", round(20 * np.log10(np.sqrt(np.mean(y ** 2))), 1), "peak", round(20 * np.log10(np.max(np.abs(y))), 1))


if __name__ == "__main__":
    out = sys.argv[1]
    for k, f in VARIANTS.items():
        render(k, f, out)
