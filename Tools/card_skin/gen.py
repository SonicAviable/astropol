"""Генератор текстур интерфейса карточного боя (стиль «дорогой» CCG: металл с фасками, самоцветы, гнёзда).
python3 gen.py <out_dir>"""
import sys, os, math
import numpy as np
from PIL import Image, ImageFilter

D = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(D, 'dl', 'tex')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, 'out')
os.makedirs(OUT, exist_ok=True)
SS = 2  # суперсэмплинг

def load(path, size):
    im = Image.open(path).convert('RGB').resize(size, Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0

def tile(arr, w, h):
    th, tw = arr.shape[:2]
    reps = (h // th + 1, w // tw + 1) + ((1,) if arr.ndim == 3 else ())
    return np.tile(arr, reps)[:h, :w]

METAL_C = load(os.path.join(TEX, 'Metal046B', 'Metal046B_1K-JPG_Color.jpg'), (512, 512))
METAL_N = load(os.path.join(TEX, 'Metal046B', 'Metal046B_1K-JPG_NormalGL.jpg'), (512, 512)) * 2 - 1
LEATHER = load(os.path.join(TEX, 'Leather026', 'Leather026_1K-JPG_Color.jpg'), (512, 512))
LEATHER_N = load(os.path.join(TEX, 'Leather026', 'Leather026_1K-JPG_NormalGL.jpg'), (512, 512)) * 2 - 1
PLATES_N = load(os.path.join(TEX, 'MetalPlates006', 'MetalPlates006_1K-JPG_NormalGL.jpg'), (512, 512)) * 2 - 1

def grid(w, h):
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    return x + 0.5, y + 0.5

# ---------------------------------------------------------------- SDF
def sd_roundrect(x, y, cx, cy, hw, hh, r):
    qx = np.abs(x - cx) - (hw - r)
    qy = np.abs(y - cy) - (hh - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r

def sd_circle(x, y, cx, cy, r):
    return np.hypot(x - cx, y - cy) - r

def sd_poly(x, y, pts):
    """Знаковое расстояние до выпуклого/невыпуклого многоугольника."""
    d = np.full(x.shape, 1e9, np.float32)
    s = np.ones(x.shape, np.float32)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]; bx, by = pts[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        wx, wy = x - ax, y - ay
        t = np.clip((wx * ex + wy * ey) / (ex * ex + ey * ey), 0, 1)
        dx, dy = wx - ex * t, wy - ey * t
        d = np.minimum(d, dx * dx + dy * dy)
        c1 = y >= ay; c2 = y < by; c3 = ex * wy > ey * wx
        flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
        s = np.where(flip, -s, s)
    return s * np.sqrt(d)

def hexagon(cx, cy, r, rot=0.0):
    return [(cx + r * math.cos(rot + i * math.pi / 3), cy + r * math.sin(rot + i * math.pi / 3)) for i in range(6)]

def smooth(e0, e1, v):
    t = np.clip((v - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)

def cov(d, aa=1.0):
    return np.clip(0.5 - d / aa, 0, 1)

# ---------------------------------------------------------------- освещение
L = np.array([-0.45, -0.55, 0.70]); L /= np.linalg.norm(L)    # свет сверху-слева (y вниз в картинке)
V = np.array([0, 0, 1.0]); H = (L + V); H /= np.linalg.norm(H)

def normals_from_height(h, strength):
    gy, gx = np.gradient(h)
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(h)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    return nx / n, ny / n, nz / n

def shade(albedo, h, strength=3.0, detail=None, detail_k=0.35, spec_pow=40, spec_k=0.9, amb=0.35, env=0.25):
    nx, ny, nz = normals_from_height(h, strength)
    if detail is not None:
        nx = nx + detail[..., 0] * detail_k; ny = ny - detail[..., 1] * detail_k
        n = np.sqrt(nx * nx + ny * ny + nz * nz); nx, ny, nz = nx / n, ny / n, nz / n
    diff = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)
    spec = np.clip(nx * H[0] + ny * H[1] + nz * H[2], 0, 1) ** spec_pow
    # «окружение»: верх светлее, низ темнее — металл читается как объём
    envl = np.clip(0.5 - ny * 0.5, 0, 1)
    col = albedo * (amb + diff[..., None] * 0.85 + envl[..., None] * env) + spec[..., None] * spec_k
    return col

def save(name, rgb, a, size=None):
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])
    im = Image.fromarray((img * 255).astype(np.uint8), 'RGBA')
    if size: im = im.resize(size, Image.LANCZOS)
    im.save(os.path.join(OUT, name + '.png'))
    return im

def metal(w, h, tint, rough=1.0):
    c = tile(METAL_C, w, h)
    lum = c.mean(axis=2, keepdims=True)
    base = (0.55 + (lum - lum.mean()) * 1.6 * rough) * np.array(tint)
    return np.clip(base, 0, 1.5)

# ================================================================ РАМКИ
def frame_ring(name, W, Hh, r, ring, tint=(0.92, 0.92, 0.95), inner_line=True):
    """Металлическое кольцо-рамка (центр прозрачный): внешняя фаска, канавка, внутренняя кромка."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    d = sd_roundrect(x, y, w / 2, h / 2, w / 2 - 2 * SS, h / 2 - 2 * SS, r * SS)
    rin = ring * SS
    a_out = cov(d, SS)
    a_in = cov(-(d + rin), SS)
    a = a_out * a_in
    t = np.clip(-d / rin, 0, 1)                      # 0 снаружи → 1 у внутреннего края
    # профиль: скат наружу, плато, канавка, скат внутрь
    hgt = smooth(0, 0.22, t) * 1.0 - 0.55 * np.exp(-((t - 0.62) / 0.07) ** 2) + 0.15 * smooth(0.75, 0.9, t) - smooth(0.88, 1.0, t) * 0.9
    det = tile(METAL_N, w, h)
    col = shade(metal(w, h, tint), hgt * 6 * SS, strength=1.0, detail=det, detail_k=0.25, spec_pow=28, spec_k=0.75)
    if inner_line:
        line = np.exp(-((t - 0.62) / 0.03) ** 2)
        col = col * (1 - line[..., None] * 0.6)
    # тёмная обводка снаружи
    edge = np.exp(-((d + 1.0 * SS) / (1.2 * SS)) ** 2)
    col = col * (1 - edge[..., None] * 0.5)
    save(name, col, a, (W, Hh))

def panel(name, W, Hh, r, rim, base=(0.06, 0.075, 0.09), rim_tint=(0.8, 0.82, 0.86), tex='metal', inset=True):
    """Сплошная панель: металлическая кромка с фаской и тёмное поле (металл/кожа) с внутренней тенью."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    d = sd_roundrect(x, y, w / 2, h / 2, w / 2 - 2 * SS, h / 2 - 2 * SS, r * SS)
    a = cov(d, SS)
    rr = rim * SS
    t = np.clip(-d / rr, 0, 1)
    hgt = smooth(0, 0.35, t) - smooth(0.7, 1.0, t) * 1.2
    det = tile(METAL_N, w, h)
    rimc = shade(metal(w, h, rim_tint), hgt * 5 * SS, strength=1.0, detail=det, detail_k=0.3, spec_pow=26, spec_k=0.7)
    if tex == 'leather':
        f = tile(LEATHER, w, h); fl = f.mean(axis=2, keepdims=True)
        field = np.array(base) * (0.75 + (fl - fl.mean()) * 3.0)
        fn = tile(LEATHER_N, w, h)
        field = shade(field, np.zeros((h, w), np.float32), detail=fn, detail_k=0.8, spec_pow=12, spec_k=0.05, amb=0.6, env=0.3)
    elif tex == 'plates':
        fn = tile(PLATES_N, w, h)
        f = metal(w, h, (1, 1, 1)) * np.array(base) * 2.2
        field = shade(f, np.zeros((h, w), np.float32), detail=fn, detail_k=0.9, spec_pow=20, spec_k=0.12, amb=0.6, env=0.25)
    else:
        f = metal(w, h, (1, 1, 1)) * np.array(base) * 2.0
        field = shade(f, np.zeros((h, w), np.float32), detail=det, detail_k=0.5, spec_pow=20, spec_k=0.08, amb=0.6, env=0.25)
    if inset:
        dd = -(d + rr)                                   # расстояние внутрь от края поля
        sh = np.exp(-np.clip(dd, 0, None) / (10 * SS))   # внутренняя тень
        field = field * (1 - sh[..., None] * 0.65)
    m = smooth(0.92, 1.0, t)[..., None]
    col = rimc * (1 - m) + field * m
    save(name, col, a, (W, Hh))

# ================================================================ САМОЦВЕТЫ
def gem(name, S, shape, color, facets=6, socket=(0.75, 0.72, 0.68), glow=0.5, socket_w=0.13):
    """Самоцвет в металлической оправе. shape: 'hex' | 'diamond' | 'shield' | 'circle' | 'drop'."""
    w = h = S * SS
    x, y = grid(w, h)
    c = w / 2
    R = w / 2 - 3 * SS
    if shape == 'hex':
        d = sd_poly(x, y, hexagon(c, c, R, -math.pi / 2))
    elif shape == 'diamond':
        d = sd_poly(x, y, [(c, c - R), (c + R * 0.86, c), (c, c + R), (c - R * 0.86, c)])
    elif shape == 'shield':
        pts = [(c - R * 0.85, c - R * 0.82), (c, c - R * 0.98), (c + R * 0.85, c - R * 0.82), (c + R * 0.8, c + R * 0.05),
               (c + R * 0.45, c + R * 0.62), (c, c + R * 0.98), (c - R * 0.45, c + R * 0.62), (c - R * 0.8, c + R * 0.05)]
        d = sd_poly(x, y, pts)
    else:
        d = sd_circle(x, y, c, c, R)
    a = cov(d, SS)
    sw = R * socket_w
    t = np.clip(-d / sw, 0, 1)
    # оправа: выпуклый металл
    hs = np.sin(np.clip(t, 0, 1) * math.pi) * 1.0
    det = tile(METAL_N, w, h)
    sock = shade(metal(w, h, socket), hs * 4 * SS, strength=1.0, detail=det, detail_k=0.2, spec_pow=30, spec_k=0.8)
    # камень: грани по углу + радиальная выпуклость
    gx, gy = x - c, y - c
    ang = np.arctan2(gy, gx)
    rr = np.hypot(gx, gy) / R
    seg = (np.floor((ang + math.pi) / (2 * math.pi) * facets) + 0.5) / facets * 2 * math.pi - math.pi
    fx, fy = np.cos(seg), np.sin(seg)
    inner = rr < 0.42
    tilt = np.where(inner, 0.0, 0.55)
    nx = np.where(inner, 0, fx * tilt); ny = np.where(inner, 0, fy * tilt); nz = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz); nx, ny, nz = nx / n, ny / n, nz / n
    diff = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)
    spec = np.clip(nx * H[0] + ny * H[1] + nz * H[2], 0, 1) ** 60
    col = np.array(color)
    dark = col * 0.25
    stone = dark + (col - dark) * (diff[..., None] ** 1.4) + spec[..., None] * 0.9
    # внутреннее свечение снизу (преломление), блик сверху-слева
    stone += col * glow * np.exp(-(((gx / R) * 1.2) ** 2 + ((gy / R - 0.45) / 0.45) ** 2))[..., None]
    hl = np.exp(-(((gx / R + 0.35) / 0.18) ** 2 + ((gy / R + 0.4) / 0.12) ** 2))
    stone += hl[..., None] * 0.8
    m = smooth(0.9, 1.0, t)[..., None]
    stone = stone * (1 - np.exp(-np.clip(-(d + sw), 0, None) / (2.5 * SS))[..., None] * 0.6)   # тень от оправы
    col = sock * (1 - m) + stone * m
    save(name, col, a, (S, S))

