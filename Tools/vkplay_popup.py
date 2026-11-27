import sys, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

R = '/home/user/astropol/Assets'
OUT = sys.argv[1]
W, H = 700, 750

def load(p): return Image.open(os.path.join(R, p))

# --- фон: туманность
bg = load('Resources/Menu/menu_bg.jpg').convert('RGB')
s = H / bg.height * 1.05
bg = bg.resize((int(bg.width * s), int(bg.height * s)), Image.LANCZOS)
x0 = int(bg.width * 0.42)
bg = bg.crop((x0, 0, x0 + W, H))
bg = ImageEnhance.Brightness(bg).enhance(0.9)
canvas = bg.convert('RGBA')

# --- планета справа сверху
pl = load('Resources/Menu/menu_planet.png').convert('RGBA')
bb = pl.getbbox(); pl = pl.crop(bb)
pw = 520; pl = pl.resize((pw, int(pl.height * pw / pl.width)), Image.LANCZOS)
canvas.alpha_composite(pl, (W - pw + 170, 10))

def leader(name, h):
    img = load(f'Resources/Leaders/leader_{name}.jpg').convert('RGB')
    fx = load(f'Resources/Leaders/leader_{name}_fx.png').convert('RGBA')
    img = img.resize(fx.size, Image.LANCZOS)
    a = fx.getchannel('A').filter(ImageFilter.GaussianBlur(1.2))
    out = img.convert('RGBA'); out.putalpha(a)
    out = out.crop(out.getbbox())
    return out.resize((int(out.width * h / out.height), h), Image.LANCZOS)

def rim(im, color, blur=10):
    a = im.getchannel('A').filter(ImageFilter.GaussianBlur(blur))
    g = Image.new('RGBA', im.size, color + (0,)); g.putalpha(a.point(lambda v: int(v * 0.55)))
    return g

def place(im, cx, top, glow):
    pad = 30
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    gl = Image.new('RGBA', (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0))
    gl.alpha_composite(rim(im, glow), (pad, pad))
    gl = gl.filter(ImageFilter.GaussianBlur(14))
    layer.alpha_composite(gl, (cx - im.width // 2 - pad, top - pad)) if top - pad >= 0 else None
    canvas.alpha_composite(layer)
    x = cx - im.width // 2
    tmp = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    tmp.paste(im, (x, top), im)
    canvas.alpha_composite(tmp)

L = leader('aquila', 610)
Rt = leader('xarn', 600)
C = leader('iridia', 690)
# боковые — чуть темнее, чтобы центр читался
L = Image.merge('RGBA', (*ImageEnhance.Brightness(L.convert('RGB')).enhance(0.8).split(), L.getchannel('A')))
Rt = Image.merge('RGBA', (*ImageEnhance.Brightness(Rt.convert('RGB')).enhance(0.75).split(), Rt.getchannel('A')))
place(L, 150, 95, (230, 180, 90))
place(Rt, 560, 100, (220, 60, 60))
place(C, 360, 55, (120, 90, 255))

# --- нижний градиент под текст и служебную зону
grad = Image.new('L', (1, H))
for y in range(H):
    t = max(0.0, (y - 330) / (H - 330))
    grad.putpixel((0, y), int(255 * min(1.0, t ** 0.85 * 1.15)))
dark = Image.new('RGBA', (W, H), (8, 10, 20, 255)); dark.putalpha(grad.resize((W, H)))
canvas.alpha_composite(dark)
# лёгкое затемнение сверху под плашки «Новинка/Реклама»
tg = Image.new('L', (1, H))
for y in range(H): tg.putpixel((0, y), int(110 * max(0, 1 - y / 90)))
top = Image.new('RGBA', (W, H), (8, 10, 20, 255)); top.putalpha(tg.resize((W, H)))
canvas.alpha_composite(top)

# --- логотип: эмблема + словесный знак (левый нижний край области)
d = ImageDraw.Draw(canvas)
emb = load('Branding/logo.png').convert('RGBA').resize((112, 112), Image.LANCZOS)
LX, LY = 24, 410
canvas.alpha_composite(emb, (LX, LY))
fb = ImageFont.truetype('/usr/share/fonts/opentype/inter/Inter-Black.otf', 50)
word = 'ASTROPOLITY'; tx = LX + 126; ty = LY + 18
track = 3
def draw_tracked(dr, xy, text, font, fill, tr):
    x, y = xy
    for ch in text:
        dr.text((x, y), ch, font=font, fill=fill); x += dr.textlength(ch, font=font) + tr
    return x
# золотой градиент по тексту
mask = Image.new('L', (W, H), 0); md = ImageDraw.Draw(mask)
x_end = draw_tracked(md, (tx, ty), word, fb, 255, track)
gold = Image.new('RGBA', (W, H))
gd = ImageDraw.Draw(gold)
for y in range(ty, ty + 70):
    t = (y - ty) / 60
    c = (int(255 - 40 * t), int(232 - 70 * t), int(170 - 110 * t), 255)
    gd.line([(0, y), (W, y)], fill=c)
shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0)); shadow.putalpha(mask.filter(ImageFilter.GaussianBlur(6)).point(lambda v: int(v * 0.8)))
canvas.alpha_composite(shadow)
gold.putalpha(mask); canvas.alpha_composite(gold)
fs = ImageFont.truetype('/usr/share/fonts/opentype/inter/Inter-SemiBold.otf', 17)
draw_tracked(d, (tx + 2, ty + 64), 'Space 4X strategy', fs, (200, 214, 230, 255), 2)

# --- подзаголовок (макс. 2 строки, без капслока)
ft = ImageFont.truetype('/usr/share/fonts/opentype/inter/Inter-Medium.otf', 18)
lines = ['Постройте межзвёздную империю: исследуйте галактику,',
         'осваивайте новые миры и ведите дипломатию с другими расами']
y = 548
for ln in lines:
    d.text((LX + 1, y + 1), ln, font=ft, fill=(0, 0, 0, 160))
    d.text((LX, y), ln, font=ft, fill=(255, 255, 255, 255)); y += 26

out = canvas.convert('RGB')
q = 92
while True:
    out.save(OUT, 'JPEG', quality=q, optimize=True, progressive=True)
    if os.path.getsize(OUT) <= 500 * 1024 or q < 60: break
    q -= 4
print(OUT, out.size, os.path.getsize(OUT) // 1024, 'KB q', q)
