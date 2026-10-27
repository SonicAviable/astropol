"""Макет экрана карточного боя из сгенерированных текстур — проверка дизайна до переноса в Unity.
Координаты как в Unity: центр экрана 1920×1080, y вверх."""
import os, sys, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance

D = os.path.dirname(os.path.abspath(__file__))
O = os.path.join(D, 'out')
R = '/home/user/astropol/Assets/Resources'
FONT_T = os.path.join(D, 'dl', 'RussoOne-Regular.ttf')
FONT_B = os.path.join(R, 'Fonts', 'Play-Bold.ttf')
FONT_R = os.path.join(R, 'Fonts', 'Play-Regular.ttf')
W, H = 1920, 1080

def tex(n): return Image.open(os.path.join(O, n + '.png')).convert('RGBA')
def art(p): return Image.open(os.path.join(R, p)).convert('RGBA')
def P(x, y): return (int(W / 2 + x), int(H / 2 - y))
def font(f, s): return ImageFont.truetype(f, s)

def tint(im, c, a=1.0):
    r, g, b, al = im.split()
    from PIL import ImageChops
    rgb = Image.merge('RGB', (r, g, b))
    col = Image.new('RGB', im.size, tuple(int(v * 255) for v in c[:3]))
    rgb = ImageChops.multiply(rgb, col)
    if a != 1.0: al = al.point(lambda v: int(v * a))
    out = rgb.convert('RGBA'); out.putalpha(al); return out

def nine(im, size, b, scale=1.0):
    """9-slice: b — бордюр в пикселях текстуры, scale — во сколько раз ужать бордюр на экране."""
    w, h = size; sw, sh = im.size; bd = int(b * scale)
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    xs = [0, b, sw - b, sw]; ys = [0, b, sh - b, sh]
    dx = [0, bd, w - bd, w]; dy = [0, bd, h - bd, h]
    for i in range(3):
        for j in range(3):
            src = im.crop((xs[i], ys[j], xs[i + 1], ys[j + 1]))
            tw, th = dx[i + 1] - dx[i], dy[j + 1] - dy[j]
            if tw > 0 and th > 0: out.paste(src.resize((tw, th), Image.LANCZOS), (dx[i], dy[j]))
    return out

