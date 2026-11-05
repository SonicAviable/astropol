"""
Рецепты звуков стратегического слоя (интерфейс, уведомления, время, карта и флоты, стройка и наука,
космический бой, стингеры). Каждый рецепт — функция (rng) → стерео до мастеринга.
Словарь слоёв — внизу описания: tick/thock/glass/metal/servo/air/sub/hum/data/crackle/pad/braam/hit/riser/explosion.
"""
import numpy as np
from dsp import *

# Неармонические моды материалов (отношения частот)
GLASS = [1.0, 2.32, 4.25, 6.63, 9.38]
BAR = [1.0, 2.756, 5.404, 8.933]
PLATE = [1.0, 1.59, 2.14, 2.30, 2.65, 2.92, 3.16, 3.50]


def midi_hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ============================== словарь слоёв (моно, если не сказано иное) ==============================

def modal(rng, f0, ratios, decays, amps, sec=None, jitter=0.008, attack=0.0004):
    sec = sec or max(decays) * 1.1
    n = N(sec)
    t = t_axis(n)
    y = np.zeros(n)
    for r, d, a in zip(ratios, decays, amps):
        f = f0 * r * (1 + rng.u(-jitter, jitter))
        if f > SR * 0.45:
            continue
        y += a * np.sin(2 * np.pi * f * t + rng.u(0, 6.28)) * np.exp(-6.9 * t / d)
    a = N(attack)
    if a > 1:
        y[:a] *= np.linspace(0, 1, a)
    return y


def burst(rng, sec, lo=1500, hi=None, decay=None, color="white"):
    """Короткий шумовой транзиент (удар, касание)."""
    n = N(sec)
    x = noise(rng, sec, color, stereo=False)
    x = filt(x, "hp", lo, 0.7)
    if hi:
        x = filt(x, "lp", hi, 0.7)
    return x * env_exp(n, 0.0002, decay or sec)


def tick(rng, f=4000, body=0.5, ping=0.35):
    """Точный механический щелчок: шумовая атака + звон пружины + корпус."""
    y = np.zeros(N(0.08))
    b = burst(rng, 0.004, 1800, 14000, 0.0016)
    b = filt(b, "peak", f, 1.4, 6)
    y[: b.size] += b * 0.9
    p = modal(rng, f * 1.02, BAR[:3], [0.010, 0.006, 0.004], [1, 0.4, 0.2], 0.05)
    y[: p.size] += p * ping
    if body > 0:
        c = modal(rng, f * 0.23, [1.0, 1.71, 2.6], [0.022, 0.012, 0.008], [1, 0.5, 0.25], 0.06)
        y[: c.size] += c * body * 0.45
    return y


def thock(rng, f=600, sub_hz=95, heavy=0.5, dur=0.14):
    """Глухой удар корпуса (панель, защёлка): низ-середина + толчок."""
    y = np.zeros(N(dur))
    b = burst(rng, 0.008, 200, 3500, 0.004)
    y[: b.size] += b * 0.7
    c = modal(rng, f, [1.0, 1.48, 2.1, 2.9], [0.06, 0.035, 0.02, 0.012], [1, 0.55, 0.3, 0.15], dur)
    y[: c.size] += c * 0.8
    if heavy > 0:
        s = sub(rng, sub_hz * 1.4, sub_hz * 0.75, 0.07, drive=1.4)
        y[: s.size] += s * heavy
    return y


def glass(rng, f, dec=0.6, strike=0.25, bright=1.0):
    """Стеклянный/кристаллический тон: неармонические моды + удар-касание."""
    amps = [1, 0.45 * bright, 0.22 * bright, 0.12 * bright, 0.06 * bright]
    decs = [dec, dec * 0.55, dec * 0.35, dec * 0.22, dec * 0.12]
    y = modal(rng, f, GLASS, decs, amps, dec * 1.1)
    b = burst(rng, 0.003, 3000, 16000, 0.001)
    y[: b.size] += b * strike
    return y


def metal(rng, f, dec=0.3, amps=None, bright=0.8):
    """Металл (лист, кронштейн): плотные неармонические моды."""
    amps = amps or [1, 0.8, 0.6, 0.5, 0.4, 0.3, 0.25, 0.2]
    amps = [a * (bright ** i) for i, a in enumerate(amps)]
    decs = [dec * (1 - 0.08 * i) for i in range(len(PLATE))]
    y = modal(rng, f, PLATE, decs, amps, dec * 1.1, jitter=0.015)
    b = burst(rng, 0.006, 500, 9000, 0.003)
    y[: b.size] += b * 0.5
    return y


def sub(rng, f0, f1, dur, drive=1.8, attack=0.002):
    n = N(dur)
    y = osc(glide(f0, f1, n), n)
    y *= env_exp(n, attack, dur)
    return sat(y, drive, 0.05)


def servo(rng, dur, f0, f1, wobble=0.01):
    """Сервопривод/актуатор: пила с жужжанием редуктора, полосовой, без «пищалки»."""
    n = N(dur)
    f = glide(f0, f1, n) * (1 + wobble * np.sin(2 * np.pi * rng.u(18, 30) * t_axis(n)))
    y = osc(f, n, "saw") * 0.6 + osc(f * 2.01, n, "saw") * 0.25
    y = filt(y, "bp", (f0 + f1) * 2.2, 1.6)
    y = filt(y, "lp", 3200, 0.7)
    g = filt(noise(rng, dur, "white", False), "bp", 1800, 1.2) * 0.25
    y = y + g * (0.5 + 0.5 * np.sin(2 * np.pi * rng.u(25, 40) * t_axis(n)))
    return y * env_adsr(n, 0.012, 0.03, 0.8, 0.03)


def air(rng, dur, f0, f1, q=1.1, pan0=0.0, pan1=0.0, skew=0.5, color="pink"):
    """Поток воздуха (вжух): шум сквозь движущийся полосовой фильтр, стерео-движение."""
    n = N(dur)
    x = noise(rng, dur, color)
    x = sweep(x, "bp", f0, f1, q)
    t = np.linspace(0, 1, n)
    # окно с перекосом: пик в точке skew
    e = np.where(t < skew, np.sin(0.5 * np.pi * t / max(skew, 1e-3)) ** 2,
                 np.cos(0.5 * np.pi * (t - skew) / max(1 - skew, 1e-3)) ** 2)
    x *= e
    p = pan0 + (pan1 - pan0) * t
    th = (p + 1) * np.pi / 4
    return np.vstack([x[0] * np.cos(th), x[1] * np.sin(th)]) * 1.6


def hum(rng, dur, f=60, harm=8, lp=1500, attack=0.05, release=0.15):
    n = N(dur)
    t = t_axis(n)
    y = np.zeros(n)
    for k in range(1, harm + 1):
        y += np.sin(2 * np.pi * f * k * t + rng.u(0, 6.28)) / k ** 1.1
    y *= 1 + 0.15 * np.sin(2 * np.pi * rng.u(5, 9) * t)
    y = filt(y, "lp", lp, 0.7)
    return y * env_adsr(n, attack, 0.05, 0.85, release)


