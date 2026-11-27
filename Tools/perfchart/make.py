"""
Инфографика для игроков: как Astropolity использует процессор (потоки) и видеокарту — простыми словами.

    python3 Tools/perfchart/make.py         → Docs/perf_threads_ru.png (1920×1080)

Схема по коду игры: вся логика (время, экономика, ИИ, флоты — ~20 систем на каждый игровой день) и интерфейс —
в главном потоке; фоновые потоки самой игры — синтез звуков при запуске (до 4 потоков, SFXManager.Prewarm)
и создание галактики (Threaded.For на всех ядрах, GalaxyGenerator); остальное — потоки движка Unity
(рендер, задачи частиц и сборки интерфейса, звук, видео) и видеокарта. Длительности на шкале — условные.
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
A = lambda p: os.path.join(ROOT, 'Assets', p)
OUT = os.path.join(ROOT, 'Docs', 'perf_threads_ru.png')
W, H = 1920, 1080

CYAN = (102, 219, 209)
GOLD = (242, 199, 102)
RED = (240, 110, 95)
VIOLET = (170, 140, 255)
TEXT = (240, 250, 255)
MUTED = (170, 196, 210)
DIM = (70, 88, 100)


def font(name, size):
    return ImageFont.truetype(A('Resources/Fonts/' + name), size)


F_TITLE = font('RussoOne-Regular.ttf', 44)
F_HEAD = font('RussoOne-Regular.ttf', 24)
F_SUB = font('Play-Regular.ttf', 22)
F_LANE = font('Play-Bold.ttf', 18)
F_ROLE = font('Play-Regular.ttf', 14)
F_SEG = font('Play-Bold.ttf', 14)
F_SMALL = font('Play-Regular.ttf', 14)
F_TIP_T = font('Play-Bold.ttf', 19)
F_TIP = font('Play-Regular.ttf', 16)
F_CHIP = font('Play-Bold.ttf', 15)


def tracked(d, xy, text, f, fill, spacing=2.0):
    x, y = xy
    for ch in text:
        d.text((x, y), ch, font=f, fill=fill)
        x += d.textlength(ch, font=f) + spacing


def background():
    bg = Image.open(A('Resources/UI/LoadingArt_2.jpg')).convert('RGB')
    s = max(W / bg.width, H / bg.height)
    bg = bg.resize((int(bg.width * s) + 1, int(bg.height * s) + 1), Image.LANCZOS)
    bg = bg.crop(((bg.width - W) // 2, (bg.height - H) // 2, (bg.width - W) // 2 + W, (bg.height - H) // 2 + H))
    bg = bg.filter(ImageFilter.GaussianBlur(4))
    dark = Image.new('RGB', (W, H), (2, 6, 12))
    return Image.blend(bg, dark, 0.78).convert('RGBA')


def glass(img, box, rim, radius=20):
    glow = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(box, radius, outline=rim + (120,), width=6)
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(12)))
    panel = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(panel).rounded_rectangle(box, radius, fill=(3, 9, 14, 222))
    img.alpha_composite(panel)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle(box, radius, outline=rim + (190,), width=2)


def wrap(d, text, f, width):
    words, lines, cur = text.split(), [], ''
    for w in words:
        t = (cur + ' ' + w).strip()
        if d.textlength(t, font=f) <= width: cur = t
        else:
            if cur: lines.append(cur)
            cur = w
    if cur: lines.append(cur)
    return lines


def para(d, xy, text, f, fill, width, lh):
    x, y = xy
    for line in wrap(d, text, f, width):
        d.text((x, y), line, font=f, fill=fill)
        y += lh
    return y


# ------------------------------------------------------------------ один кадр

def frame_panel(img):
    x0, y0, x1, y1 = 80, 196, 1190, 716
    glass(img, (x0, y0, x1, y1), CYAN)
    d = ImageDraw.Draw(img)
    tracked(d, (x0 + 30, y0 + 22), 'ОДИН КАДР ИГРЫ', F_HEAD, CYAN, 3)
    chip = '60 кадров в секунду = 16,7 мс на кадр'
    tw = d.textlength(chip, font=F_CHIP) + 30
    d.rounded_rectangle((x1 - 30 - tw, y0 + 20, x1 - 30, y0 + 52), 16, fill=(10, 40, 46, 255), outline=CYAN + (150,))
    d.text((x1 - 30 - tw / 2, y0 + 36), chip, font=F_CHIP, fill=TEXT, anchor='mm')

    tx0, tx1 = 360, x1 - 34
    ms = lambda t: tx0 + (tx1 - tx0) * t / 16.7
    # шкала
    ty = y0 + 84
    for t in (0, 4, 8, 12, 16.7):
        x = ms(t)
        d.line((x, ty + 12, x, y1 - 96), fill=(36, 58, 66), width=1)
        d.text((x, ty), ('16,7' if t == 16.7 else str(int(t))) + ' мс', font=F_SMALL, fill=MUTED, anchor='mm')

    lanes = [
        ('Главный поток', '«мозг»: логика и интерфейс', GOLD),
        ('Поток рендера', '«курьер»: команды видеокарте', CYAN),
        ('Помощники', 'рабочие потоки движка', VIOLET),
        ('Видеокарта', '«художник»: рисует кадр', RED),
    ]
    rows = [ty + 62 + i * 86 for i in range(4)]
    for (name, role, col), y in zip(lanes, rows):
        d.rectangle((x0 + 30, y - 22, x0 + 34, y + 22), fill=col + (255,))
        d.text((x0 + 46, y - 20), name, font=F_LANE, fill=TEXT)
        d.text((x0 + 46, y + 4), role, font=F_ROLE, fill=MUTED)
        d.rounded_rectangle((tx0, y - 27, tx1, y + 27), 10, fill=(13, 24, 32))

    def seg(y, a, b, label, col, alpha=230, h=40, dy=0, txt=TEXT):
        xa, xb = ms(a), ms(b)
        box = (xa + 1, y - h / 2 + dy, xb - 1, y + h / 2 + dy)
        k = 0.42 if alpha > 200 else 0.22
        d.rounded_rectangle(box, 8, fill=tuple(int(c * k) for c in col), outline=col if alpha > 200 else tuple(int(c * 0.6) for c in col), width=2)
        if label:
            f = F_SEG if h >= 30 else F_ROLE
            lines = wrap(d, label, f, xb - xa - 14)
            if h < 30: lines = lines[:1]
            lh = 16
            cy = y + dy - (len(lines) - 1) * lh / 2
            for i, ln in enumerate(lines[:2]):
                d.text(((xa + xb) / 2, cy + i * lh), ln, font=f, fill=txt, anchor='mm')

    r0, r1, r2, r3 = rows
    seg(r0, 0, 1.0, 'Ввод', GOLD)
    seg(r0, 1.0, 7.2, 'Игра: время, экономика, ИИ, флоты, бои', GOLD)
    seg(r0, 7.2, 10.6, 'Интерфейс и окна', GOLD)
    seg(r0, 10.6, 12.0, 'Камера', GOLD)
    # запас кадра
    xa, xb = ms(12.2), ms(16.6)
    for k in range(int(xa), int(xb) - 14, 12):
        d.line((k, r0 + 18, k + 14, r0 - 18), fill=(40, 62, 70), width=2)
    lw = d.textlength('запас кадра', font=F_SEG) + 20
    d.rounded_rectangle(((xa + xb - lw) / 2, r0 - 12, (xa + xb + lw) / 2, r0 + 12), 10, fill=(13, 24, 32))
    d.text(((xa + xb) / 2, r0), 'запас кадра', font=F_SEG, fill=MUTED, anchor='mm')

    seg(r1, 0, 2.6, 'прошлый кадр', CYAN, alpha=120, txt=MUTED)
    seg(r1, 12.0, 16.0, 'Готовит команды видеокарте', CYAN)

    seg(r2, 7.4, 10.6, 'Интерфейс', VIOLET, h=22, dy=-12)
    seg(r2, 8.4, 11.8, 'Частицы', VIOLET, h=22, dy=12)
    seg(r2, 10.8, 13.4, 'Отсечение', VIOLET, h=22, dy=-12)
    seg(r2, 0.2, 6.8, 'звук · музыка · видео', (110, 140, 160), alpha=100, h=22, dy=12)

    seg(r3, 0, 9.5, 'Рисует прошлый кадр: галактика, планеты, стекло, свет', RED, alpha=150)
    seg(r3, 13.4, 16.7, 'Этот кадр', RED)

    para(d, (x0 + 30, y1 - 62), 'Работа идёт конвейером: пока видеокарта рисует один кадр, процессор уже считает следующий. '
         'Звук, музыка и видео играют в своих потоках и на плавность не влияют.',
         F_SMALL, MUTED, x1 - x0 - 60, 20)


# ------------------------------------------------------------------ когда что нагружается

def load_panel(img):
    x0, y0, x1, y1 = 1220, 196, 1840, 716
    glass(img, (x0, y0, x1, y1), GOLD)
    d = ImageDraw.Draw(img)
    tracked(d, (x0 + 30, y0 + 22), 'КОГДА ЧТО НАГРУЖАЕТСЯ', F_HEAD, GOLD, 3)
    d.text((x0 + 30, y0 + 58), 'Пример: процессор с 8 ядрами', font=F_SMALL, fill=MUTED)
    rows = [
        ('Запуск игры', 'Синтез звуков интерфейса и боя', [2, 2, 2, 2, 0, 0, 0, 0], 'до 4 ядер · пара секунд'),
        ('Новая партия', 'Создание галактики и коридоров', [2] * 8, 'все ядра · секунды'),
        ('Обычная игра', 'Главный поток + рендер + помощники', [3, 2, 1, 1, 1, 0, 0, 0], '1 главное + помощники'),
        ('Поздняя игра на ×4', 'Каждый день: экономика, ИИ, флоты', [4, 2, 1, 1, 0, 0, 0, 0], 'упор в одно ядро'),
        ('Карточный бой', 'Правила мгновенны, рисует видеокарта', [2, 2, 1, 1, 0, 0, 0, 0], 'нагрузка на видеокарту'),
    ]
    cols = {0: (40, 52, 60), 1: (60, 110, 120), 2: CYAN, 3: GOLD, 4: RED}
    y = y0 + 96
    for title, desc, cores, note in rows:
        d.text((x0 + 30, y), title, font=F_LANE, fill=TEXT)
        d.text((x0 + 30, y + 24), desc, font=F_ROLE, fill=MUTED)
        cx = x1 - 30 - 8 * 26 + 4
        for i, lv in enumerate(cores):
            d.rounded_rectangle((cx + i * 26, y + 2, cx + i * 26 + 20, y + 22), 4, fill=cols[lv] + (255,))
        d.text((x1 - 30, y + 34), note, font=F_ROLE, fill=GOLD if cores[0] >= 3 else MUTED, anchor='ra')
        y += 76
        d.line((x0 + 30, y - 12, x1 - 30, y - 12), fill=(36, 58, 66), width=1)
    # легенда
    ly = y1 - 36
    lx = x0 + 30
    for lv, name in ((4, 'упор'), (3, 'высокая'), (2, 'средняя'), (1, 'низкая'), (0, 'простой')):
        d.rounded_rectangle((lx, ly, lx + 16, ly + 16), 3, fill=cols[lv] + (255,))
        d.text((lx + 22, ly - 1), name, font=F_ROLE, fill=MUTED)
        lx += 34 + d.textlength(name, font=F_ROLE)


# ------------------------------------------------------------------ советы

def tips_panel(img):
    x0, y0, x1, y1 = 80, 740, 1840, 978
    glass(img, (x0, y0, x1, y1), (115, 190, 255))
    d = ImageDraw.Draw(img)
    tracked(d, (x0 + 30, y0 + 22), 'ЧТО ЭТО ЗНАЧИТ ДЛЯ ВАС', F_HEAD, (150, 205, 255), 3)
    tips = [
        ('Важна частота ядра', GOLD, 'Вся логика игры живёт в одном главном потоке: быстрое ядро ускоряет позднюю игру сильнее, чем лишние ядра.'),
        ('4–6 ядер — достаточно', CYAN, 'Больше ядер ускоряют только запуск игры и создание новой галактики.'),
        ('Падает FPS на карте?', RED, 'Упор в видеокарту: пресет «Среднее», масштаб рендера 75% и AMD FSR в настройках графики.'),
        ('Тормозит время на ×4?', VIOLET, 'Упор в процессор: скорость ×2, галактика поменьше или меньше соперников.'),
    ]
    cw = (x1 - x0 - 60 - 3 * 24) / 4
    for i, (t, col, text) in enumerate(tips):
        cx = x0 + 30 + i * (cw + 24)
        box = (cx, y0 + 70, cx + cw, y1 - 26)
        d.rounded_rectangle(box, 14, fill=(8, 18, 26, 235), outline=col + (150,), width=2)
        d.rectangle((cx + 18, y0 + 92, cx + 22, y0 + 114), fill=col)
        d.text((cx + 32, y0 + 90), t, font=F_TIP_T, fill=TEXT)
        para(d, (cx + 20, y0 + 128), text, F_TIP, MUTED, cw - 40, 23)


def main():
    img = background()
    d = ImageDraw.Draw(img)
    logo = Image.open(A('Branding/logo.png')).convert('RGBA').resize((118, 118), Image.LANCZOS)
    lg = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lg).ellipse((76, 30, 212, 166), fill=GOLD + (80,))
    img.alpha_composite(lg.filter(ImageFilter.GaussianBlur(22)))
    img.alpha_composite(logo, (85, 38))
    tracked(d, (228, 52), 'КАК ИГРА ИСПОЛЬЗУЕТ ПРОЦЕССОР И ВИДЕОКАРТУ', F_TITLE, GOLD, 2)
    d.rectangle((230, 124, 300, 126), fill=GOLD + (230,))
    d.text((314, 112), 'Один кадр, потоки и когда что нагружается — простыми словами', font=F_SUB, fill=MUTED)

    frame_panel(img)
    load_panel(img)
    tips_panel(img)
    d = ImageDraw.Draw(img)
    d.text((W / 2, 1024), 'Схема упрощена: длительности и доли нагрузки условные и зависят от компьютера', font=F_SMALL, fill=(140, 160, 172), anchor='mm')
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.convert('RGB').save(OUT, optimize=True)
    print(OUT)


if __name__ == '__main__':
    main()