def orb(name, S, color, ring_tint=(0.85, 0.83, 0.8)):
    """Гнездо силы сектора: массивное кольцо и стеклянная сфера с числом (число рисует игра)."""
    w = h = S * SS
    x, y = grid(w, h)
    c = w / 2; R = w / 2 - 3 * SS
    d = sd_circle(x, y, c, c, R)
    a = cov(d, SS)
    ringw = R * 0.2
    t = np.clip(-d / ringw, 0, 1)
    hs = np.sin(t * math.pi) * 1.0 + 0.3 * np.cos(np.arctan2(y - c, x - c) * 12) * (t > 0.2) * (t < 0.8)  # насечки
    det = tile(METAL_N, w, h)
    ring = shade(metal(w, h, ring_tint), hs * 4 * SS, strength=1.0, detail=det, detail_k=0.2, spec_pow=26, spec_k=0.75)
    gx, gy = (x - c) / (R - ringw), (y - c) / (R - ringw)
    rr2 = np.clip(gx * gx + gy * gy, 0, 1)
    z = np.sqrt(1 - rr2)
    col = np.array(color)
    diff = np.clip(gx * L[0] + gy * L[1] + z * L[2], 0, 1)
    glass = col * 0.12 + col * 0.45 * (1 - z)[..., None] ** 0.6 + col * 0.35 * np.exp(-((gx) ** 2 + ((gy - 0.5) / 0.6) ** 2) * 2.5)[..., None]
    spec = np.clip(gx * H[0] + gy * H[1] + z * H[2], 0, 1) ** 80
    hl = np.exp(-(((gx + 0.32) / 0.28) ** 2 + ((gy + 0.42) / 0.16) ** 2))
    glass = glass + spec[..., None] * 0.6 + hl[..., None] * 0.55
    glass = glass * (1 - np.exp(-np.clip(-(d + ringw), 0, None) / (3 * SS))[..., None] * 0.7)
    m = smooth(0.92, 1.0, t)[..., None]
    out = ring * (1 - m) + glass * m
    save(name, out, a, (S, S))