def data(rng, dur, rate=45, f=3200, spread=(1.0, 1.25, 1.5, 2.0)):
    """Телеметрия: россыпь крошечных отсчётов (3–6 мс), очень тихо и высоко."""
    n = N(dur)
    y = np.zeros(n)
    t = 0.0
    while t < dur:
        fr = f * spread[int(rng.u(0, len(spread)))]
        m = N(rng.u(0.003, 0.006))
        b = osc(fr, m) * env_exp(m, 0.0004, m / SR)
        s = N(t)
        e = min(n, s + m)
        y[s:e] += b[: e - s] * rng.u(0.4, 1.0)
        t += rng.u(0.6, 1.4) / rate
    return filt(y, "hp", 1500, 0.7) * env_adsr(n, 0.01, 0.02, 1.0, dur * 0.3)


def crackle(rng, dur, density=400, f=3000, q=0.8, decay=None):
    """Электрический треск: разреженные импульсы сквозь полосу, с затуханием."""
    n = N(dur)
    y = np.zeros(n)
    k = int(density * dur)
    idx = (rng.g.uniform(0, 1, k) ** 1.6 * n).astype(int)
    y[np.clip(idx, 0, n - 1)] = rng.g.uniform(-1, 1, k) * 3
    y = filt(y, "bp", f, q)
    y += filt(y, "bp", f * 2.3, 2) * 0.4
    return y * env_exp(n, 0.0005, decay or dur)


def pad(rng, dur, midis, cut0=500, cut1=2200, attack=0.4, release=1.0, voices=3, detune=7, gain=1.0):
    """Подложка: расстроенные пилы → движущийся ФНЧ; голоса разведены по стерео."""
    n = N(dur)
    out = np.zeros((2, n))
    for m in midis:
        for v in range(voices):
            cents = (v - (voices - 1) / 2) * detune + rng.u(-2, 2)
            f = midi_hz(m) * 2 ** (cents / 1200)
            y = osc(f, n, "saw", rng.u(0, 6.28)) * 0.5 + osc(f * 0.5, n, "tri") * 0.15
            pan = rng.u(-0.7, 0.7)
            add(out, y[None, :] if False else y, 0, 1.0 / len(midis), pan)
    out = sweep(out, "lp", cut0, cut1, 0.6, order=2)
    out *= env_adsr(n, attack, 0.3, 0.85, release)
    return out * gain


def braam(rng, dur, midis, cut_peak=1600, cut_end=300, drive=2.2, attack=0.03):
    """Кинематографичный «браам»: плотная пила с перегрузом, ФНЧ раскрывается и закрывается."""
    n = N(dur)
    out = np.zeros((2, n))
    for m in midis:
        for v in range(4):
            f = midi_hz(m) * 2 ** (((v - 1.5) * 9 + rng.u(-3, 3)) / 1200)
            y = osc(f, n, "saw", rng.u(0, 6.28))
            add(out, y, 0, 0.6 / len(midis), (v - 1.5) * 0.35)
    out = sat(out * 1.2, drive, 0.06)
    pk = 0.12
    out = sweep(out, "lp", 1, 1, 0.9, order=2,
                fq=lambda tt: cut_peak * (tt / pk) ** 0.6 + 120 if tt < pk else cut_end + (cut_peak - cut_end) * np.exp(-(tt - pk) * 4))
    out *= env_adsr(n, attack, 0.4, 0.75, dur * 0.4)
    # суббас
    s = sub(rng, midi_hz(min(midis)) * 1.02, midi_hz(min(midis)) * 0.98, dur, 1.6, 0.02)
    add(out, s, 0, 0.5)
    return out


def hit(rng, size=1.0, tone=60):
    """Кинематографичный удар: щелчок, тело (шум + том), суббас с падением, хвост."""
    dur = 0.9 + 1.2 * size
    out = buf(dur)
    add(out, burst(rng, 0.012, 1200, 12000, 0.006), 0, 0.6)
    body = filt(noise(rng, 0.25, "pink", False), "lp", 900 + 600 * size, 0.7) * env_exp(N(0.25), 0.001, 0.2)
    add(out, sat(body * 2, 2.0), 0, 0.7)
    add(out, modal(rng, tone * 1.6, [1, 1.5, 2.2], [0.4, 0.25, 0.15], [1, 0.5, 0.25]), 0, 0.5)
    add(out, sub(rng, tone * 1.5, tone * 0.55, 0.6 + 0.6 * size, 2.4, 0.001), 0, 1.0)
    return out


def riser(rng, dur, f0=110, f1=440, ncut0=400, ncut1=7000, tone=0.5):
    n = N(dur)
    out = np.zeros((2, n))
    nz = sweep(noise(rng, dur), "bp", ncut0, ncut1, 1.2)
    add(out, nz, 0, 0.7)
    for v in range(3):
        f = glide(f0, f1, n, shape=1.6) * 2 ** ((v - 1) * 8 / 1200)
        add(out, osc(f, n, "saw"), 0, tone * 0.3, (v - 1) * 0.5)
    out = sweep(out, "lp", 600, 9000, 0.7)
    e = (np.linspace(0, 1, n) ** 2.4)
    return out * e


def reverse_swell(rng, src, kind="hall", wet=1.0, length=None):
    """Реверс-набег: хвост реверберации src задом наперёд (заканчивается в точке удара)."""
    w = verb(src, kind, wet=wet, dry=0.0)
    if length:
        w = w[:, : N(length)]
    w = w[:, ::-1]
    return fade(w, 0.02, 0.004)


def explosion(rng, size=1.0):
    dur = 1.2 + 2.0 * size
    out = buf(dur)
    add(out, sat(burst(rng, 0.01, 900, 15000, 0.005) * 2, 2.5), 0, 0.9)          # треск
    nb = noise(rng, 0.9 * (0.6 + size), "pink")
    nb = sweep(nb, "lp", 4000, 220, 0.6)
    nb *= env_exp(nb.shape[1], 0.002, 0.5 + 0.6 * size)
    add(out, sat(nb * 2.2, 2.4), 0, 0.9)                                           # тело
    add(out, sub(rng, 90, 32, 0.9 + 1.2 * size, 2.6, 0.001), 0, 1.1)                # суббас
    rb = filt(noise(rng, dur, "brown"), "lp", 160, 0.7) * env_exp(N(dur), 0.05, dur * 0.8)
    add(out, rb * 3, 0.02, 0.6 * size)                                             # раскат
    add(out, crackle(rng, 1.0 + size, 160, 2600, 0.7), 0.05, 0.35)                 # обломки
    return out


