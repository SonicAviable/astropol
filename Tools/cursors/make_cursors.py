# Курсор игры и его эффекты -> Assets/Resources/UI/Cursors/ (собирает и анимирует GameCursor.cs).
#   cursor_arrow.png       — голографическая стрелка: перелив белый -> бирюзовый -> фиолетовый, грани, светлая кромка (обычное смешивание)
#   cursor_arrow_glow.png  — размытое свечение стрелки
#   fx_sparkle.png         — четырёхлучевая звёздочка на острие
#   fx_dot.png             — мягкая точка: звёздная пыль шлейфа, искры клика, спутник орбиты
#   fx_ring.png            — орбита с разрывами и засечками (наведение)
#   fx_ripple.png          — мягкое кольцо ударной волны клика
#   cursor_target.png      — прицел приказа флоту (обычное смешивание, с тёмной обводкой)
# Свечения (glow, fx_*) — светлое на чёрном: шейдер UIAdditive берёт только цвет, без альфы.
# Запуск: python3 make_cursors.py
import os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Resources', 'UI', 'Cursors')
SS = 4
WHITE = np.array([255, 255, 255.]); CYAN = np.array([110, 228, 255.]); BLUE = np.array([60, 120, 255.]); VIOLET = np.array([170, 110, 255.])

# Стрелка в единицах 0..1 (остриё — ARROW[0]); TIP — остриё в долях картинки (GameCursor.ArrowTip)
ARROW = [(0.10, 0.06), (0.10, 0.80), (0.29, 0.645), (0.42, 0.93), (0.55, 0.875), (0.425, 0.60), (0.68, 0.60)]
FACET = [(0.10, 0.06), (0.10, 0.80), (0.29, 0.645), (0.30, 0.42)]     # левая, светлая грань
TIP = ARROW[0]

def poly(n, pts): return [(x * n, y * n) for x, y in pts]

def mask(n, pts, blur=0):
    m = Image.new('L', (n, n), 0); ImageDraw.Draw(m).polygon(poly(n, pts), fill=255)
    return m.filter(ImageFilter.GaussianBlur(blur)) if blur else m

def grad(n, stops):
    """Перелив вдоль оси от острия к хвосту."""
    y, x = np.mgrid[0:n, 0:n] / n
    t = np.clip(((x - TIP[0]) * 0.45 + (y - TIP[1]) * 0.9) / 0.85, 0, 1)
    out = np.zeros((n, n, 3))
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        k = np.clip((t - t0) / (t1 - t0), 0, 1)[..., None]
        sel = ((t >= t0) & (t <= t1))[..., None]
        out = np.where(sel, c0 * (1 - k) + c1 * k, out)
    return out

def save(arr_rgb, alpha, name, size):
    img = Image.fromarray(np.dstack([np.clip(arr_rgb, 0, 255), np.clip(alpha, 0, 255)]).astype(np.uint8), 'RGBA')
    img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name + '.png'))