def end_button(name, W, Hh, color, ring_tint=(0.9, 0.82, 0.62)):
    """Кнопка хода: овальный самоцвет в массивной бронзовой оправе (как в Hearthstone)."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    d = sd_roundrect(x, y, w / 2, h / 2, w / 2 - 3 * SS, h / 2 - 3 * SS, h / 2 - 3 * SS)
    a = cov(d, SS)
    ringw = 14 * SS
    t = np.clip(-d / ringw, 0, 1)
    hs = smooth(0, 0.4, t) - smooth(0.75, 1, t) * 0.9 + 0.25 * np.exp(-((t - 0.55) / 0.08) ** 2)
    det = tile(METAL_N, w, h)
    ring = shade(metal(w, h, ring_tint), hs * 6 * SS, strength=1.0, detail=det, detail_k=0.25, spec_pow=26, spec_k=0.8)
    # стекло: вытянутая выпуклость
    di = -(d + ringw)
    hh = np.sqrt(np.clip(di / (h / 2 - ringw - 3 * SS), 0, 1))
    nx, ny, nz = normals_from_height(hh * 30 * SS, 1.0)
    col = np.array(color)
    diff = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)
    spec = np.clip(nx * H[0] + ny * H[1] + nz * H[2], 0, 1) ** 50
    yy = (y - h / 2) / (h / 2)
    glass = col * (0.35 + 0.65 * diff[..., None]) + col * 0.5 * np.clip(yy, 0, 1)[..., None] ** 1.5 + spec[..., None] * 0.6
    hl = np.exp(-(((y - h * 0.3) / (h * 0.09)) ** 2)) * (di > 6 * SS) * np.clip(1 - np.abs(x - w / 2) / (w * 0.38), 0, 1)
    glass = glass + hl[..., None] * 0.35
    glass = glass * (1 - np.exp(-np.clip(di, 0, None) / (4 * SS))[..., None] * 0.65)
    m = smooth(0.92, 1.0, t)[..., None]
    save(name, ring * (1 - m) + glass * m, a, (W, Hh))

def crystal(name, S, color, empty=False):
    """Кристалл очка приказов (как кристаллы маны)."""
    w = h = S * SS
    x, y = grid(w, h)
    c = w / 2; R = w / 2 - 2 * SS
    pts = [(c, c - R), (c + R * 0.72, c - R * 0.35), (c + R * 0.62, c + R * 0.55), (c, c + R), (c - R * 0.62, c + R * 0.55), (c - R * 0.72, c - R * 0.35)]
    d = sd_poly(x, y, pts)
    a = cov(d, SS)
    col = np.array(color)
    if empty:
        t = np.clip(-d / (3 * SS), 0, 1)
        rim = np.exp(-((t - 0.5) / 0.4) ** 2)
        out = np.ones((h, w, 3)) * col * 0.6
        a = a * (0.15 + rim * 0.75)
        save(name, out, a, (S, S)); return
    gx, gy = (x - c) / R, (y - c) / R
    left = gx < 0
    top = gy < -0.1
    shadeF = np.where(top, np.where(left, 1.0, 0.75), np.where(left, 0.6, 0.35))
    out = col * (0.35 + 0.75 * shadeF[..., None])
    edge = np.exp(-(gx / 0.03) ** 2) * 0.25 + np.exp(-((gy + 0.1) / 0.03) ** 2) * 0.2
    out = out + edge[..., None]
    out = out + np.exp(-(((gx + 0.22) / 0.12) ** 2 + ((gy + 0.45) / 0.12) ** 2))[..., None] * 0.9
    out = out * (1 - np.exp(-np.clip(-d, 0, None) / (2 * SS))[..., None] * 0.45)
    save(name, out, a, (S, S))

def ribbon(name, W, Hh, color):
    """Лента-баннер для имени карты и заголовков: ткань/металл с загнутыми концами."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    notch = h * 0.5
    pts = [(0, 0), (w, 0), (w - notch * 0.6, h / 2), (w, h), (0, h), (notch * 0.6, h / 2)]
    d = sd_poly(x, y, pts)
    a = cov(d, SS)
    col = np.array(color)
    yy = (y / h - 0.5) * 2
    hgt = 1 - yy ** 2
    nx, ny, nz = normals_from_height(hgt * 18 * SS, 1.0)
    det = tile(LEATHER_N, w, h)
    out = shade(col * np.ones((h, w, 3)), hgt * 18 * SS, strength=1.0, detail=det, detail_k=0.4, spec_pow=18, spec_k=0.25, amb=0.5)
    border = np.exp(-((np.abs(yy) - 0.82) / 0.05) ** 2)
    out = out + border[..., None] * np.array([0.85, 0.7, 0.4]) * 0.6
    save(name, out, a, (W, Hh))