def radio(rng, dur=0.05):
    """Щелчок тангенты и «пшш» шумоподавителя в полосе рации."""
    x = noise(rng, dur, "white", False)
    x = filt(filt(x, "hp", 400, 0.7), "lp", 3400, 0.7)
    x = sat(x * 2, 3.0) * env_exp(N(dur), 0.001, dur)
    y = np.zeros(N(dur + 0.01))
    y[: x.size] += x * 0.6
    t = tick(rng, 2400, 0.2, 0.1)
    y[: min(y.size, t.size)] += t[: y.size] * 0.6
    return y


# ============================== ИНТЕРФЕЙС ==============================

def UiClick(r):
    b = buf(0.2)
    add(b, tick(r, r.j(3600, 0.07), body=0.65), 0, 1.0, r.u(-0.04, 0.04))
    return verb(b, "booth", 0.16)


def UiHover(r):
    b = buf(0.12)
    x = burst(r, 0.02, 4000, 12000, 0.012)
    x = filt(x, "peak", r.j(7200, 0.1), 2.0, 8)
    add(b, x, 0, 0.6, r.u(-0.1, 0.1))
    add(b, modal(r, r.j(5400, 0.05), BAR[:2], [0.02, 0.01], [1, 0.3]), 0.001, 0.12)
    return verb(b, "booth", 0.12)


def UiConfirm(r):
    b = buf(0.6)
    add(b, tick(r, r.j(3400, 0.05), 0.7), 0, 0.9)
    add(b, thock(r, r.j(720, 0.05), 120, 0.45, 0.12), 0.002, 0.5)
    add(b, glass(r, r.j(1320, 0.02), 0.32, 0.1, 0.7), 0.014, 0.32, 0.1)
    return verb(b, "room", 0.2)


def UiBack(r):
    b = buf(0.35)
    add(b, air(r, 0.11, 2800, 700, 1.4, 0.15, -0.15, 0.3), 0, 0.22)
    add(b, thock(r, r.j(520, 0.06), 100, 0.3, 0.1), 0.075, 0.7)
    add(b, tick(r, 2600, 0.3, 0.15), 0.077, 0.35)
    return verb(b, "room", 0.16)


def buzz(r, dur, f=98, bp=420, drive=2.5):
    n = N(dur)
    y = osc(f, n, "saw") + osc(f * 1.007, n, "saw")
    y = sat(filt(y, "bp", bp, 0.9) * 2, drive)
    return y * env_adsr(n, 0.004, 0.02, 0.8, 0.03)


def UiDenied(r):
    b = buf(0.45)
    add(b, thock(r, r.j(330, 0.05), 85, 0.5, 0.12), 0, 0.8)
    add(b, thock(r, r.j(290, 0.05), 75, 0.45, 0.12), 0.085, 0.75)
    add(b, buzz(r, 0.13, 92, 380), 0.004, 0.16)
    return verb(b, "room", 0.14)


def UiDanger(r):
    b = buf(0.8)
    add(b, thock(r, 250, 70, 0.8, 0.2), 0, 0.8)
    add(b, metal(r, r.j(360, 0.04), 0.28, bright=0.7), 0.004, 0.35)
    add(b, buzz(r, 0.26, 73, 340, 3.0), 0.01, 0.2)
    add(b, sub(r, 75, 42, 0.35), 0, 0.5)
    return verb(b, "room", 0.22)


def UiTab(r):
    b = buf(0.18)
    add(b, tick(r, r.j(4600, 0.06), 0.35, 0.3), 0, 0.9, r.u(-0.06, 0.06))
    add(b, servo(r, 0.035, 900, 1150), 0.002, 0.12)
    return verb(b, "booth", 0.15)


def WindowOpen(r):
    b = buf(0.7)
    add(b, air(r, 0.3, 380, 3200, 1.3, -0.25, 0.25, 0.75), 0, 0.3)
    add(b, servo(r, 0.2, 140, 230), 0.02, 0.16)
    add(b, sub(r, 110, 70, 0.12, 1.4), 0, 0.22)
    add(b, tick(r, 3000, 0.6, 0.25), 0.25, 0.55)
    return verb(b, "room", 0.22)


def WindowClose(r):
    b = buf(0.6)
    add(b, air(r, 0.22, 2800, 480, 1.3, 0.2, -0.2, 0.3), 0, 0.26)
    add(b, servo(r, 0.15, 230, 150), 0.0, 0.13)
    add(b, thock(r, r.j(560, 0.04), 95, 0.4, 0.12), 0.19, 0.7)
    return verb(b, "room", 0.2)


def PanelSlide(r):
    b = buf(0.6)
    add(b, air(r, 0.34, 900, 2400, 2.0, -0.4, 0.4, 0.55), 0, 0.22)
    add(b, servo(r, 0.3, 160, 195, 0.02), 0.01, 0.2)
    add(b, tick(r, 2800, 0.5, 0.2), 0.32, 0.2)
    return verb(b, "room", 0.18)


def TextTick(r):
    b = buf(0.06)
    add(b, tick(r, r.j(6200, 0.15), 0.0, 0.5), 0, 0.5, r.u(-0.2, 0.2))
    return b


def HoloScan(r):
    dur = 0.75
    b = buf(1.0)
    x = sweep(noise(r, dur), "bp", 1400, 9000, 4.0)
    n = x.shape[1]
    gate = (np.sin(2 * np.pi * 32 * t_axis(n)) > 0.2).astype(float)
    gate = filt(gate, "lp", 400, 0.7)
    x *= gate * np.sin(np.pi * np.linspace(0, 1, n)) ** 1.5
    th = (np.linspace(-0.6, 0.6, n) + 1) * np.pi / 4
    x = np.vstack([x[0] * np.cos(th), x[1] * np.sin(th)])
    add(b, x, 0, 0.8)
    add(b, data(r, 0.6, 50, 3800), 0.05, 0.2, 0.2)
    add(b, glass(r, 2637, 0.4, 0.0, 0.4), 0.0, 0.06)
    return verb(b, "plate", 0.3)


def MapHover(r):
    b = buf(0.25)
    x = burst(r, 0.035, 600, 6000, 0.03, "pink")
    x = filt(x, "peak", r.j(1800, 0.1), 3, 10)
    x[: N(0.004)] *= np.linspace(0, 1, N(0.004))
    add(b, x, 0, 0.3, r.u(-0.1, 0.1))
    add(b, glass(r, r.j(2600, 0.05), 0.14, 0.0, 0.3), 0.002, 0.1)
    return verb(b, "booth", 0.14)


def AlertPop(r):
    b = buf(0.9)
    add(b, tick(r, 3000, 0.4, 0.2), 0, 0.5)
    add(b, glass(r, r.j(1760, 0.01), 0.5, 0.15), 0.004, 0.45, -0.1)
    add(b, glass(r, r.j(2350, 0.01), 0.42, 0.1), 0.065, 0.3, 0.12)
    return verb(b, "room", 0.28)


