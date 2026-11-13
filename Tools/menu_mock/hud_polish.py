# Имитация правок читаемости HUD на реальных скриншотах игры (в Unity не запускается):
#   1) затемнение по краям экрана под HUD (UIManager.BuildEdgeScrims);
#   2) непрозрачный фон подсказки (TooltipSystem.PanelTint).
# Вуаль накладывается только на карту: элементы HUD закрыты масками (формы и яркость в зонах текста).
# Запуск из корня репозитория: python3 Tools/menu_mock/hud_polish.py
import numpy as np
from PIL import Image, ImageDraw, ImageFont

NEWS = 'Assets/Resources/Menu/News/'
OUT = 'Tools/menu_mock/'
W, H = 1920, 1080
INK = np.array([0.004, 0.012, 0.02])
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

def to_lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def to_srgb(c): return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.clip(c, 0, None) ** (1 / 2.4) - 0.055)
def sstep(t): t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)
def rect(x0, y0, x1, y1): return ((xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)).astype(np.float32)
def disc(cx, cy, r): return np.clip(r + 1 - np.hypot(xx - cx, yy - cy), 0, 1)

def scrim_alpha():
    top = 0.9 * (1 - sstep(yy / 200))
    left = 0.7 * 0.94 * np.clip(1 - xx / 190, 0, 1) ** 1.6
    bottom = 0.85 * (1 - sstep((H - yy) / 130))
    return 1 - (1 - top) * (1 - left) * (1 - bottom)

def hud_mask(srgb, extra=()):
    m = np.zeros((H, W), np.float32)
    shapes = [disc(66, 62, 64), rect(138, 62, 292, 86), rect(386, 6, 886, 72), rect(1036, 6, 1534, 72),
              disc(960, 78, 84), disc(1587, 26, 17), rect(1612, 10, 1908, 42),
              disc(142, 940, 134), disc(229, 809, 19), disc(269, 849, 17), disc(293, 900, 17),
              disc(297, 957, 17), disc(280, 1012, 17), disc(244, 1056, 17), rect(1815, 1032, 1910, 1062),
              rect(78, 224, 100, 242), rect(78, 676, 100, 694), disc(66, 747, 10)]
    shapes += [disc(66, y, 31) for y in (150, 202, 254, 306, 370, 422, 486, 538, 602, 654, 706)]
    shapes += list(extra)
    for s in shapes: m = np.maximum(m, s)
    # Текст HUD без плашек: в его зонах яркие пиксели — буквы и линии прибора, их вуаль не трогает
    lum = sstep((srgb.max(axis=2) - 0.42) / 0.2)
    zones = np.zeros((H, W), np.float32)
    for z in (rect(135, 16, 372, 60), rect(850, 0, 1075, 230), rect(60, 770, 196, 790), rect(290, 1042, 1600, 1076)):
        zones = np.maximum(zones, z)
    return np.maximum(m, lum * zones)

def apply_scrim(img, extra=()):
    srgb = np.asarray(img.convert('RGB'), np.float32) / 255
    lin = to_lin(srgb)
    a = scrim_alpha()[..., None]
    dark = lin * (1 - a) + INK * a
    h = hud_mask(srgb, extra)[..., None]
    out = to_srgb(lin * h + dark * (1 - h))
    return Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))

def fill_from_row(img, box, src):
    """Убрать просвет под подсказкой: область заливается фоном подсказки, взятым из чистого участка той же строки."""
    a = np.asarray(img).copy()
    x0, y0, x1, y1 = box
    for y in range(y0, y1):
        a[y, x0:x1] = np.median(a[y, src[0]:src[1]], axis=0)
    return Image.fromarray(a)

def font(size, bold=True):
    return ImageFont.truetype('Assets/Resources/Fonts/' + ('Play-Bold.ttf' if bold else 'Play-Regular.ttf'), size)

def sheet(title, pairs, note, name, gap=18):
    """Лист «до / после»: пары кадров одна под другой или рядом."""
    pad, head = 28, 64
    w = max(a.width + b.width + gap for a, b in pairs) if pairs[0][0].width < 700 else max(a.width for a, _ in pairs)
    side = pairs[0][0].width < 700
    h = head + sum((a.height if side else a.height + b.height + gap) + 34 + gap for a, b in pairs) + 44
    im = Image.new('RGB', (w + pad * 2, h + pad), (4, 9, 15))
    d = ImageDraw.Draw(im)
    d.text((pad, 20), title, font=font(28), fill=(120, 240, 228))
    y = head + 14
    def tag(x, y, t, c): d.text((x, y), t, font=font(18), fill=c)
    for a, b in pairs:
        if side:
            tag(pad, y, 'ДО', (255, 170, 120)); tag(pad + a.width + gap, y, 'ПОСЛЕ', (130, 235, 160))
            im.paste(a, (pad, y + 30)); im.paste(b, (pad + a.width + gap, y + 30))
            y += a.height + 34 + gap
        else:
            tag(pad, y, 'ДО', (255, 170, 120)); im.paste(a, (pad, y + 30)); y += a.height + 34 + gap
            tag(pad, y, 'ПОСЛЕ', (130, 235, 160)); im.paste(b, (pad, y + 30)); y += b.height + 34 + gap
    d.text((pad, y + 2), note, font=font(16, False), fill=(150, 170, 182))
    im.save(OUT + name)

NOTE = 'Имитация на реальном скриншоте игры: правки внесены в код, в Unity не запускались.'

m0 = Image.open(NEWS + '1.6.7_map.jpg').convert('RGB')
m1 = apply_scrim(m0)
m1.save(OUT + 'hud_polish_full.jpg', quality=90)
c = (0, 0, 1100, 330)
sheet('Затемнение под HUD: шапка империи и левое меню', [(m0.crop(c), m1.crop(c))], NOTE, 'hud_polish_top.png')
c1, c2 = (0, 330, 560, 800), (300, 990, 1620, 1080)
sheet('Левое меню', [(m0.crop(c1), m1.crop(c1))], NOTE, 'hud_polish_left.png')
sheet('Бегущая строка «Эфир»', [(m0.crop(c2), m1.crop(c2))], NOTE, 'hud_polish_ticker.png')

r0 = Image.open(NEWS + '1.6.7_resources.jpg').convert('RGB')
r1 = fill_from_row(r0, (578, 296, 800, 318), (545, 572))      # «призрак» подписи системы
r1 = fill_from_row(r1, (628, 118, 768, 196), (780, 860))      # блик звезды
r1 = apply_scrim(r1, extra=[rect(468, 56, 870, 498)])
c = (440, 40, 900, 512)
sheet('Подсказка: фон непрозрачный, подписи карты не просвечивают', [(r0.crop(c), r1.crop(c))], NOTE, 'hud_polish_tooltip.png')