def slot(name, W, Hh, r):
    """Пустое место под карту: вдавленная выемка с тонкой светящейся кромкой."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    d = sd_roundrect(x, y, w / 2, h / 2, w / 2 - 3 * SS, h / 2 - 3 * SS, r * SS)
    a_fill = cov(d, SS) * 0.55
    sh = np.exp(-np.clip(-d, 0, None) / (8 * SS))
    rim = np.exp(-((d + 1.5 * SS) / (1.2 * SS)) ** 2)
    col = np.zeros((h, w, 3)) + np.array([0.02, 0.03, 0.04])
    col = col * (1 - sh[..., None] * 0.6) + rim[..., None] * np.array([0.6, 0.75, 0.85]) * 0.7
    a = np.clip(a_fill + sh * 0.35 * cov(d, SS) + rim * 0.5, 0, 1)
    save(name, col, a, (W, Hh))

def hexgrid(name, S):
    """Соты энергетического щита (бесшовная плитка): белые линии с мягким свечением."""
    w = h = S * SS
    x, y = grid(w, h)
    size = w / 4.0
    # координаты сот: плитка периодична по ширине (кратно sqrt3*size)
    q = (x * math.sqrt(3) / 3 - y / 3) / size
    r = (y * 2 / 3) / size
    # к кубическим
    cx, cz = q, r; cy = -cx - cz
    rx, ry, rz = np.round(cx), np.round(cy), np.round(cz)
    dx, dy, dz = np.abs(rx - cx), np.abs(ry - cy), np.abs(rz - cz)
    mx = (dx > dy) & (dx > dz); my = (~mx) & (dy > dz)
    rx = np.where(mx, -ry - rz, rx); ry = np.where(my, -rx - rz, ry)
    fx, fz = cx - rx, cz - rz
    fy = -fx - fz
    edge = np.maximum(np.maximum(np.abs(fx - fy), np.abs(fy - fz)), np.abs(fz - fx))  # 1 на ребре
    line = np.exp(-((1 - np.clip(edge, 0, 1)) / 0.035) ** 2)
    glow = np.exp(-((1 - np.clip(edge, 0, 1)) / 0.2) ** 2) * 0.3
    a = np.clip(line + glow + 0.06, 0, 1)
    save(name, np.ones((h, w, 3)), a, (S, S))

def soft(name, S, kind):
    """Мягкие формы: свечение, кольцо, виньетка, луч."""
    w = h = S * SS
    x, y = grid(w, h)
    c = w / 2
    r = np.hypot(x - c, y - c) / c
    if kind == 'glow': a = np.exp(-(r / 0.45) ** 2)
    elif kind == 'ring': a = np.exp(-((r - 0.8) / 0.08) ** 2)
    elif kind == 'halo': a = np.exp(-((r - 0.72) / 0.22) ** 2) * (r < 1)
    elif kind == 'disc': a = np.clip((1 - r) * c / SS, 0, 1)
    else: a = np.clip(r - 0.3, 0, 1) ** 1.5
    save(name, np.ones((h, w, 3)), a, (S, S))

# ================================================================ ФЛИПБУКИ (Particle Pack) и частицы (Kenney)
def flipbooks():
    vfx = os.path.join(D, '..', '..', '..', '..', '..', 'home', 'user', 'astropol', 'Assets', 'Resources', 'VFX', 'Fire & Explosion Effects', 'Textures')
    vfx = '/home/user/astropol/Assets/Resources/VFX/Fire & Explosion Effects/Textures'
    ramp = np.asarray(Image.open(os.path.join(vfx, 'ramp01.png')).convert('RGB').resize((256, 1))).astype(np.float32)[0] / 255
    def colorize(src, out, size, mode):
        g = np.asarray(Image.open(os.path.join(vfx, src)).convert('L').resize((size, size), Image.LANCZOS)).astype(np.float32) / 255
        if mode == 'fire':
            idx = np.clip((g ** 0.8) * 255, 0, 255).astype(int)
            w = smooth(0.12, 0.55, g)[..., None]
            rgb = ramp[idx] * 1.2 * w + np.array([0.16, 0.14, 0.13]) * (1 - w)
            a = np.clip(g * 1.8, 0, 1)
        elif mode == 'smoke':
            rgb = np.ones(g.shape + (3,)) * 0.85
            a = np.clip(g * 1.4, 0, 1)
        else:
            rgb = np.ones(g.shape + (3,))
            a = np.clip(g * 1.3, 0, 1)
        save(out, rgb, a)
    colorize('Explosion.tif', 'fx_explosion', 1536, 'fire')
    colorize('SmokePuff01.tif', 'fx_smoke', 1280, 'smoke')
    colorize('EnergyEffect.tif', 'fx_energy', 1536, 'white')

def kenney():
    src = os.path.join(D, 'dl', 'particle', 'PNG (Transparent)')
    pick = {'k_smoke1': 'smoke_04', 'k_smoke2': 'smoke_07', 'k_fire1': 'fire_01', 'k_fire2': 'fire_02', 'k_flame': 'flame_03',
            'k_muzzle1': 'muzzle_01', 'k_muzzle2': 'muzzle_04', 'k_spark1': 'spark_01', 'k_spark2': 'spark_04', 'k_spark3': 'spark_06',
            'k_light': 'light_01', 'k_flare': 'flare_01', 'k_star': 'star_06', 'k_star2': 'star_04', 'k_circle': 'circle_05',
            'k_scorch': 'scorch_01', 'k_trace': 'trace_01', 'k_trace2': 'trace_04', 'k_twirl': 'twirl_02', 'k_magic': 'magic_03',
            'k_dirt': 'dirt_02', 'k_slash': 'slash_03', 'k_window': 'window_02'}
    for k, f in pick.items():
        im = Image.open(os.path.join(src, f + '.png')).convert('RGBA')
        sz = 256 if not k.startswith('k_trace') else 256
        im.resize((sz, sz), Image.LANCZOS).save(os.path.join(OUT, k + '.png'))

if __name__ == '__main__':
    what = sys.argv[2] if len(sys.argv) > 2 else 'all'
    if what in ('all', 'ui'):
        frame_ring('frame_card', 256, 256, 26, 20)
        frame_ring('frame_thin', 128, 128, 16, 9, inner_line=False)
        frame_ring('frame_round', 256, 256, 124, 22)
        panel('panel_dark', 128, 128, 18, 9)
        panel('panel_leather', 256, 256, 22, 10, base=(0.07, 0.065, 0.06), tex='leather')
        panel('panel_plates', 256, 256, 26, 14, base=(0.05, 0.06, 0.07), tex='plates')
        panel('panel_text', 128, 128, 12, 4, base=(0.09, 0.085, 0.08), rim_tint=(0.55, 0.5, 0.45), tex='leather')
        gem('gem_cost', 128, 'hex', (0.25, 0.65, 1.0), facets=6)
        gem('gem_power', 128, 'diamond', (1.0, 0.68, 0.15), facets=4)
        gem('gem_armor', 128, 'shield', (0.85, 0.22, 0.2), facets=8)
        gem('gem_kind', 64, 'circle', (0.8, 0.8, 0.85), facets=8, glow=0.2)
        orb('orb_red', 128, (1.0, 0.28, 0.2))
        orb('orb_teal', 128, (0.2, 0.9, 0.85))
        orb('orb_gray', 128, (0.55, 0.6, 0.66))
        end_button('btn_end_green', 512, 192, (0.2, 0.85, 0.35))
        end_button('btn_end_amber', 512, 192, (1.0, 0.7, 0.15))
        end_button('btn_end_gray', 512, 192, (0.35, 0.38, 0.42))
        crystal('crystal_full', 64, (0.3, 0.75, 1.0))
        crystal('crystal_empty', 64, (0.3, 0.75, 1.0), empty=True)
        ribbon('ribbon', 512, 96, (0.16, 0.13, 0.12))
        slot('slot', 128, 178, 12)
        hexgrid('hexgrid', 256)
        soft('disc', 256, 'disc'); soft('glow', 128, 'glow'); soft('ring', 256, 'ring'); soft('halo', 256, 'halo')
    if what in ('all', 'fx'):
        flipbooks(); kenney()
    print('ok', OUT)


# ================================================================ v2: свечение по контуру, купол щита, самоцвет атаки
def glow_edge(name, S=256, edge=64, r=26, inner=10.0):
    """Свечение вокруг прямоугольника со скруглением: яркое у кромки, гаснет наружу, середина пустая (9-slice 96)."""
    w = h = S * SS
    x, y = grid(w, h)
    e = edge * SS
    d = sd_roundrect(x, y, w / 2, h / 2, w / 2 - e, h / 2 - e, r * SS)
    out = np.exp(-np.clip(d, 0, None) / (e * 0.32)) * (d >= 0)
    ins = np.exp(-np.clip(-d, 0, None) / (inner * SS)) * (d < 0)
    a = np.clip(out + ins * 0.85, 0, 1) ** 1.2
    line = np.exp(-(d / (1.6 * SS)) ** 2) * 0.6
    a = np.clip(a + line, 0, 1)
    save(name, np.ones((h, w, 3)), a, (S, S))

def dome(name, W=512, Hh=256):
    """Энергетический купол: полусфера (основание снизу) с сотами, яркой кромкой (френель) и мягким основанием."""
    w, h = W * SS, Hh * SS
    x, y = grid(w, h)
    cx, cy = w / 2, h
    rx, ry = w / 2 - 4 * SS, h - 4 * SS
    nxp = (x - cx) / rx; nyp = (cy - y) / ry
    rr = np.sqrt(nxp ** 2 + nyp ** 2)
    inside = rr < 1
    z = np.sqrt(np.clip(1 - rr ** 2, 0, 1))
    fres = (1 - z) ** 2.2
    # соты, «натянутые» на сферу
    size = 22 * SS
    u = x + (x - cx) * (1 - z) * 0.35; v = y + (y - cy) * (1 - z) * 0.35
    q = (u * math.sqrt(3) / 3 - v / 3) / size; r_ = (v * 2 / 3) / size
    cxh, czh = q, r_; cyh = -cxh - czh
    rxh, ryh, rzh = np.round(cxh), np.round(cyh), np.round(czh)
    dx, dy, dz = np.abs(rxh - cxh), np.abs(ryh - cyh), np.abs(rzh - czh)
    mx = (dx > dy) & (dx > dz); my = (~mx) & (dy > dz)
    rxh = np.where(mx, -ryh - rzh, rxh); ryh = np.where(my, -rxh - rzh, ryh)
    fx, fz = cxh - rxh, czh - rzh; fy = -fx - fz
    edge = np.maximum(np.maximum(np.abs(fx - fy), np.abs(fy - fz)), np.abs(fz - fx))
    hexl = np.exp(-((1 - np.clip(edge, 0, 1)) / 0.05) ** 2)
    rim = np.exp(-((rr - 0.985) / 0.02) ** 2)
    base = smooth(0.0, 0.12, nyp)       # основание растворяется
    a = (0.035 + fres * 0.6 + hexl * (0.22 + 0.55 * fres)) * base + rim * 1.0 * base
    a = np.where(inside | (rim > 0.05), a, 0)
    col = np.dstack([0.55 + 0.45 * fres, 0.82 + 0.18 * fres, np.ones_like(fres)])
    hl = np.exp(-(((nxp + 0.35) / 0.25) ** 2 + ((nyp - 0.75) / 0.12) ** 2)) * inside
    a = np.clip(a + hl * 0.35, 0, 1)
    save(name, col, a, (W, Hh))

def gem_attack(name, S=128):
    """Атака: золотой круглый самоцвет с выгравированным мечом — сразу читается как урон."""
    w = h = S * SS
    x, y = grid(w, h)
    c = w / 2; R = w / 2 - 3 * SS
    d = sd_circle(x, y, c, c, R)
    a = cov(d, SS)
    sw = R * 0.16
    t = np.clip(-d / sw, 0, 1)
    det = tile(METAL_N, w, h)
    sock = shade(metal(w, h, (1.0, 0.82, 0.45)), np.sin(t * math.pi) * 4 * SS, strength=1.0, detail=det, detail_k=0.2, spec_pow=30, spec_k=0.9)
    gx, gy = (x - c) / (R - sw), (y - c) / (R - sw)
    z = np.sqrt(np.clip(1 - gx * gx - gy * gy, 0, 1))
    col = np.array([1.0, 0.72, 0.12])
    diff = np.clip(gx * L[0] + gy * L[1] + z * L[2], 0, 1)
    stone = col * (0.35 + 0.75 * diff[..., None]) + col * 0.35 * np.exp(-((gx) ** 2 + ((gy - 0.5) / 0.5) ** 2) * 2)[..., None]
    # меч по диагонали (из левого низа в правый верх): клинок, гарда, рукоять
    ang = math.radians(45)
    ca, sa = math.cos(ang), math.sin(ang)
    px = (gx * ca + gy * sa); py = (-gx * sa + gy * ca)     # повёрнутые координаты: py вдоль меча (вверх — минус)
    blade = (np.abs(px) < 0.11 - np.clip(-py - 0.45, 0, 1) * 0.25) & (py > -0.82) & (py < 0.30)
    tip = (py <= -0.62) & (np.abs(px) < (py + 0.86) * 0.45)
    guard = (np.abs(py - 0.33) < 0.06) & (np.abs(px) < 0.36)
    grip = (np.abs(px) < 0.06) & (py > 0.33) & (py < 0.66)
    pommel = ((px) ** 2 + (py - 0.72) ** 2) < 0.09 ** 2
    sword = (blade & ~((py <= -0.62) & ~tip)) | guard | grip | pommel
    sword = np.where(blade & (py <= -0.62), tip, sword)
    swf = np.array(Image.fromarray((sword * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2 * SS))).astype(np.float32) / 255
    stone = stone * (1 - swf[..., None] * 0.55)          # тёмная гравировка
    sh = np.array(Image.fromarray((sword * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2 * SS))).astype(np.float32) / 255
    stone = stone + np.roll(np.roll(sh, -2 * SS, 0), -2 * SS, 1)[..., None] * 0.18 * (1 - swf[..., None])   # блик по кромке гравировки
    spec = np.clip(gx * H[0] + gy * H[1] + z * H[2], 0, 1) ** 70
    hl = np.exp(-(((gx + 0.35) / 0.25) ** 2 + ((gy + 0.45) / 0.14) ** 2))
    stone = stone + spec[..., None] * 0.5 + hl[..., None] * 0.5
    stone = stone * (1 - np.exp(-np.clip(-(d + sw), 0, None) / (3 * SS))[..., None] * 0.6)
    m = smooth(0.9, 1.0, t)[..., None]
    save(name, sock * (1 - m) + stone * m, a, (S, S))

if __name__ == '__main__' and (len(sys.argv) > 2 and sys.argv[2] in ('all', 'v2')):
    glow_edge('glow_edge')
    dome('dome')
    gem_attack('gem_attack')
    gem('gem_armor', 128, 'shield', (0.95, 0.2, 0.18), facets=8, glow=0.7)
    print('v2 ok')