def AlertDismiss(r):
    b = buf(0.35)
    add(b, air(r, 0.12, 2200, 600, 1.4, 0.1, -0.1, 0.3), 0, 0.22)
    add(b, tick(r, 2700, 0.4, 0.2), 0.09, 0.25)
    return verb(b, "room", 0.16)


def DealPlace(r):
    b = buf(0.45)
    add(b, thock(r, r.j(430, 0.06), 110, 0.6, 0.16), 0, 0.9)
    slap = burst(r, 0.02, 300, 2400, 0.012, "pink")
    add(b, slap, 0, 0.35)
    return verb(b, "room", 0.2)


def DealRemove(r):
    b = buf(0.3)
    add(b, air(r, 0.1, 800, 2600, 1.4, -0.1, 0.1, 0.6), 0, 0.22)
    add(b, tick(r, 3300, 0.3, 0.2), 0.0, 0.4)
    return verb(b, "room", 0.16)


def DealAccepted(r):
    b = buf(1.6)
    add(b, thock(r, 380, 90, 0.7, 0.2), 0, 0.8)
    add(b, metal(r, 520, 0.22, bright=0.6), 0.01, 0.25)
    add(b, sub(r, 95, 50, 0.3), 0.0, 0.5)
    add(b, glass(r, 1046.5, 0.9, 0.1, 0.8), 0.05, 0.32, -0.15)
    add(b, glass(r, 1568, 0.8, 0.1, 0.7), 0.1, 0.24, 0.15)
    return verb(b, "room", 0.28)


# ============================== УВЕДОМЛЕНИЯ ==============================

def NotifyInfo(r):
    b = buf(1.6)
    add(b, glass(r, 1174.7, 0.9, 0.15), 0, 0.5, -0.1)
    add(b, glass(r, 1760, 0.7, 0.1), 0.07, 0.3, 0.12)
    add(b, air(r, 0.4, 3000, 6000, 1.0, -0.3, 0.3, 0.3), 0, 0.05)
    return verb(b, "hall", 0.24)


def NotifySuccess(r):
    b = buf(2.4)
    add(b, glass(r, 880, 1.1, 0.15), 0, 0.42, -0.15)
    add(b, glass(r, 1318.5, 1.0, 0.12), 0.09, 0.36, 0.1)
    add(b, glass(r, 1760, 1.2, 0.1, 0.8), 0.18, 0.28, 0.0)
    add(b, pad(r, 1.4, [57, 64, 69], 700, 1800, 0.15, 0.8), 0.02, 0.18)
    add(b, sub(r, 110, 90, 0.25, 1.2, 0.01), 0, 0.25)
    return verb(b, "hall", 0.3)


def NotifyWarning(r):
    b = buf(1.8)
    add(b, metal(r, 330, 0.6, bright=0.6), 0, 0.5, -0.1)
    add(b, metal(r, 311, 0.6, bright=0.6), 0.22, 0.45, 0.1)
    add(b, hum(r, 0.7, 55, 8, 900), 0, 0.12)
    add(b, sub(r, 72, 50, 0.4), 0, 0.4)
    return verb(b, "hall", 0.24)


def NotifyDanger(r):
    b = buf(2.2)
    add(b, braam(r, 0.8, [38, 44], 900, 250, 2.4), 0, 0.5)
    add(b, hit(r, 0.35, 55), 0, 0.6)
    add(b, metal(r, 262, 0.7, bright=0.65), 0.005, 0.3)
    return verb(b, "hall", 0.3)


def RedAlert(r):
    b = buf(2.4)
    for k in range(3):
        n = N(0.42)
        f = glide(200, 300, n, shape=0.6)
        y = osc(f, n, "saw") + osc(f * 1.006, n, "saw") * 0.8 + osc(f * 2, n, "saw") * 0.3
        y = sat(filt(y, "bp", 850, 0.8) * 2.5, 3.0)
        y *= env_adsr(n, 0.02, 0.05, 0.85, 0.08)
        add(b, y, k * 0.5, 0.32, (k - 1) * 0.15)
        add(b, sub(r, 58, 50, 0.4, 1.6, 0.01), k * 0.5, 0.3)
    return verb(b, "hall", 0.32)


def Bankrupt(r):
    b = buf(3.0)
    add(b, pad(r, 2.2, [33, 40, 43], 500, 160, 0.05, 1.0, 3, 10), 0, 0.5)
    n = N(1.8)
    t = t_axis(n)
    g = np.zeros(n)
    for k, rr in enumerate(PLATE[:5]):
        g += np.sin(2 * np.pi * np.cumsum(glide(140 * rr, 108 * rr, n)) / SR) * np.exp(-t * (1.6 + k)) / (k + 1)
    add(b, filt(g, "lp", 1800, 0.7), 0.02, 0.4)
    add(b, sub(r, 60, 36, 1.2, 2.0), 0, 0.6)
    return verb(b, "hall", 0.3)


def BudgetWarning(r):
    b = buf(1.0)
    add(b, thock(r, 300, 80, 0.5, 0.14), 0, 0.7)
    add(b, thock(r, 270, 72, 0.5, 0.14), 0.16, 0.65)
    add(b, hum(r, 0.5, 60, 7, 800), 0, 0.12)
    return verb(b, "room", 0.2)


def AlertUrgent(r):
    b = buf(1.6)
    add(b, riser(r, 0.25, 220, 440, 800, 6000, 0.3), 0, 0.25)
    add(b, hit(r, 0.25, 70), 0.25, 0.55)
    add(b, glass(r, 2093, 0.4, 0.2), 0.25, 0.3, -0.15)
    add(b, glass(r, 2093, 0.4, 0.2), 0.36, 0.25, 0.15)
    return verb(b, "hall", 0.25)


# ============================== ВРЕМЯ ==============================

def TimePause(r):
    b = buf(0.6)
    add(b, servo(r, 0.2, 260, 115), 0, 0.3)
    add(b, air(r, 0.2, 2400, 380, 1.2, 0.1, -0.1, 0.3), 0, 0.2)
    add(b, thock(r, 260, 70, 0.6, 0.14), 0.18, 0.75)
    return verb(b, "room", 0.18)


def TimeResume(r):
    b = buf(0.5)
    add(b, servo(r, 0.18, 115, 260), 0, 0.3)
    add(b, air(r, 0.16, 500, 2600, 1.2, -0.1, 0.1, 0.8), 0, 0.2)
    add(b, tick(r, 3200, 0.6, 0.25), 0.17, 0.35)
    return verb(b, "room", 0.18)


def TimeSpeed(r):
    b = buf(0.3)
    add(b, tick(r, 3000, 0.5, 0.2), 0, 0.6)
    add(b, tick(r, 3800, 0.4, 0.2), 0.05, 0.5)
    add(b, servo(r, 0.06, 420, 620), 0.0, 0.14)
    return verb(b, "booth", 0.16)


