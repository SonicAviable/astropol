"""
Эффекты карточного боя (флипбуки) — рисуются кодом, без чужих ассетов.

    python3 Tools/card_fx/gen_fx.py Assets/Resources/UI/CardBattle [папка_превью]

Каждый файл fx2_<имя>.png — лист кадров (слева направо, сверху вниз), белый RGB, яркость — в альфе.
В игре их рисует шейдер Astropolity/UI/FxRamp: яркое ядро уходит в белый, края — в цвет оттенка
(одна текстура — любой цвет: золото усиления, бирюза захвата, красный урона).
Стиль — как у больших карточных игр: резкие вспышки-звёзды, дуги ударов, ударные волны, лучи, рунные круги.
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw

SS = 2   # суперсэмплинг: рисуем вдвое крупнее, потом уменьшаем — гладкие края


# ---------------------------------------------------------------- основа

def ease_out(t, p=3.0):
    t = min(1.0, max(0.0, t))
    return 1.0 - (1.0 - t) ** p


def ease_in_out(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def env(t, a, b):
    """Огибающая: нарастает до a, держится, гаснет после b."""
    if t < a: return ease_out(t / a, 2)
    if t > b: return max(0.0, 1.0 - (t - b) / (1.0 - b)) ** 1.6
    return 1.0


def coords(w, h):
    """Координаты пикселей: x вправо, y вверх, единица — половина ширины кадра; центр — середина."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    s = w / 2.0
    return (x + 0.5 - w / 2.0) / s, (h / 2.0 - y - 0.5) / s


def polar(x, y):
    return np.sqrt(x * x + y * y), np.arctan2(y, x)


def box(a, r, axis):
    if r < 1: return a
    c = np.cumsum(np.pad(a, [(r + 1, r) if i == axis else (0, 0) for i in range(a.ndim)]), axis=axis)
    hi = np.take(c, np.arange(2 * r + 1, c.shape[axis]), axis=axis)
    lo = np.take(c, np.arange(0, c.shape[axis] - 2 * r - 1), axis=axis)
    return (hi - lo) / (2 * r + 1)


