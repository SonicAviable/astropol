"""
Картинка системных требований для страницы игры (VK Play и др.) в стиле интерфейса Astropolity:
фон — арт загрузки, тёмное стекло с бирюзовой / золотой кромкой, шрифты Play и Russo One.

    python3 Tools/sysreq/make.py            → VKPlay/system_requirements_ru.png (1920×1080)

Текст требований — в REQ ниже; при изменении достаточно перезапустить скрипт.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
A = lambda p: os.path.join(ROOT, 'Assets', p)
OUT = os.path.join(ROOT, 'VKPlay', 'system_requirements_ru.png')
W, H = 1920, 1080

CYAN = (102, 219, 209)
GOLD = (242, 199, 102)
TEXT = (240, 250, 255)
MUTED = (170, 196, 210)

REQ = [
    ('МИНИМАЛЬНЫЕ', CYAN, '1080p  ·  «Низкое»  ·  30+ кадров/с', [
        ('ОПЕРАЦИОННАЯ СИСТЕМА', 'Windows 10 64-бит (21H2 и новее)'),
        ('ПРОЦЕССОР', 'Intel Core i5-4460 / AMD Ryzen 3 1200', '4 ядра, 3,0 ГГц'),
        ('ОПЕРАТИВНАЯ ПАМЯТЬ', '8 ГБ'),
        ('ВИДЕОКАРТА', 'GeForce GTX 960 / Radeon R9 380', 'или Intel Iris Xe · 2 ГБ видеопамяти'),
        ('DIRECTX', 'Версия 11'),
        ('МЕСТО НА ДИСКЕ', '6 ГБ'),
    ]),
    ('РЕКОМЕНДУЕМЫЕ', GOLD, '1080p  ·  «Высокое»  ·  60 кадров/с', [
        ('ОПЕРАЦИОННАЯ СИСТЕМА', 'Windows 10 / 11 64-бит'),
        ('ПРОЦЕССОР', 'Intel Core i5-9600K / AMD Ryzen 5 3600', '6 ядер — для больших галактик и поздней игры'),
        ('ОПЕРАТИВНАЯ ПАМЯТЬ', '16 ГБ'),
        ('ВИДЕОКАРТА', 'GeForce GTX 1660 Super / Radeon RX 5600 XT', '6 ГБ видеопамяти'),
        ('DIRECTX', 'Версия 11 (поддерживается 12)'),
        ('МЕСТО НА ДИСКЕ', '6 ГБ на SSD'),
    ]),
]

FOOT = 'Требования предварительные и могут уточниться к выходу игры'


def font(name, size):
    return ImageFont.truetype(A('Resources/Fonts/' + name), size)


F_TITLE = font('RussoOne-Regular.ttf', 60)
F_HEAD = font('RussoOne-Regular.ttf', 34)
F_LABEL = font('Play-Bold.ttf', 17)
F_VALUE = font('Play-Bold.ttf', 30)
F_NOTE = font('Play-Regular.ttf', 19)
F_SUB = font('Play-Regular.ttf', 24)
F_TARGET = font('Play-Bold.ttf', 19)


def tracked(d, xy, text, f, fill, spacing=2.0, anchor_center=False):
    """Текст с разрядкой (как Track в интерфейсе игры)."""
    widths = [d.textlength(ch, font=f) for ch in text]
    total = sum(widths) + spacing * (len(text) - 1)
    x, y = xy
    if anchor_center: x -= total / 2
    for ch, w in zip(text, widths):
        d.text((x, y), ch, font=f, fill=fill)
        x += w + spacing
    return total


def background():
    bg = Image.open(A('Resources/UI/LoadingArt.jpg')).convert('RGB')
    s = max(W / bg.width, H / bg.height)
    bg = bg.resize((int(bg.width * s) + 1, int(bg.height * s) + 1), Image.LANCZOS)
    bg = bg.crop(((bg.width - W) // 2, (bg.height - H) // 2, (bg.width - W) // 2 + W, (bg.height - H) // 2 + H))
    bg = bg.filter(ImageFilter.GaussianBlur(3))
    # Затемнение: сверху и снизу плотнее, по центру видна галактика
    shade = Image.new('L', (W, H))
    sd = ImageDraw.Draw(shade)
    for y in range(H):
        t = abs(y - H * 0.42) / (H * 0.58)
        sd.line([(0, y), (W, y)], fill=int(105 + 120 * min(1, t ** 1.2)))
    dark = Image.new('RGB', (W, H), (2, 6, 12))
    img = Image.composite(dark, bg, shade)
    # Виньетка
    vig = Image.new('L', (W, H), 0)
    ImageDraw.Draw(vig).ellipse([-W * 0.25, -H * 0.35, W * 1.25, H * 1.35], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(160))
    return Image.composite(img, Image.new('RGB', (W, H), (0, 2, 6)), vig).convert('RGBA')


def glass(img, box, rim, radius=22):
    """Тёмное стекло с цветной кромкой и мягким неоновым ореолом — как LiquidGlass в игре."""
    x0, y0, x1, y1 = box
    glow = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(box, radius, outline=rim + (150,), width=6)
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(14)))
    panel = Image.new('RGBA', img.size, (0, 0, 0, 0))
    pd = ImageDraw.Draw(panel)
    pd.rounded_rectangle(box, radius, fill=(3, 9, 14, 206))
    # Блик сверху: стекло светлее у верхней кромки (по форме панели, со скруглёнными углами)
    hl = Image.new('RGBA', img.size, (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    for i in range(90):
        hd.line([(x0, y0 + i), (x1, y0 + i)], fill=(150, 230, 240, int(24 * (1 - i / 90) ** 2)))
    mask = Image.new('L', img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((x0 + 2, y0 + 2, x1 - 2, y1 - 2), radius - 2, fill=255)
    panel.alpha_composite(Image.composite(hl, Image.new('RGBA', img.size, (0, 0, 0, 0)), mask))
    img.alpha_composite(panel)
    ImageDraw.Draw(img).rounded_rectangle(box, radius, outline=rim + (190,), width=2)
    ImageDraw.Draw(img).rounded_rectangle((x0 + 3, y0 + 3, x1 - 3, y1 - 3), radius - 3, outline=(255, 255, 255, 22), width=1)


def column(img, x0, x1, y0, y1, title, rim, target, rows):
    glass(img, (x0, y0, x1, y1), rim)
    d = ImageDraw.Draw(img)
    cx = (x0 + x1) / 2
    tracked(d, (cx, y0 + 30), title, F_HEAD, rim, 4, True)
    # Плашка «на чём рассчитано»
    tw = d.textlength(target, font=F_TARGET) + 44
    chip = (cx - tw / 2, y0 + 86, cx + tw / 2, y0 + 120)
    d.rounded_rectangle(chip, 17, fill=(rim[0] // 6, rim[1] // 6, rim[2] // 6, 230), outline=rim + (150,), width=1)
    d.text((cx, y0 + 103), target, font=F_TARGET, fill=TEXT, anchor='mm')
    d.line([(x0 + 40, y0 + 146), (x1 - 40, y0 + 146)], fill=rim + (60,), width=1)
    y = y0 + 168
    for row in rows:
        label, value = row[0], row[1]
        d.rectangle((x0 + 44, y + 5, x0 + 47, y + 17), fill=rim + (230,))
        tracked(d, (x0 + 58, y), label, F_LABEL, GOLD, 1.6)
        d.text((x0 + 58, y + 25), value, font=F_VALUE, fill=TEXT)
        if len(row) > 2:
            d.text((x0 + 58, y + 63), row[2], font=F_NOTE, fill=MUTED)
            y += 112
        else:
            y += 86


def main():
    img = background()
    d = ImageDraw.Draw(img)

    logo = Image.open(A('Branding/logo.png')).convert('RGBA').resize((150, 150), Image.LANCZOS)
    lg = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lg).ellipse((112, 34, 278, 200), fill=GOLD + (90,))
    img.alpha_composite(lg.filter(ImageFilter.GaussianBlur(24)))
    img.alpha_composite(logo, (120, 42))

    tracked(d, (304, 62), 'СИСТЕМНЫЕ ТРЕБОВАНИЯ', F_TITLE, GOLD, 3)
    d.rectangle((306, 150, 386, 152), fill=GOLD + (230,))
    d.text((402, 140), 'ASTROPOLITY  ·  космическая 4X-стратегия  ·  Windows', font=F_SUB, fill=MUTED)

    top, bottom = 228, 986
    column(img, 120, 930, top, bottom, *REQ[0])
    column(img, 990, 1800, top, bottom, *REQ[1])

    d.text((W / 2, 1034), FOOT, font=F_NOTE, fill=(150, 172, 186), anchor='mm')
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.convert('RGB').save(OUT, optimize=True)
    print(OUT)


if __name__ == '__main__':
    main()