# ============================== КАРТА И ФЛОТЫ ==============================

def SystemSelect(r):
    b = buf(1.0)
    add(b, tick(r, r.j(3300, 0.05), 0.5, 0.2), 0, 0.6)
    add(b, glass(r, r.j(1568, 0.02), 0.28, 0.05, 0.6), 0.008, 0.28)
    add(b, data(r, 0.12, 60, 3600), 0.02, 0.12, 0.15)
    return verb(verb(b, "room", 0.12), "space", 0.05, hp=500, tail=0.3)


def SystemEnter(r):
    b = buf(2.0)
    add(b, air(r, 0.7, 3400, 240, 1.0, 0.2, -0.2, 0.45), 0, 0.55)
    n = N(1.0)
    s = osc(glide(80, 38, n), n) * env_adsr(n, 0.25, 0.2, 0.7, 0.5)
    add(b, sat(s, 1.8), 0.1, 0.5)
    add(b, reverse_swell(r, glass(r, 523, 0.5, 0.0) * 0.6, "space", 1.0, 0.6), 0.0, 0.25)
    return verb(b, "space", 0.25)


def SystemExit(r):
    b = buf(1.6)
    add(b, air(r, 0.55, 280, 3200, 1.0, -0.2, 0.2, 0.7), 0, 0.5)
    add(b, sub(r, 70, 48, 0.4, 1.5, 0.08), 0.1, 0.35)
    return verb(b, "space", 0.22)


def SelectMilitary(r):
    b = buf(0.9)
    add(b, metal(r, r.j(210, 0.04), 0.35, bright=0.6), 0, 0.45)
    add(b, thock(r, 300, 80, 0.7, 0.16), 0, 0.6)
    add(b, radio(r, 0.035), 0.05, 0.25, 0.2)
    return verb(b, "room", 0.22)


def SelectScience(r):
    b = buf(0.9)
    add(b, data(r, 0.16, 70, 3400), 0, 0.25)
    add(b, glass(r, r.j(2093, 0.02), 0.35, 0.1, 0.6), 0.02, 0.3)
    add(b, tick(r, 3800, 0.4, 0.2), 0, 0.45)
    add(b, air(r, 0.25, 2000, 5000, 1.4, -0.2, 0.2, 0.3), 0, 0.06)
    return verb(b, "room", 0.22)


def SelectConstructor(r):
    b = buf(0.8)
    add(b, servo(r, 0.16, 300, 420), 0, 0.28)
    add(b, thock(r, 500, 90, 0.5, 0.12), 0.0, 0.6)
    add(b, metal(r, r.j(640, 0.04), 0.12, bright=0.7), 0.1, 0.35)
    return verb(b, "room", 0.2)


def OrderMove(r):
    b = buf(1.4)
    add(b, radio(r, 0.03), 0, 0.35)
    n = N(0.7)
    eng = filt(noise(r, 0.7, "brown", False), "lp", 220, 0.7) * 3 + filt(osc(55, n, "saw"), "lp", 300, 0.7) * 0.4
    add(b, sat(eng * env_adsr(n, 0.08, 0.1, 0.6, 0.4), 1.6), 0.02, 0.45)
    add(b, air(r, 0.45, 600, 2200, 1.2, -0.3, 0.3, 0.4), 0.05, 0.25)
    return verb(verb(b, "room", 0.12), "space", 0.12, tail=0.5)


def OrderQueue(r):
    b = buf(0.3)
    add(b, tick(r, r.j(3500, 0.06), 0.4, 0.2), 0, 0.6)
    add(b, data(r, 0.06, 80, 4000), 0.01, 0.2)
    return verb(b, "room", 0.15)


def OrderStop(r):
    b = buf(0.5)
    add(b, servo(r, 0.12, 300, 150), 0, 0.28)
    add(b, thock(r, 350, 80, 0.6, 0.14), 0.1, 0.7)
    return verb(b, "room", 0.18)


def FtlJump(r):
    b = buf(3.2)
    add(b, riser(r, 0.7, 110, 440, 500, 7000, 0.6), 0, 0.45)
    add(b, sat(burst(r, 0.01, 800, 15000, 0.005) * 2, 2.5), 0.7, 0.7)
    add(b, sub(r, 95, 28, 0.9, 2.4, 0.001), 0.7, 0.9)
    add(b, air(r, 0.45, 6000, 300, 0.9, 0.0, 0.0, 0.1, "white"), 0.7, 0.4)
    return verb(b, "space", 0.32)


def FtlArrive(r):
    b = buf(2.6)
    add(b, air(r, 0.38, 300, 3200, 1.1, -0.3, 0.0, 0.97), 0, 0.45)
    add(b, sub(r, 75, 40, 0.6, 2.0, 0.002), 0.36, 0.7)
    add(b, burst(r, 0.008, 1000, 12000, 0.004), 0.36, 0.4)
    add(b, glass(r, 3136, 0.9, 0.0, 0.4), 0.36, 0.08)
    return verb(b, "space", 0.3)


def SurveyComplete(r):
    b = buf(2.4)
    add(b, data(r, 0.22, 70, 3200), 0, 0.2)
    add(b, glass(r, 1318.5, 0.9, 0.15), 0.2, 0.4, -0.12)
    add(b, glass(r, 1975.5, 0.8, 0.1), 0.28, 0.28, 0.12)
    add(b, pad(r, 1.1, [52, 59, 64], 600, 1500, 0.1, 0.7), 0.18, 0.16)
    return verb(b, "hall", 0.28)


def OutpostBuilt(r):
    b = buf(2.2)
    add(b, thock(r, 260, 70, 0.7, 0.16), 0, 0.7)
    add(b, thock(r, 280, 75, 0.6, 0.16), 0.14, 0.6)
    add(b, metal(r, 300, 0.6, bright=0.55), 0.28, 0.4)
    add(b, sub(r, 80, 42, 0.6), 0.28, 0.55)
    add(b, hum(r, 0.9, 55, 9, 1200, 0.3, 0.4), 0.3, 0.12)
    return verb(b, "hall", 0.26)


def Anomaly(r):
    dur = 2.6
    b = buf(3.4)
    n = N(dur)
    t = t_axis(n)
    d = (np.sin(2 * np.pi * 466 * t) + np.sin(2 * np.pi * 471.5 * t)) * env_adsr(n, 0.5, 0.3, 0.6, 1.0)
    add(b, filt(d, "lp", 2000, 0.7), 0, 0.12)
    add(b, glass(r, 740, 2.4, 0.0, 1.0), 0.45, 0.25, -0.2)
    add(b, glass(r, 1047 * 1.012, 2.0, 0.0, 1.0), 0.6, 0.18, 0.2)
    add(b, reverse_swell(r, glass(r, 740, 1.0, 0.0), "hall", 1.0, 0.5), 0.0, 0.3)
    wind = sweep(noise(r, dur, "pink"), "bp", 500, 1400, 6.0) * env_adsr(n, 0.8, 0.3, 0.6, 1.0)
    add(b, wind, 0, 0.25)
    return verb(b, "space", 0.4)