def place(canvas, im, cx, cy, size=None):
    if size: im = im.resize((int(size[0]), int(size[1])), Image.LANCZOS)
    x, y = P(cx, cy)
    canvas.alpha_composite(im, (x - im.width // 2, y - im.height // 2))

def cover(im, w, h, ty=0.4):
    ta = im.width / im.height; fa = w / h
    if ta > fa:
        nw = int(im.height * fa); x = (im.width - nw) // 2; im = im.crop((x, 0, x + nw, im.height))
    else:
        nh = int(im.width / fa); y = int((im.height - nh) * ty); im = im.crop((0, y, im.width, y + nh))
    return im.resize((int(w), int(h)), Image.LANCZOS)

def round_mask(w, h, r):
    m = Image.new('L', (w * 2, h * 2), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w * 2 - 1, h * 2 - 1), r * 2, fill=255)
    return m.resize((w, h), Image.LANCZOS)

def text(canvas, s, cx, cy, f, size, col=(255, 255, 255), anchor='mm', shadow=True, stroke=0, track=0):
    d = ImageDraw.Draw(canvas)
    fn = font(f, size)
    x, y = P(cx, cy)
    if shadow: d.text((x + 2, y + 2), s, font=fn, fill=(0, 0, 0, 200), anchor=anchor, stroke_width=stroke, stroke_fill=(0, 0, 0))
    d.text((x, y), s, font=fn, fill=col, anchor=anchor, stroke_width=stroke, stroke_fill=(0, 0, 0))

def glow(canvas, cx, cy, w, h, col, a=0.8):
    g = tex('glow').resize((int(w), int(h)), Image.LANCZOS)
    place(canvas, tint(g, col, a), cx, cy)

GOLD = (1.0, 0.82, 0.45); STEEL = (0.85, 0.88, 0.92); BRONZE = (0.9, 0.62, 0.4)
TEAL = (0.3, 0.95, 0.85); RED = (1.0, 0.35, 0.28)

# ---------------------------------------------------------------- карты
def card_full(a, name, kind, txt, cost, pw, ar, frame=STEEL, w=200, h=280, stars=0):
    c = Image.new('RGBA', (w + 40, h + 40), (0, 0, 0, 0))
    ox, oy = 20, 20
    sh = Image.new('RGBA', c.size, (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle((ox + 4, oy + 10, ox + w + 4, oy + h + 10), 18, fill=(0, 0, 0, 170))
    c.alpha_composite(sh.filter(ImageFilter.GaussianBlur(9)))
    body = nine(tex('panel_dark'), (w, h), 40, 0.5)
    c.alpha_composite(body, (ox, oy))
    aw, ah = w - 20, int(h * 0.55)
    im = cover(art(a), aw, ah); m = round_mask(aw, ah, 12); c.paste(im, (ox + 10, oy + 10), m)
    c.alpha_composite(tint(nine(tex('frame_thin'), (aw + 6, ah + 6), 40, 0.35), frame), (ox + 7, oy + 7))
    tp = nine(tex('panel_text'), (w - 24, h - ah - 44), 30, 0.4)
    c.alpha_composite(tp, (ox + 12, oy + ah + 32))
    rb = tint(tex('ribbon'), (1, 1, 1)).resize((w + 16, 36), Image.LANCZOS)
    c.alpha_composite(rb, (ox - 8, oy + ah - 6))
    c.alpha_composite(tint(nine(tex('frame_card'), (w, h), 70, 0.38), frame), (ox, oy))
    d = ImageDraw.Draw(c)
    d.text((ox + w // 2 + 1, oy + ah + 13), name, font=font(FONT_T, 15), fill=(0, 0, 0), anchor='mm')
    d.text((ox + w // 2, oy + ah + 12), name, font=font(FONT_T, 15), fill=(255, 245, 225), anchor='mm')
    d.text((ox + w // 2, oy + ah + 44), kind, font=font(FONT_B, 10), fill=tuple(int(v * 255) for v in frame), anchor='mm')
    import textwrap
    lines = textwrap.wrap(txt, 26)
    for i, ln in enumerate(lines[:4]):
        d.text((ox + w // 2, oy + ah + 62 + i * 15), ln, font=font(FONT_R, 12), fill=(225, 220, 210), anchor='mm')
    def gem(n, x, y, s, val):
        g = tex(n).resize((s, s), Image.LANCZOS); c.alpha_composite(g, (x - s // 2, y - s // 2))
        dd = ImageDraw.Draw(c)
        dd.text((x, y + 1), str(val), font=font(FONT_T, int(s * 0.46)), fill=(255, 255, 255), anchor='mm', stroke_width=3, stroke_fill=(0, 0, 0))
    gem('gem_cost', ox + 14, oy + 14, 54, cost)
    if pw is not None:
        gem('gem_power', ox + 16, oy + h - 14, 56, pw)
        gem('gem_armor', ox + w - 16, oy + h - 14, 56, ar)
    if stars: d.text((ox + w - 16, oy + 18), '★' * stars, font=font(FONT_B, 16), fill=(255, 210, 90), anchor='rm')
    return c

def card_board(a, name, pw, ar, frame=STEEL, w=128, h=178, hurt=False, status=None):
    c = Image.new('RGBA', (w + 40, h + 40), (0, 0, 0, 0))
    ox, oy = 20, 20
    sh = Image.new('RGBA', c.size, (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle((ox + 3, oy + 8, ox + w + 3, oy + h + 8), 14, fill=(0, 0, 0, 190))
    c.alpha_composite(sh.filter(ImageFilter.GaussianBlur(7)))
    c.alpha_composite(nine(tex('panel_dark'), (w, h), 40, 0.4), (ox, oy))
    aw, ah = w - 12, h - 12
    im = cover(art(a), aw, ah); m = round_mask(aw, ah, 10); c.paste(im, (ox + 6, oy + 6), m)
    gr = Image.new('RGBA', (aw, ah), (0, 0, 0, 0)); gd = ImageDraw.Draw(gr)
    for i in range(60): gd.rectangle((0, ah - 60 + i, aw, ah - 60 + i), fill=(5, 8, 14, int(i * 3.6)))
    c.alpha_composite(gr, (ox + 6, oy + 6))
    c.alpha_composite(tint(nine(tex('frame_card'), (w, h), 70, 0.26), frame), (ox, oy))
    d = ImageDraw.Draw(c)
    d.text((ox + w // 2, oy + h - 36), name, font=font(FONT_T, 12), fill=(255, 245, 225), anchor='mm', stroke_width=2, stroke_fill=(0, 0, 0))
    def gem(n, x, y, s, val, col=(255, 255, 255)):
        g = tex(n).resize((s, s), Image.LANCZOS); c.alpha_composite(g, (x - s // 2, y - s // 2))
        ImageDraw.Draw(c).text((x, y + 1), str(val), font=font(FONT_T, int(s * 0.48)), fill=col, anchor='mm', stroke_width=3, stroke_fill=(0, 0, 0))
    if pw is not None:
        gem('gem_power', ox + 10, oy + h - 8, 46, pw)
        gem('gem_armor', ox + w - 10, oy + h - 8, 46, ar, (255, 130, 120) if hurt else (255, 255, 255))
    if status:
        rb = tex('ribbon').resize((w + 10, 26), Image.LANCZOS); c.alpha_composite(tint(rb, (1, 0.6, 0.4)), (ox - 5, oy + 6))
        d.text((ox + w // 2, oy + 19), status, font=font(FONT_B, 11), fill=(255, 230, 200), anchor='mm')
    return c

def card_back(fac, w=128, h=178):
    c = Image.new('RGBA', (w + 30, h + 30), (0, 0, 0, 0))
    ox = oy = 15
    sh = Image.new('RGBA', c.size, (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle((ox + 2, oy + 6, ox + w + 2, oy + h + 6), 12, fill=(0, 0, 0, 180))
    c.alpha_composite(sh.filter(ImageFilter.GaussianBlur(6)))
    c.alpha_composite(nine(tex('panel_plates'), (w, h), 70, 0.3), (ox, oy))
    e = art('Factions/emblem_%s.png' % fac).resize((int(w * 0.7), int(w * 0.7)), Image.LANCZOS)
    c.alpha_composite(e, (ox + (w - e.width) // 2, oy + (h - e.height) // 2))
    c.alpha_composite(tint(nine(tex('frame_card'), (w, h), 70, 0.24), (0.95, 0.4, 0.32)), (ox, oy))
    return c

# ================================================================ ЭКРАН
def build(out):
    cv = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    bg = cover(art('UI/Events/Quake.jpg'), W, H, 0.5)
    bg = ImageEnhance.Brightness(bg).enhance(0.45).filter(ImageFilter.GaussianBlur(3))
    cv.alpha_composite(bg)
    vig = tint(tex('halo'), (0, 0, 0)).resize((int(W * 1.6), int(H * 1.9)))
    v2 = Image.new('RGBA', (W, H), (0, 0, 0, 0)); v2.alpha_composite(vig, ((W - vig.width) // 2, (H - vig.height) // 2)); cv.alpha_composite(v2)

    # ---- стол: окно в поверхность планеты в массивной раме
    TX, TY, TW, TH = 0, 80, 1480, 600
    inner = cover(art('UI/Events/Quake.jpg'), TW - 40, TH - 40, 0.5)
    inner = ImageEnhance.Brightness(inner).enhance(0.62)
    m = round_mask(TW - 40, TH - 40, 22)
    x0, y0 = P(TX - (TW - 40) / 2, TY + (TH - 40) / 2)
    shade = Image.new('RGBA', (TW + 60, TH + 60), (0, 0, 0, 0)); ImageDraw.Draw(shade).rounded_rectangle((30, 40, TW + 30, TH + 40), 40, fill=(0, 0, 0, 220))
    place(cv, shade.filter(ImageFilter.GaussianBlur(20)), TX, TY)
    cv.paste(inner, (x0, y0), m)
    # голо-сетка поверх поверхности + тёмная стеклянная подложка рядов
    glass = Image.new('RGBA', (TW - 40, TH - 40), (6, 12, 20, 110)); cv.paste(Image.alpha_composite(inner, glass), (x0, y0), m)
    frame = tint(nine(tex('frame_card'), (TW, TH), 70, 0.6), BRONZE)
    place(cv, frame, TX, TY)

    LANE = 482
    sectors = [('ЭЛЕКТРОСТАНЦИЯ', 'Пока у защитника — работают турели и щит Штаба', 'Strike.jpg', 2, 0, -1),
               ('ЗАВОД', 'Держащий получает +1 карту каждый ход', 'Drills.jpg', 0, 3, 1),
               ('ШТАБ', 'Щит снят и 9+ силы — прорыв, планета пала', 'PrecursorVault.jpg', 0, 0, -1)]
    for i, (nm, rule, a, pe, pp, ctl) in enumerate(sectors):
        lx = (i - 1) * LANE
        # разделители
        if i > 0:
            dv = Image.new('RGBA', (6, TH - 60), (0, 0, 0, 0)); ImageDraw.Draw(dv).rectangle((2, 0, 3, TH - 60), fill=(200, 170, 120, 70))
            place(cv, dv, lx - LANE / 2, TY)
        # места под карты (выемки)
        for row_y in (TY + 200, TY - 200):
            for k in range(3):
                place(cv, tex('slot'), lx + (k - 1) * 138, row_y, (128, 178))
        # плашка сектора
        PW, PH = 440, 118
        pl = Image.new('RGBA', (PW, PH), (0, 0, 0, 0))
        im = cover(art('UI/Events/' + a), PW - 16, PH - 16, 0.5); im = ImageEnhance.Brightness(im).enhance(0.7)
        pl.paste(im, (8, 8), round_mask(PW - 16, PH - 16, 14))
        gr = Image.new('RGBA', (PW - 16, PH - 16), (0, 0, 0, 0)); gd = ImageDraw.Draw(gr)
        for j in range(PW - 16): gd.line((j, 0, j, PH), fill=(4, 8, 14, int(max(0, 230 - j * 0.9))))
        pl.alpha_composite(gr, (8, 8))
        col = TEAL if ctl == 1 else RED
        if ctl != 0: glow(cv, lx, TY, PW + 90, PH + 90, col, 0.55)
        pl.alpha_composite(tint(nine(tex('frame_card'), (PW, PH), 70, 0.4), GOLD), (0, 0))
        place(cv, pl, lx, TY)
        text(cv, nm, lx - 185, TY + 22, FONT_T, 22, (255, 236, 200), anchor='lm')
        import textwrap
        for j, ln in enumerate(textwrap.wrap(rule, 34)[:2]):
            text(cv, ln, lx - 185, TY - 8 - j * 17, FONT_R, 13, (205, 215, 225), anchor='lm')
        # сила: враг сверху, вы снизу
        for val, oy, orbn, side in ((pe, 66, 'orb_red', 0), (pp, -66, 'orb_teal', 1)):
            win = (val > (pp if side == 0 else pe))
            if win: glow(cv, lx + 170, TY + oy, 140, 140, RED if side == 0 else TEAL, 0.8)
            place(cv, tex(orbn), lx + 170, TY + oy, (70, 70))
            text(cv, str(val), lx + 170, TY + oy + 1, FONT_T, 32, (255, 255, 255), stroke=3)
        if i == 2:
            # купол щита над рядом защитника
            dome = Image.new('RGBA', (450, 230), (0, 0, 0, 0))
            hx = tex('hexgrid').resize((450, 450))
            mm = Image.new('L', (450, 230), 0); ImageDraw.Draw(mm).ellipse((-10, 10, 460, 450), fill=255)
            hx = tint(hx, (0.45, 0.8, 1.0), 0.35)
            dome.paste(hx.crop((0, 0, 450, 230)), (0, 0), mm)
            rim = Image.new('RGBA', (450, 230), (0, 0, 0, 0)); ImageDraw.Draw(rim).ellipse((-10, 10, 460, 450), outline=(140, 210, 255, 200), width=3)
            dome.alpha_composite(rim.filter(ImageFilter.GaussianBlur(1.5)))
            place(cv, dome, lx, TY + 215)

    # ---- карты на столе
    place(cv, card_board('UI/Events/Strike.jpg', 'Бункер', 0, 6, STEEL), -LANE - 69, TY + 200)
    place(cv, card_board('UI/Events/Drills.jpg', 'Турель', 2, 5, STEEL, stars=0) if False else card_board('UI/Events/Drills.jpg', 'Турель', 2, 5, BRONZE), -LANE + 69, TY + 200)
    place(cv, card_board('UI/Events/PrecursorVault.jpg', 'Щит Штаба', 0, 13, (0.5, 0.8, 1.0)), LANE, TY + 200)
    place(cv, card_board('UI/Events/LegionReview.jpg', 'Штурмовики', 3, 2, STEEL, hurt=True), -69, TY - 200)
    place(cv, card_back('nol'), 69, TY - 200)
    glow(cv, 0, TY - 200, 380, 260, TEAL, 0.0)

    # ---- портреты
    for (cy, fac, nm, role, col) in ((455, 'xarn', 'ДОМИНИОН КСАРН', 'ГАРНИЗОН', RED), (-430, 'nol', 'КОНСЕНСУС «НОЛЬ»', 'ДЕСАНТ', TEAL)):
        px = 0 if cy > 0 else -640
        glow(cv, px, cy, 230, 230, col, 0.55)
        S = 124
        p = cover(art('Leaders/leader_%s.jpg' % fac), S, S, 0.15)
        mk = Image.new('L', (S * 2, S * 2), 0); ImageDraw.Draw(mk).ellipse((0, 0, S * 2 - 1, S * 2 - 1), fill=255); mk = mk.resize((S, S), Image.LANCZOS)
        pc = Image.new('RGBA', (S, S), (0, 0, 0, 0)); pc.paste(p, (0, 0), mk)
        place(cv, pc, px, cy)
        place(cv, tint(tex('frame_round'), GOLD), px, cy, (S + 24, S + 24))
        rb = tint(tex('ribbon'), (1, 1, 1)).resize((250, 34))
        ry = cy - 72 if cy > 0 else cy + 74
        place(cv, rb, px, ry)
        text(cv, nm, px, ry + 1, FONT_T, 14, (255, 236, 200))
        text(cv, role, px, ry + (-24 if cy > 0 else 24), FONT_T, 11, tuple(int(v*255) for v in col))

    # рука врага: рубашки веером справа от портрета
    for k in range(6):
        b = card_back('xarn', 64, 90).rotate(-8 + k * 3, expand=True, resample=Image.BICUBIC)
        place(cv, b, 120 + k * 34, 470 + abs(k - 2.5) * 3)

    # ---- рука игрока
    hand = [('LegionReview.jpg', 'БТР «МУЛ»', 'ЮНИТ', 'При раскрытии: высаживает пехоту ценой до 2 из колоды в этот сектор.', 2, 1, 5, STEEL, 0),
            ('Meteor.jpg', 'ОРБИТАЛЬНЫЙ УДАР', 'ПРИКАЗ', '7 урона по сильнейшим врагам сектора, укрытие не спасает.', 5, None, None, (0.78, 0.55, 1.0), 0),
            ('Consensus.jpg', 'УЗЕЛ КОНСЕНСУСА', 'ЮНИТ', 'Все ваши дроны в секторе +1 к силе в каждой перестрелке.', 3, 1, 6, (0.88, 0.92, 0.98), 0),
            ('DoubleAgent.jpg', 'СНАЙПЕРЫ', 'ЮНИТ', 'Бьют самого хрупкого врага. По пехоте — двойной урон.', 2, 2, 2, BRONZE, 2),
            ('Battlefield.jpg', 'ТЯЖЁЛАЯ ПЕХОТА', 'ЮНИТ', 'Укрытие 2: первые 2 урона за ход не проходят.', 3, 2, 6, STEEL, 0),
            ('Mind.jpg', 'ГЕНЕРАЛ', 'ГЕРОЙ', 'При раскрытии «Прорыв»: союзники в секторе +1 к силе до конца боя.', 4, 3, 6, GOLD, 0)]
    n = len(hand)
    for k, (a, nm, kd, tx, co, pw, ar, fr, st) in enumerate(hand):
        t = k / (n - 1) - 0.5
        x = t * 150 * (n - 1); y = -470 - abs(t) * abs(t) * 60
        if k == 3: y += 110   # наведённая карта поднята
        c = card_full('UI/Events/' + a, nm, kd, tx, co, pw, ar, fr, stars=st)
        if k == 3:
            c = c.resize((int(c.width * 1.12), int(c.height * 1.12)), Image.LANCZOS)
            glow(cv, x, y, 330, 420, (0.45, 1.0, 0.6), 0.55)
        elif co <= 3:
            glow(cv, x, y, 280, 360, (0.45, 1.0, 0.6), 0.3)
        c = c.rotate(-t * 14 if k != 3 else 0, expand=True, resample=Image.BICUBIC)
        place(cv, c, x, y)

    # ---- правая колонка: ход, кнопка, погода, служебные кнопки
    RX = 850
    place(cv, tex('orb_gray'), RX, 300, (110, 110))
    text(cv, '2', RX, 304, FONT_T, 44, (255, 240, 210), stroke=3)
    text(cv, 'ХОД ИЗ 6', RX, 230, FONT_T, 15, (230, 200, 150))
    glow(cv, RX, 120, 300, 160, (0.3, 1.0, 0.4), 0.45)
    place(cv, tex('btn_end_green'), RX, 120, (220, 82))
    text(cv, 'ЗАКОНЧИТЬ', RX, 131, FONT_T, 19, (255, 255, 255), stroke=2)
    text(cv, 'ХОД', RX, 108, FONT_T, 19, (255, 255, 255), stroke=2)
    text(cv, 'СЕКТОРЫ  1 : 2', RX, 56, FONT_T, 16, (230, 225, 215)); text(cv, 'нужно 2 из 3', RX, 36, FONT_R, 12, (150,165,176))
    wp = nine(tex('panel_leather'), (226, 74), 60, 0.3); place(cv, wp, RX, -34)
    text(cv, 'ПОГОДА', RX, -8, FONT_T, 12, (160, 200, 255))
    text(cv, 'откроется на 3-м ходу', RX, -42, FONT_R, 13, (205, 215, 225))
    for k, lab in enumerate(['×1', '»»', '⟲']):
        bx = RX - 68 + k * 68
        bxx=1
        d0=Image.new('RGBA',(56,56),(0,0,0,0)); ImageDraw.Draw(d0).ellipse((0,0,55,55),fill=(15,20,28,255)); place(cv,d0,bx,-128)
        place(cv, tint(tex('frame_round'), STEEL), bx, -128, (66, 66))
        text(cv, lab, bx, -128, FONT_T, 16, (230, 230, 230))

    # ---- левая колонка: колоды и хроника
    LX = -850
    for (cy, fac, cnt) in ((300, 'xarn', 9), (-160, 'nol', 11)):
        for k in range(3): place(cv, card_back(fac, 92, 128), LX - 4 + k * 4, cy + k * 3)
        place(cv, tex('orb_gray'), LX + 52, cy - 52, (48, 48))
        text(cv, str(cnt), LX + 52, cy - 51, FONT_T, 20, (255, 255, 255), stroke=2)
    hp = nine(tex('panel_leather'), (220, 230), 60, 0.3); place(cv, hp, LX, 70)
    text(cv, 'ХРОНИКА БОЯ', LX, 170, FONT_T, 13, (230, 200, 150))
    for k, (ln, c) in enumerate([('— Ход 1 —', (240, 200, 110)), ('Враг: Бункер', (220, 220, 220)), ('Враг: Турель', (220, 220, 220)),
                                ('Вы: Штурмовики', (150, 240, 200)), ('Сектор «Завод» ваш', (120, 240, 160)), ('— Ход 2 —', (240, 200, 110))]):
        text(cv, ln, LX - 92, 140 - k * 22, FONT_R, 14, c, anchor='lm', shadow=False)

    # ---- очки приказов: кристаллы
    text(cv, '3/5', 505, -500, FONT_T, 30, (255, 255, 255), stroke=3)
    for k in range(8):
        n2 = 'crystal_full' if k < 3 else 'crystal_empty'
        if k >= 5: continue
        place(cv, tex(n2), 585 + k * 38, -500, (40, 40))
    text(cv, 'ОЧКИ ПРИКАЗОВ', 640, -466, FONT_T, 12, (160, 210, 255))

    text(cv, 'Перетащите карты в секторы. Enter — закончить ход', 0, -255, FONT_T, 15, (255, 237, 204))
    cv.convert('RGB').save(out, quality=92)

if __name__ == '__main__':
    build(sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, 'mock.jpg'))
    print('ok')
