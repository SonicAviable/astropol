# Курсоры игры -> Assets/Resources/UI/Cursors/: стрелка (обычная и подсвеченная при наведении) и прицел приказа флоту.
# Рисуются векторно с 8-кратным сглаживанием в трёх размерах (32/48/64 px) — GameCursor берёт размер по высоте экрана.
# Запуск: python3 make_cursors.py
import os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Resources', 'UI', 'Cursors')
SS = 8
CYAN = (110, 225, 255)
DARK = (8, 18, 28)

# Стрелка в единицах 0..1 (остриё — (0,0)): скошенный «клинок» с хвостом, как рамки интерфейса со срезанными углами
ARROW = [(0.06, 0.04), (0.06, 0.80), (0.25, 0.63), (0.37, 0.92), (0.50, 0.87), (0.38, 0.58), (0.64, 0.58)]
# Внутренняя прорезь-блик вдоль левого края
SLASH = [(0.13, 0.20), (0.13, 0.60), (0.20, 0.53)]

def P(pts, n, off=(0, 0)):
    return [((x * n) + off[0], (y * n) + off[1]) for x, y in pts]

def arrow(size, hover):
    n = size * SS
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    # мягкое свечение под стрелкой
    glow = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(glow).polygon(P(ARROW, n * 0.94, (n * 0.03, n * 0.03)), fill=CYAN + (150 if hover else 70,))
    glow = glow.filter(ImageFilter.GaussianBlur(n * (0.05 if hover else 0.035)))
    img.alpha_composite(glow)
    d = ImageDraw.Draw(img)
    w = max(SS, round(n * 0.045))
    shape = P(ARROW, n * 0.94, (n * 0.03, n * 0.03))
    d.polygon(shape, fill=(CYAN + (255,)) if hover else (DARK + (235,)))
    d.line(shape + [shape[0]], fill=(255, 255, 255, 255) if hover else (CYAN + (255,)), width=w, joint='curve')
    d.polygon(P(SLASH, n * 0.94, (n * 0.03, n * 0.03)), fill=(DARK + (200,)) if hover else (CYAN + (150,)))
    return img.resize((size, size), Image.LANCZOS)

def target(size):
    n = size * SS
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    c = n / 2
    def ring(draw, col, w):
        r = n * 0.30
        for a0 in (20, 110, 200, 290):     # кольцо из четырёх дуг с разрывами
            draw.arc([c - r, c - r, c + r, c + r], a0, a0 + 50, fill=col, width=w)
        for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):   # засечки внутрь
            draw.line([(c + dx * n * 0.44, c + dy * n * 0.44), (c + dx * n * 0.22, c + dy * n * 0.22)], fill=col, width=w)
        draw.ellipse([c - n * 0.045, c - n * 0.045, c + n * 0.045, c + n * 0.045], fill=col)
    glow = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    ring(ImageDraw.Draw(glow), CYAN + (160,), round(n * 0.09))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(n * 0.04)))
    d = ImageDraw.Draw(img)
    ring(d, DARK + (230,), round(n * 0.085))       # тёмная подложка — читается на светлом
    ring(d, CYAN + (255,), round(n * 0.045))
    return img.resize((size, size), Image.LANCZOS)

os.makedirs(OUT, exist_ok=True)
for size in (32, 48, 64):
    arrow(size, False).save(os.path.join(OUT, f'cursor_arrow_{size}.png'))
    arrow(size, True).save(os.path.join(OUT, f'cursor_hover_{size}.png'))
    target(size).save(os.path.join(OUT, f'cursor_move_{size}.png'))
print('ok ->', os.path.normpath(OUT))