# ============================== СТРОЙКА И НАУКА ==============================

def BuildQueued(r):
    b = buf(0.45)
    add(b, tick(r, 3400, 0.5, 0.2), 0, 0.6)
    add(b, servo(r, 0.05, 520, 700), 0.0, 0.14)
    add(b, thock(r, 650, 100, 0.4, 0.1), 0.05, 0.55)
    return verb(b, "room", 0.18)


def BuildComplete(r):
    b = buf(1.8)
    add(b, thock(r, 420, 90, 0.6, 0.16), 0, 0.7)
    add(b, metal(r, 560, 0.35, bright=0.6), 0.005, 0.3)
    add(b, glass(r, 1318.5, 0.7, 0.1), 0.06, 0.32)
    add(b, sub(r, 90, 50, 0.3), 0, 0.4)
    return verb(b, "room", 0.3)


def ShipLaunched(r):
    b = buf(3.0)
    add(b, metal(r, 400, 0.3, bright=0.6), 0, 0.35)
    add(b, thock(r, 220, 65, 0.8, 0.18), 0, 0.6)
    n = N(1.2)
    ign = sweep(noise(r, 1.2, "pink"), "lp", 300, 5000, 0.7) * env_adsr(n, 0.15, 0.2, 0.6, 0.6)
    add(b, sat(ign * 2, 1.8), 0.1, 0.5)
    rum = filt(noise(r, 1.4, "brown", False), "lp", 90, 0.7) * 4 * env_adsr(N(1.4), 0.1, 0.2, 0.7, 0.7)
    add(b, rum, 0.1, 0.5)
    add(b, air(r, 1.0, 2000, 500, 1.0, -0.6, 0.6, 0.35), 0.35, 0.35)
    return verb(b, "hall", 0.32)


def ColonyFounded(r):
    b = buf(4.2)
    add(b, pad(r, 3.0, [45, 52, 57, 59, 64], 380, 2600, 0.6, 1.4, 3, 8), 0, 0.5)
    add(b, glass(r, 1318.5, 1.6, 0.05, 0.7), 0.25, 0.16, -0.25)
    add(b, glass(r, 1975.5, 1.4, 0.05, 0.7), 0.45, 0.12, 0.25)
    n = N(2.0)
    s = osc(55, n) * env_adsr(n, 0.4, 0.3, 0.7, 1.0)
    add(b, sat(s, 1.5), 0, 0.35)
    add(b, air(r, 1.6, 1500, 6000, 0.8, -0.4, 0.4, 0.5), 0, 0.06)
    return verb(b, "hall", 0.4)


def ResearchComplete(r):
    b = buf(3.6)
    src = buf(0.9)
    add(src, glass(r, 1046.5, 0.8, 0.0)); add(src, glass(r, 1568, 0.7, 0.0))
    add(b, reverse_swell(r, src, "hall", 1.0, 0.6), 0, 0.35)
    add(b, glass(r, 1046.5, 1.4, 0.15), 0.6, 0.36, -0.2)
    add(b, glass(r, 1568, 1.2, 0.12), 0.6, 0.28, 0.2)
    add(b, glass(r, 2093, 1.0, 0.1, 0.7), 0.62, 0.2, 0.0)
    add(b, pad(r, 2.0, [48, 55, 62, 67], 500, 2200, 0.2, 1.0), 0.55, 0.22)
    add(b, sub(r, 80, 52, 0.5, 1.6), 0.6, 0.4)
    return verb(b, "hall", 0.34)


def ResearchStart(r):
    b = buf(1.2)
    add(b, tick(r, 3200, 0.5, 0.2), 0, 0.5)
    add(b, data(r, 0.32, 55, 3000), 0.02, 0.22)
    add(b, hum(r, 0.6, 110, 6, 1600, 0.15, 0.3), 0.02, 0.1)
    return verb(b, "room", 0.22)


# ============================== КОСМИЧЕСКИЙ БОЙ ==============================

def WeaponKinetic(r):
    b = buf(1.0)
    add(b, sat(burst(r, 0.005, 1500, 16000, 0.0025) * 2, 2.5), 0, 0.7, r.u(-0.1, 0.1))
    add(b, sub(r, r.j(150, 0.1), 60, 0.14, 2.2, 0.0005), 0, 0.8)
    bd = filt(noise(r, 0.1, "pink", False), "bp", r.j(650, 0.15), 0.9) * env_exp(N(0.1), 0.0005, 0.07)
    add(b, sat(bd * 3, 2.0), 0, 0.5)
    add(b, metal(r, r.j(900, 0.1), 0.07, bright=0.7), 0.004, 0.18)
    return verb(b, "space", 0.1, hp=450, tail=0.22)


def WeaponEnergy(r):
    b = buf(0.9)
    z = sweep(noise(r, 0.11), "bp", r.j(7500, 0.1), r.j(1200, 0.15), 3.0)
    z = sat(z * env_exp(z.shape[1], 0.0005, 0.1) * 3, 3.0)
    add(b, z, 0, 0.6)
    add(b, burst(r, 0.004, 2000, 16000, 0.002), 0, 0.5)
    n = N(0.11)
    hb = filt(osc(r.j(120, 0.08), n, "saw"), "bp", 800, 1.0) * env_exp(n, 0.001, 0.1)
    add(b, sat(hb * 2, 2.5), 0, 0.3)
    add(b, crackle(r, 0.09, 600, 4000, 0.8), 0.004, 0.2)
    add(b, sub(r, 110, 60, 0.08, 1.6), 0, 0.3)
    return verb(b, "space", 0.1, hp=450, tail=0.22)


def WeaponMissile(r):
    b = buf(1.6)
    add(b, thock(r, 220, 70, 0.6, 0.1), 0, 0.5)
    n = N(0.7)
    hs = sweep(noise(r, 0.7, "pink"), "bp", 1000, 3500, 0.7) * env_adsr(n, 0.02, 0.1, 0.6, 0.4)
    add(b, sat(hs * 1.6, 1.6), 0.01, 0.4)
    add(b, air(r, 0.7, 2600, 700, 0.9, r.u(-0.3, 0), r.u(0.3, 0.7), 0.25), 0.05, 0.4)
    return verb(b, "space", 0.12, hp=400, tail=0.3)


def HitShield(r):
    b = buf(1.0)
    add(b, crackle(r, 0.16, 700, 3500, 0.7), 0, 0.5)
    add(b, glass(r, r.j(2900, 0.06), 0.25, 0.3, 0.9), 0, 0.3)
    add(b, sub(r, 95, 50, 0.18, 1.8), 0, 0.5)
    fz = sweep(noise(r, 0.22), "bp", 3200, 900, 2.0) * env_exp(N(0.22), 0.001, 0.2)
    add(b, fz, 0, 0.35)
    return verb(b, "plate", 0.18)