def blur(a, sigma):
    """Гауссово размытие (три прохода «коробкой» по каждой оси)."""
    if sigma <= 0.3: return a
    w = int(round(math.sqrt(4 * sigma * sigma + 1)))
    r = max(1, w // 2)
    for _ in range(3):
        a = box(a, r, 0)
        a = box(a, r, 1)
    return a


def bloom(core, w, glow=0.9, r1=0.012, r2=0.05):
    """Ядро + два слоя свечения разного радиуса (как у света в кино)."""
    px = w
    return core + glow * (0.6 * blur(core, r1 * px) + 0.5 * blur(core, r2 * px))


def tone(a):
    """Мягкое ограничение: яркие места насыщаются к 1, не обрезаясь."""
    return 1.0 - np.exp(-np.maximum(a, 0) * 1.4)


def angdiff(a, b):
    return np.abs((a - b + np.pi) % (2 * np.pi) - np.pi)


def sheet(name, cols, rows, fw, fh, frame_fn, out_dir, previews=None):
    W, H = fw * SS, fh * SS
    x, y = coords(W, H)
    img = np.zeros((rows * fh, cols * fw), np.float64)
    n = cols * rows
    frames = []
    for i in range(n):
        t = i / (n - 1)
        a = frame_fn(t, x, y, W, H, i)
        a = tone(a)
        a = a.reshape(fh, SS, fw, SS).mean(axis=(1, 3))
        cx, cy = i % cols, i // cols
        img[cy * fh:(cy + 1) * fh, cx * fw:(cx + 1) * fw] = a
        frames.append(a)
    alpha = np.clip(img * 255.0 + 0.5, 0, 255).astype(np.uint8)
    rgba = np.zeros(alpha.shape + (4,), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = alpha
    Image.fromarray(rgba, 'RGBA').save(os.path.join(out_dir, f'fx2_{name}.png'), optimize=True)
    if previews is not None: previews.append((name, frames))
    print(name, img.shape[1], 'x', img.shape[0], 'кадров', n)


RNG = np.random.default_rng(7)


# ---------------------------------------------------------------- эффекты

def impact():
    """Удар: вспышка-звезда с острыми лучами, ядро, разлёт штрихов, тонкое кольцо."""
    n_sp = 7
    sp_a = RNG.uniform(0, 2 * np.pi, n_sp)
    sp_l = RNG.uniform(0.55, 0.95, n_sp)
    sp_l[0] = sp_l[3] = 1.0
    n_ln = 16
    ln_a = RNG.uniform(0, 2 * np.pi, n_ln)
    ln_s = RNG.uniform(0.7, 1.15, n_ln)

    def f(t, x, y, W, H, i):
        r, th = polar(x, y)
        e = ease_out(t * 2.6)
        fade = (1 - t) ** 1.7
        a = np.zeros_like(r)
        # ядро
        a += 2.2 * np.exp(-(r / (0.07 + 0.08 * e)) ** 2) * (1 - t) ** 2.4
        # лучи звезды: длинные иглы, тонкие к концам
        for k in range(n_sp):
            L = sp_l[k] * (0.25 + 0.75 * e) * (1 - 0.35 * t)
            d = angdiff(th, sp_a[k] + 0.15 * t) * r
            w = 0.022 * (1 - r / (L + 1e-6)).clip(0, 1) + 0.002
            a += 1.6 * np.exp(-(d / w) ** 2) * (r < L) * (1 - r / (L + 1e-6)).clip(0, 1) ** 0.7 * fade
        # разлетающиеся штрихи
        for k in range(n_ln):
            r0 = (0.18 + 0.75 * ease_out(t * 1.4)) * ln_s[k]
            r1 = r0 + 0.22 * (1 - t) ** 1.5
            d = angdiff(th, ln_a[k]) * r
            seg = ((r > r0) & (r < r1)) * np.sin(np.clip((r - r0) / (r1 - r0 + 1e-6), 0, 1) * np.pi)
            a += 1.0 * np.exp(-(d / 0.007) ** 2) * seg * (1 - t) ** 1.2
        # кольцо
        R = 0.12 + 0.8 * ease_out(t * 1.3)
        a += 0.9 * np.exp(-((r - R) / (0.012 + 0.03 * (1 - t))) ** 2) * (1 - t) ** 1.6
        return bloom(a, W, 0.9)
    return f


def slash():
    """Рассекающая дуга: голова бежит по дуге, хвост догоняет и истончается; кадр 2:1."""
    def f(t, x, y, W, H, i):
        cy = -0.62
        r, th = polar(x, y - cy)
        R = 1.0
        a0, a1 = math.radians(158), math.radians(22)
        head = a0 + (a1 - a0) * ease_out(t * 1.8, 2.5)
        tail = a0 + (a1 - a0) * ease_in_out((t - 0.12) / 0.7)
        # положение вдоль дуги: 0 — хвост, 1 — голова (угол убывает от a0 к a1)
        span = head - tail
        u = np.where(np.abs(span) > 1e-4, (th - tail) / (span if abs(span) > 1e-4 else 1), 0)
        inside = (u >= 0) & (u <= 1)
        prof = np.sin(np.clip(u, 0, 1) * np.pi * 0.92) ** 0.6 * (0.25 + 0.75 * np.clip(u, 0, 1))
        w = 0.16 * prof + 0.005
        d = np.abs(r - R)
        fade = env(t, 0.1, 0.55)
        core = np.exp(-(d / (w * 0.35)) ** 2) * inside * prof
        edge = 0.5 * np.exp(-(d / w) ** 2) * inside * prof
        # «перо» внутрь дуги — след движения
        trail = 0.35 * np.exp(-((R - r) / (w * 2.2)) ** 2) * (r < R) * inside * prof ** 1.5
        a = (2.0 * core + edge + trail) * fade
        return bloom(a, W, 0.8)
    return f


def shock():
    """Ударная волна: резкая кромка, мягкий внутренний свет, второе запаздывающее кольцо."""
    def f(t, x, y, W, H, i):
        r, th = polar(x, y)
        R = 0.08 + 0.86 * ease_out(t, 2.4)
        fade = (1 - t) ** 1.4
        wv = 0.018 + 0.05 * (1 - t)
        a = 1.4 * np.exp(-((r - R) / wv) ** 2)
        a += 0.45 * np.exp(-np.maximum(R - r, 0) / (0.12 + 0.1 * t)) * (r < R)
        R2 = 0.05 + 0.6 * ease_out(max(0, t - 0.1) * 1.2, 2.4)
        a += 0.5 * np.exp(-((r - R2) / 0.02) ** 2) * (t > 0.1)
        a += 1.6 * np.exp(-(r / 0.12) ** 2) * (1 - t) ** 4
        return bloom(a * fade, W, 0.7)
    return f


def burst():
    """Лучи для легендарных карт и героев: веер лучей вращается, ядро, волна, искры-звёздочки."""
    n = 30
    ra = RNG.uniform(0, 2 * np.pi, n)
    rw = RNG.uniform(0.015, 0.07, n)
    ri = RNG.uniform(0.4, 1.0, n)
    rl = RNG.uniform(0.6, 1.0, n)
    tw = RNG.uniform(-0.75, 0.75, (8, 2))
    tw_t = RNG.uniform(0.15, 0.6, 8)

    def f(t, x, y, W, H, i):
        r, th = polar(x, y)
        ev = env(t, 0.15, 0.6)
        rot = 0.5 * t
        rays = np.zeros_like(r)
        for k in range(n):
            d = angdiff(th, ra[k] + rot * (1 if k % 2 else -0.6))
            rays += ri[k] * np.exp(-(d / rw[k]) ** 2)
        L = 0.95 * ease_out(t * 2.2) * 1.0
        rays *= np.exp(-r / (0.28 + 0.3 * ease_out(t * 2))) * (r < L + 0.1)
        a = 0.9 * rays * ev
        a += 2.0 * np.exp(-(r / (0.13 + 0.06 * math.sin(t * 9))) ** 2) * ev
        R = 0.1 + 0.85 * ease_out(t * 1.6)
        a += 0.8 * np.exp(-((r - R) / 0.02) ** 2) * (1 - t) ** 1.5
        # звёздочки-блики
        for k in range(8):
            lt = (t - tw_t[k]) / 0.3
            if 0 < lt < 1:
                s = math.sin(lt * math.pi)
                dx, dy = x - tw[k, 0], y - tw[k, 1]
                a += 1.3 * s * (np.exp(-(dx / 0.006) ** 2 - (dy / 0.07) ** 2) + np.exp(-(dy / 0.006) ** 2 - (dx / 0.07) ** 2))
        return bloom(a, W, 0.85)
    return f


def circle():
    """Тактический рунный круг: кольца рисуются по дуге, засечки и узлы, внутренний шестиугольник вращается навстречу."""
    def ring(r, R, w): return np.exp(-((r - R) / w) ** 2)

    def f(t, x, y, W, H, i):
        r, th = polar(x, y)
        draw = ease_out(t / 0.35, 2)
        ev = env(t, 0.08, 0.72)
        rot1, rot2 = 0.9 * t, -1.3 * t
        a = np.zeros_like(r)
        # дорисовка по дуге: от верха по часовой
        def drawn(rot):
            ang = (np.pi / 2 - (th - rot)) % (2 * np.pi)
            return ang <= draw * 2 * np.pi + 1e-6
        m1 = drawn(rot1)
        a += 1.1 * ring(r, 0.9, 0.008) * m1
        a += 0.6 * ring(r, 0.84, 0.004) * m1
        # засечки
        tick = (np.abs(((th - rot1) * 60 / (2 * np.pi)) % 1 - 0.5) > 0.42) * (r > 0.74) * (r < 0.81)
        a += 0.9 * tick * m1
        # руны: короткие дуги-штрихи на радиусе 0.68
        seg = (np.sin((th + rot2) * 9) > 0.55) * ring(r, 0.68, 0.012)
        a += 0.9 * seg * drawn(-rot2)
        # шестиугольник и треугольники внутри
        m2 = drawn(rot2)
        for k, (R, w, sides, amp) in enumerate([(0.5, 0.008, 6, 1.2), (0.36, 0.006, 3, 0.9)]):
            ph = th - rot2 * (1 if k == 0 else -1.4)
            sec = np.cos(np.pi / sides) / np.cos(((ph % (2 * np.pi / sides)) - np.pi / sides))
            a += amp * ring(r, R * sec, w) * m2
        # узлы на шестиугольнике
        for k in range(6):
            ang = rot2 + k * np.pi / 3
            nx, ny = 0.5 * math.cos(ang), 0.5 * math.sin(ang)
            a += 1.5 * np.exp(-(((x - nx) ** 2 + (y - ny) ** 2) / 0.0012)) * (draw > k / 6)
        # вспышка в момент замыкания круга
        pulse = math.exp(-((t - 0.36) / 0.06) ** 2)
        a *= 0.75 + 0.6 * pulse
        a += 0.25 * np.exp(-(r / 0.55) ** 2) * pulse
        return bloom(a * ev, W, 0.9, 0.008, 0.04)
    return f


def pillar():
    """Столб света снизу вверх: усиление, лечение, появление; кадр 1:2, у основания — эллипс-круг."""
    n = 12
    sx = RNG.uniform(-0.55, 0.55, n)
    sp = RNG.uniform(0, 1, n)
    sv = RNG.uniform(0.8, 1.4, n)

    def f(t, x, y, W, H, i):
        # y: от −2 (низ) до 2 (верх) при кадре 1:2
        ev = env(t, 0.15, 0.55)
        top = -1.6 + 3.6 * ease_out(t * 1.8)
        wcol = 0.32 * (1 - 0.5 * t)
        col = np.exp(-(x / wcol) ** 2) * (y < top) * np.exp(-np.maximum(y - (-1.6), 0) / 2.4)
        core = np.exp(-(x / (wcol * 0.25)) ** 2) * (y < top) * np.exp(-np.maximum(y + 1.6, 0) / 1.6)
        a = 0.6 * col + 1.1 * core
        # основание: эллипс
        ey = -1.6
        er = np.sqrt((x / 0.75) ** 2 + ((y - ey) / 0.16) ** 2)
        a += 1.2 * np.exp(-((er - 0.6 - 0.3 * t) / 0.08) ** 2) + 1.0 * np.exp(-(er / 0.5) ** 2)
        # восходящие штрихи
        for k in range(n):
            yy = -1.6 + ((sp[k] + t * sv[k] * 1.4) % 1.0) * 3.4
            a += 1.2 * np.exp(-((x - sx[k] * 0.9) / 0.012) ** 2) * np.exp(-((y - yy) / 0.14) ** 2) * (yy < top + 0.2)
        return bloom(a * ev, W, 0.8)
    return f


def lightning():
    """Электрический разряд: ветвистые молнии из центра (новый рисунок каждые два кадра), кольцо, ядро."""
    cache = {}

    def bolt(draw, x0, y0, x1, y1, width, depth, rng):
        pts = [(x0, y0), (x1, y1)]
        disp = math.hypot(x1 - x0, y1 - y0) * 0.35
        for _ in range(5):
            new = [pts[0]]
            for (ax, ay), (bx, by) in zip(pts, pts[1:]):
                mx, my = (ax + bx) / 2, (ay + by) / 2
                nx, ny = -(by - ay), bx - ax
                l = math.hypot(nx, ny) + 1e-6
                o = rng.normal(0, disp)
                new += [(mx + nx / l * o, my + ny / l * o), (bx, by)]
            pts = new
            disp *= 0.5
        draw.line(pts, fill=255, width=max(1, int(width)))
        if depth > 0:
            for _ in range(2):
                j = rng.integers(len(pts) // 4, len(pts) * 3 // 4)
                px, py = pts[j]
                ang = math.atan2(py - y0, px - x0) + rng.normal(0, 0.7)
                L = math.hypot(x1 - x0, y1 - y0) * rng.uniform(0.25, 0.45)
                bolt(draw, px, py, px + math.cos(ang) * L, py + math.sin(ang) * L, width * 0.6, depth - 1, rng)

    def f(t, x, y, W, H, i):
        key = i // 2
        if key not in cache:
            rng = np.random.default_rng(100 + key)
            im = Image.new('L', (W, H), 0)
            d = ImageDraw.Draw(im)
            for k in range(3):
                ang = rng.uniform(0, 2 * np.pi)
                L = rng.uniform(0.65, 0.95) * W / 2
                bolt(d, W / 2, H / 2, W / 2 + math.cos(ang) * L, H / 2 + math.sin(ang) * L, 3.4 * SS, 1, rng)
            cache[key] = np.asarray(im).astype(np.float64) / 255.0
        r, th = polar(x, y)
        ev = env(t, 0.06, 0.6)
        flick = 0.75 + 0.25 * math.sin(i * 2.7)
        a = 1.6 * cache[key] * ev * flick
        a += 1.4 * np.exp(-(r / 0.1) ** 2) * ev
        R = 0.15 + 0.7 * ease_out(t * 1.4)
        a += 0.7 * np.exp(-((r - R) / 0.015) ** 2) * (1 - t) ** 1.5
        return bloom(a, W, 0.9)
    return f


def sparkle():
    """Блёстки: четырёхлучевые звёздочки вспыхивают и гаснут по очереди, чуть поднимаясь."""
    n = 14
    pos = RNG.uniform(-0.7, 0.7, (n, 2))
    t0 = RNG.uniform(0, 0.6, n)
    sz = RNG.uniform(0.6, 1.2, n)

    def f(t, x, y, W, H, i):
        a = np.zeros_like(x)
        for k in range(n):
            lt = (t - t0[k]) / 0.35
            if not (0 < lt < 1): continue
            s = math.sin(lt * math.pi) * sz[k]
            px, py = pos[k, 0], pos[k, 1] + 0.25 * lt
            dx, dy = x - px, y - py
            arm = 0.16 * s
            a += 1.5 * s * (np.exp(-(dx / 0.006) ** 2 - (dy / arm) ** 2) + np.exp(-(dy / 0.006) ** 2 - (dx / arm) ** 2))
            a += 1.2 * s * np.exp(-(dx * dx + dy * dy) / 0.0009)
        return bloom(a, W, 0.9)
    return f


def hexshield():
    """Энергощит: по сотам бежит волна от центра, кромка купола вспыхивает."""
    def f(t, x, y, W, H, i):
        r, th = polar(x, y)
        s = 0.11
        # расстояние до рёбер шестиугольной сетки
        qx = x / (s * 1.5)
        qy = (y - x * 0 ) / (s * math.sqrt(3))
        def hexdist(px, py):
            px, py = np.abs(px), np.abs(py)
            return np.maximum(px * 0.866025 + py * 0.5, py)
        best = np.full_like(x, 9.0)
        cxs = np.round(x / (s * 1.5))
        for ox in (-1, 0, 1):
            cx = (cxs + ox)
            off = (cx % 2) * 0.5
            cy = np.round(y / (s * math.sqrt(3)) - off)
            for oy in (-1, 0, 1):
                hx = cx * s * 1.5
                hy = (cy + oy + off) * s * math.sqrt(3)
                best = np.minimum(best, hexdist(x - hx, y - hy))
        edge = np.exp(-((best - s * 0.866) / 0.006) ** 2)
        R = 0.05 + 0.95 * ease_out(t, 2)
        wave = np.exp(-((r - R) / 0.16) ** 2)
        dome = r < 0.95
        a = (1.3 * edge * wave + 0.12 * edge) * dome
        a += 1.0 * np.exp(-((r - 0.95) / 0.02) ** 2) * (0.4 + 0.6 * wave.max())
        a += 0.25 * np.exp(-((r - R) / 0.05) ** 2) * dome
        return bloom(a * (1 - t) ** 1.2, W, 0.8)
    return f


EFFECTS = [
    # имя, колонок, рядов, ширина кадра, высота кадра, функция
    ('impact', 4, 4, 256, 256, impact),
    ('slash', 4, 4, 256, 128, slash),
    ('shock', 4, 4, 256, 256, shock),
    ('burst', 6, 6, 192, 192, burst),
    ('circle', 6, 6, 256, 256, circle),
    ('pillar', 4, 4, 128, 256, pillar),
    ('lightning', 4, 4, 256, 256, lightning),
    ('sparkle', 4, 4, 256, 256, sparkle),
    ('hexshield', 4, 4, 256, 256, hexshield),
]


def preview(prev, path, colors):
    """Лист проверки: кадры каждого эффекта, раскрашенные как в игре (ядро — белое, края — в цвет)."""
    rows = []
    for (name, frames), col in zip(prev, colors):
        idx = np.linspace(0, len(frames) - 1, 8).round().astype(int)
        tiles = []
        for j in idx:
            a = frames[j]
            h, w = a.shape
            scale = 160 / max(h, w)
            c = np.array(col)
            core = np.clip((a - 0.55) / 0.45, 0, 1)[..., None]
            rgb = (c * (1 - core) + core) * a[..., None] * 1.4
            bg = np.array([0.05, 0.07, 0.1])
            out = np.clip(bg + rgb, 0, 1)
            im = Image.fromarray((out * 255).astype(np.uint8)).resize((max(1, int(w * scale)), max(1, int(h * scale))))
            tile = Image.new('RGB', (170, 170), (12, 17, 25))
            tile.paste(im, ((170 - im.width) // 2, (170 - im.height) // 2))
            tiles.append(tile)
        row = Image.new('RGB', (170 * 8, 170))
        for k, tl in enumerate(tiles): row.paste(tl, (k * 170, 0))
        rows.append(row)
    sheet_img = Image.new('RGB', (170 * 8, 170 * len(rows)))
    for k, rw in enumerate(rows): sheet_img.paste(rw, (0, k * 170))
    sheet_img.save(path, quality=90)


if __name__ == '__main__':
    out = sys.argv[1]
    prev_dir = sys.argv[2] if len(sys.argv) > 2 else None
    os.makedirs(out, exist_ok=True)
    prev = [] if prev_dir else None
    only = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None
    for name, c, r, fw, fh, fn in EFFECTS:
        if only and name not in only: continue
        sheet(name, c, r, fw, fh, fn(), out, prev)
    if prev_dir:
        cols = {'impact': (1, .55, .2), 'slash': (1, .75, .3), 'shock': (.4, .9, 1), 'burst': (1, .8, .35), 'circle': (.45, .85, 1),
                'pillar': (1, .82, .35), 'lightning': (.55, .8, 1), 'sparkle': (1, .85, .45), 'hexshield': (.45, .8, 1)}
        preview(prev, os.path.join(prev_dir, 'fx_preview.jpg'), [cols[n] for n, _ in prev])