def arrow(size=128):
    n = size * SS
    body = np.asarray(mask(n, ARROW)) / 255.
    facet = np.asarray(mask(n, FACET)) / 255.
    rgb = grad(n, [(0, WHITE), (0.25, CYAN), (0.7, BLUE), (1, VIOLET)])
    rgb = rgb * (0.62 + 0.38 * facet[..., None])              # правая грань темнее — объём кристалла
    # светлая кромка: контур стрелки
    edge = Image.new('L', (n, n), 0)
    ImageDraw.Draw(edge).line(poly(n, ARROW + [ARROW[0]]), fill=255, width=int(n * 0.03), joint='curve')
    e = np.asarray(edge.filter(ImageFilter.GaussianBlur(n * 0.004))) / 255.
    rgb = rgb * (1 - e[..., None]) + (WHITE * 0.85 + CYAN * 0.15) * e[..., None]
    # тонкая тёмная обводка снаружи — читается на светлом интерфейсе
    out = np.asarray(mask(n, ARROW).filter(ImageFilter.MaxFilter(int(n * 0.03) | 1))) / 255.
    out = np.asarray(Image.fromarray((out * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(n * 0.006))) / 255.
    alpha = np.maximum(body * 0.93, out * 0.75)
    col = rgb * body[..., None] + np.array([6, 10, 24.]) * (1 - body[..., None])
    alpha = np.maximum(alpha, e * body.max())
    save(col, alpha * 255, 'cursor_arrow', size)

    g = np.asarray(mask(n, ARROW, n * 0.06)) / 255.
    glow = grad(n, [(0, CYAN), (0.5, BLUE), (1, VIOLET)]) * (g[..., None] * 1.6)
    save(glow, np.clip(g * 1.6, 0, 1) * 255, 'cursor_arrow_glow', size)

def sparkle(size=128):
    n = size * SS
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n - 0.5
    r = np.hypot(x, y) + 1e-6
    ax = np.exp(-np.abs(y) / 0.010) * np.clip(1 - np.abs(x) / 0.5, 0, 1) ** 2
    ay = np.exp(-np.abs(x) / 0.010) * np.clip(1 - np.abs(y) / 0.5, 0, 1) ** 2
    d1 = np.exp(-np.abs(x - y) / 0.012) * np.clip(1 - r / 0.25, 0, 1) ** 2
    d2 = np.exp(-np.abs(x + y) / 0.012) * np.clip(1 - r / 0.25, 0, 1) ** 2
    core = np.exp(-(r / 0.05) ** 2) + 0.35 * np.exp(-(r / 0.13) ** 2)
    v = np.clip(ax + ay + 0.45 * (d1 + d2) + core, 0, 1)
    save(np.dstack([v, v, v]) * 255, v * 255, 'fx_sparkle', size)

def dot(size=64):
    n = size * SS
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n - 0.5
    r = np.hypot(x, y) / 0.5
    v = np.clip(np.exp(-(r / 0.28) ** 2) + 0.25 * np.exp(-(r / 0.6) ** 2) * (r < 1), 0, 1)
    save(np.dstack([v, v, v]) * 255, v * 255, 'fx_dot', size)

def ring(size=256):
    n = size * SS
    img = Image.new('L', (n, n), 0); d = ImageDraw.Draw(img)
    c, R, w = n / 2, n * 0.40, int(n * 0.018)
    for a0, a1 in ((-80, 40), (55, 160), (175, 265)):          # орбита из трёх дуг
        d.arc([c - R, c - R, c + R, c + R], a0, a1, fill=255, width=w)
    for a in (0, 90, 180, 270):                                 # засечки снаружи
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        d.line([(c + ca * R * 1.08, c + sa * R * 1.08), (c + ca * R * 1.2, c + sa * R * 1.2)], fill=200, width=w)
    sharp = np.asarray(img) / 255.
    soft = np.asarray(img.filter(ImageFilter.GaussianBlur(n * 0.02))) / 255.
    v = np.clip(sharp + soft * 1.2, 0, 1)
    save(np.dstack([v, v, v]) * 255, v * 255, 'fx_ring', size)

def ripple(size=256):
    n = size * SS
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n - 0.5
    r = np.hypot(x, y)
    v = np.exp(-((r - 0.40) / 0.022) ** 2) + 0.35 * np.exp(-((r - 0.37) / 0.06) ** 2)
    v = np.clip(v, 0, 1)
    save(np.dstack([v, v, v]) * 255, v * 255, 'fx_ripple', size)

def target(size=128):
    n = size * SS
    def draw(d, col, w):
        c, R = n / 2, n * 0.30
        for a0 in (15, 105, 195, 285):
            d.arc([c - R, c - R, c + R, c + R], a0, a0 + 60, fill=col, width=w)
        for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
            d.polygon([(c + dx * n * 0.47 - dy * n * 0.03, c + dy * n * 0.47 + dx * n * 0.03),
                       (c + dx * n * 0.47 + dy * n * 0.03, c + dy * n * 0.47 - dx * n * 0.03),
                       (c + dx * n * 0.34, c + dy * n * 0.34)], fill=col)        # клинья к центру
        d.ellipse([c - n * 0.035, c - n * 0.035, c + n * 0.035, c + n * 0.035], fill=col)
    dark = Image.new('L', (n, n), 0); draw(ImageDraw.Draw(dark), 255, int(n * 0.07))
    lite = Image.new('L', (n, n), 0); draw(ImageDraw.Draw(lite), 255, int(n * 0.032))
    dk = np.asarray(dark.filter(ImageFilter.GaussianBlur(n * 0.01))) / 255.
    lt = np.asarray(lite) / 255.
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n - 0.5
    t = np.clip(np.hypot(x, y) / 0.5, 0, 1)[..., None]
    col = (CYAN * (1 - t) + VIOLET * t) * 0.6 + WHITE * 0.4
    rgb = col * lt[..., None] + np.array([6, 10, 24.]) * (1 - lt[..., None])
    alpha = np.maximum(lt, dk * 0.7)
    save(rgb, alpha * 255, 'cursor_target', size)

os.makedirs(OUT, exist_ok=True)
arrow(); sparkle(); dot(); ring(); ripple(); target()
print('ok ->', os.path.normpath(OUT))