def HitArmor(r):
    b = buf(1.2)
    add(b, metal(r, r.j(320, 0.08), 0.35, bright=0.75), 0, 0.5)
    cr = sat(filt(noise(r, 0.07, "white", False), "bp", 1500, 0.8) * env_exp(N(0.07), 0.0005, 0.05) * 3, 2.5)
    add(b, cr, 0, 0.45)
    add(b, crackle(r, 0.35, 140, 2800, 0.7), 0.02, 0.25)
    add(b, sub(r, 105, 50, 0.2, 2.0), 0, 0.6)
    return verb(b, "room", 0.2)


def ExplosionSmall(r):
    return verb(explosion(r, 0.45), "space", 0.16, hp=300, tail=0.45)


def ExplosionLarge(r):
    return verb(explosion(r, 1.0), "space", 0.25)


def StarbaseDown(r):
    b = buf(5.5)
    add(b, explosion(r, 1.4), 0, 1.0)
    add(b, explosion(r, 0.7), 0.5, 0.6)
    n = N(2.5)
    g = np.zeros(n)
    t = t_axis(n)
    for k, rr in enumerate(PLATE[:6]):
        g += np.sin(2 * np.pi * np.cumsum(glide(95 * rr, 68 * rr, n)) / SR) * np.exp(-t * (0.8 + 0.5 * k)) / (k + 1)
    add(b, filt(g, "lp", 1500, 0.7), 0.3, 0.35)
    return verb(b, "space", 0.3)


def Disengage(r):
    b = buf(2.6)
    n = N(1.3)
    sp = osc(glide(55, 150, n, shape=1.4), n, "saw") + osc(glide(55.4, 151, n, shape=1.4), n, "saw")
    sp = sweep(sp, "lp", 250, 1600, 0.8) * env_adsr(n, 0.1, 0.2, 0.8, 0.4)
    add(b, sat(sp, 1.8), 0, 0.3)
    add(b, air(r, 1.0, 600, 3000, 1.0, -0.5, 0.6, 0.6), 0.2, 0.4)
    return verb(b, "space", 0.28)


def FtlFail(r):
    b = buf(2.2)
    add(b, riser(r, 0.5, 110, 330, 500, 5000, 0.6), 0, 0.4)
    n = N(0.45)
    dn = osc(glide(330, 70, n, shape=0.6), n, "saw")
    dn = sat(filt(dn, "lp", 1400, 0.7) * env_exp(n, 0.002, 0.45) * 2, 2.5)
    add(b, dn, 0.5, 0.35)
    add(b, crackle(r, 0.5, 400, 3000, 0.8), 0.5, 0.35)
    add(b, thock(r, 200, 60, 0.8, 0.2), 0.52, 0.6)
    return verb(b, "space", 0.25)


# ============================== СТИНГЕРЫ ==============================

def BattleStart(r):
    b = buf(4.0)
    add(b, riser(r, 0.6, 73, 146, 300, 5000, 0.5), 0, 0.4)
    add(b, hit(r, 1.0, 55), 0.6, 0.9)
    add(b, braam(r, 2.2, [26, 38, 45], 1400, 260, 2.4), 0.6, 0.55)
    return verb(b, "hall", 0.36)


def BattleVictory(r):
    b = buf(5.0)
    add(b, hit(r, 0.5, 70), 0, 0.5)
    add(b, pad(r, 3.2, [50, 57, 62, 66, 69], 400, 2600, 0.25, 1.4, 3, 8), 0.05, 0.55)
    add(b, glass(r, 1760, 1.6, 0.05, 0.7), 0.3, 0.12, -0.2)
    add(b, glass(r, 2349, 1.4, 0.05, 0.7), 0.5, 0.1, 0.2)
    return verb(b, "hall", 0.42)


def BattleDefeat(r):
    b = buf(5.0)
    add(b, reverse_swell(r, metal(r, 180, 1.0, bright=0.5), "hall", 1.0, 0.7), 0, 0.4)
    add(b, braam(r, 2.8, [28, 40, 43], 1000, 160, 2.2, 0.08), 0.7, 0.55)
    add(b, sub(r, 55, 30, 1.6, 2.2), 0.7, 0.6)
    return verb(b, "hall", 0.42)


def WarDeclared(r):
    b = buf(5.5)
    add(b, hit(r, 1.2, 50), 0, 1.0)
    add(b, hit(r, 0.9, 46), 0.45, 0.8)
    add(b, braam(r, 3.0, [26, 38, 45, 50], 1600, 220, 2.8), 0.45, 0.6)
    return verb(b, "hall", 0.42)


def PeaceSigned(r):
    b = buf(4.6)
    add(b, pad(r, 3.0, [53, 60, 65, 69], 450, 2200, 0.5, 1.4, 3, 7), 0, 0.5)
    add(b, glass(r, 1396.9, 1.6, 0.05, 0.6), 0.2, 0.16, -0.2)
    add(b, glass(r, 2093, 1.4, 0.05, 0.6), 0.4, 0.12, 0.2)
    add(b, air(r, 1.5, 1200, 5000, 0.8, -0.3, 0.3, 0.5), 0, 0.05)
    return verb(b, "hall", 0.42)


def PactSigned(r):
    b = buf(3.0)
    add(b, thock(r, 320, 80, 0.7, 0.16), 0, 0.7)
    add(b, thock(r, 340, 85, 0.6, 0.16), 0.16, 0.6)
    add(b, metal(r, 480, 0.4, bright=0.55), 0.16, 0.25)
    add(b, pad(r, 1.8, [55, 62, 67], 500, 1800, 0.2, 1.0), 0.15, 0.4)
    return verb(b, "hall", 0.34)


def drum(r, tone=60, size=0.8):
    out = buf(1.2)
    body = filt(noise(r, 0.2, "pink", False), "lp", 800, 0.7) * env_exp(N(0.2), 0.001, 0.15)
    add(out, sat(body * 2, 2.0), 0, 0.5)
    add(out, modal(r, tone, [1, 1.52, 2.1], [0.6, 0.3, 0.15], [1, 0.4, 0.2]), 0, 0.6)
    add(out, sub(r, tone * 1.2, tone * 0.7, 0.6 * size, 2.0), 0, 0.8)
    return out


def SiegeStart(r):
    b = buf(4.0)
    for k, tm in enumerate([0, 0.36, 0.72]):
        add(b, drum(r, 62 - k * 2, 0.8), tm, 0.8 - k * 0.05)
    add(b, braam(r, 2.0, [33, 40], 1000, 200, 2.4, 0.4), 0.72, 0.45)
    return verb(b, "hall", 0.38)


def SystemCaptured(r):
    b = buf(3.4)
    add(b, hit(r, 0.8, 60), 0, 0.8)
    add(b, braam(r, 1.4, [38, 45, 50], 1500, 400, 2.2), 0, 0.45)
    add(b, glass(r, 1760, 1.2, 0.1, 0.7), 0.05, 0.16)
    return verb(b, "hall", 0.36)


def SystemLost(r):
    b = buf(3.8)
    add(b, braam(r, 1.8, [40, 46], 1200, 140, 2.4, 0.05), 0, 0.5)
    add(b, sub(r, 62, 32, 1.4, 2.2), 0, 0.6)
    add(b, reverse_swell(r, metal(r, 160, 0.8, bright=0.5), "hall", 1.0, 0.4), 0.0, 0.15)
    return verb(b, "hall", 0.38)


def GameVictory(r):
    b = buf(7.0)
    add(b, riser(r, 0.8, 98, 196, 300, 6000, 0.4), 0, 0.35)
    add(b, hit(r, 1.0, 55), 0.8, 0.8)
    add(b, pad(r, 1.6, [50, 57, 62, 64], 500, 1800, 0.1, 0.8, 3, 7), 0.8, 0.45)
    add(b, pad(r, 4.0, [50, 57, 62, 66, 69, 74], 700, 3000, 0.6, 1.8, 3, 7), 1.8, 0.55)
    add(b, glass(r, 1760, 2.0, 0.05, 0.6), 1.9, 0.12, -0.25)
    add(b, glass(r, 2637, 1.8, 0.05, 0.6), 2.2, 0.1, 0.25)
    return verb(b, "hall", 0.45)


def GameDefeat(r):
    b = buf(6.5)
    add(b, reverse_swell(r, metal(r, 140, 1.2, bright=0.5), "hall", 1.0, 0.9), 0, 0.35)
    add(b, braam(r, 3.6, [21, 33, 40], 900, 120, 2.4, 0.1), 0.9, 0.55)
    add(b, sub(r, 48, 26, 2.4, 2.2), 0.9, 0.6)
    return verb(b, "hall", 0.45)


def GameStart(r):
    b = buf(6.0)
    add(b, riser(r, 1.0, 55, 110, 250, 6000, 0.45), 0, 0.4)
    add(b, hit(r, 1.1, 50), 1.0, 0.9)
    add(b, pad(r, 3.2, [45, 52, 57, 64, 71], 400, 2800, 0.3, 1.6, 3, 8), 1.0, 0.5)
    add(b, glass(r, 1318.5, 2.0, 0.05, 0.6), 1.2, 0.1, -0.25)
    add(b, glass(r, 1975.5, 1.8, 0.05, 0.6), 1.45, 0.08, 0.25)
    return verb(b, "hall", 0.42)


# ============================== реестр ==============================
#   имя: (функция, вариантов, категория мастеринга)
UI, NOTE, STING, WORLD = "ui", "note", "sting", "world"
SOUNDS = {
    "UiClick": (UiClick, 5, UI), "UiHover": (UiHover, 4, UI), "UiConfirm": (UiConfirm, 4, UI),
    "UiBack": (UiBack, 3, UI), "UiDenied": (UiDenied, 3, UI), "UiDanger": (UiDanger, 2, UI),
    "UiTab": (UiTab, 4, UI), "WindowOpen": (WindowOpen, 3, UI), "WindowClose": (WindowClose, 3, UI),
    "PanelSlide": (PanelSlide, 3, UI), "TextTick": (TextTick, 6, UI), "HoloScan": (HoloScan, 2, UI),
    "MapHover": (MapHover, 4, UI), "AlertPop": (AlertPop, 2, UI), "AlertDismiss": (AlertDismiss, 2, UI),
    "DealPlace": (DealPlace, 3, UI), "DealRemove": (DealRemove, 3, UI), "DealAccepted": (DealAccepted, 2, UI),
    "NotifyInfo": (NotifyInfo, 2, NOTE), "NotifySuccess": (NotifySuccess, 2, NOTE), "NotifyWarning": (NotifyWarning, 2, NOTE),
    "NotifyDanger": (NotifyDanger, 2, NOTE), "RedAlert": (RedAlert, 1, NOTE), "Bankrupt": (Bankrupt, 1, NOTE),
    "BudgetWarning": (BudgetWarning, 2, NOTE), "AlertUrgent": (AlertUrgent, 2, NOTE),
    "TimePause": (TimePause, 2, UI), "TimeResume": (TimeResume, 2, UI), "TimeSpeed": (TimeSpeed, 3, UI),
    "SystemSelect": (SystemSelect, 3, UI), "SystemEnter": (SystemEnter, 2, WORLD), "SystemExit": (SystemExit, 2, WORLD),
    "SelectMilitary": (SelectMilitary, 3, UI), "SelectScience": (SelectScience, 3, UI), "SelectConstructor": (SelectConstructor, 3, UI),
    "OrderMove": (OrderMove, 3, UI), "OrderQueue": (OrderQueue, 3, UI), "OrderStop": (OrderStop, 2, UI),
    "FtlJump": (FtlJump, 3, WORLD), "FtlArrive": (FtlArrive, 3, WORLD), "SurveyComplete": (SurveyComplete, 2, NOTE),
    "OutpostBuilt": (OutpostBuilt, 2, NOTE), "Anomaly": (Anomaly, 2, NOTE),
    "BuildQueued": (BuildQueued, 3, UI), "BuildComplete": (BuildComplete, 2, NOTE), "ShipLaunched": (ShipLaunched, 2, WORLD),
    "ColonyFounded": (ColonyFounded, 1, STING), "ResearchComplete": (ResearchComplete, 2, NOTE), "ResearchStart": (ResearchStart, 2, UI),
    "WeaponKinetic": (WeaponKinetic, 6, WORLD), "WeaponEnergy": (WeaponEnergy, 6, WORLD), "WeaponMissile": (WeaponMissile, 4, WORLD),
    "HitShield": (HitShield, 5, WORLD), "HitArmor": (HitArmor, 5, WORLD), "ExplosionSmall": (ExplosionSmall, 4, WORLD),
    "ExplosionLarge": (ExplosionLarge, 3, WORLD), "StarbaseDown": (StarbaseDown, 1, WORLD), "Disengage": (Disengage, 2, WORLD),
    "FtlFail": (FtlFail, 1, WORLD),
    "BattleStart": (BattleStart, 1, STING), "BattleVictory": (BattleVictory, 1, STING), "BattleDefeat": (BattleDefeat, 1, STING),
    "WarDeclared": (WarDeclared, 1, STING), "PeaceSigned": (PeaceSigned, 1, STING), "PactSigned": (PactSigned, 1, STING),
    "SiegeStart": (SiegeStart, 1, STING), "SystemCaptured": (SystemCaptured, 1, STING), "SystemLost": (SystemLost, 1, STING),
    "GameVictory": (GameVictory, 1, STING), "GameDefeat": (GameDefeat, 1, STING), "GameStart": (GameStart, 1, STING),
}
