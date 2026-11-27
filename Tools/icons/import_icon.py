# Рисованная иконка (белый рисунок на чёрном фоне, например из генератора) -> Resources/UI/Icons/<имя>.png:
# белый силуэт на прозрачном фоне 256×256, как у остальных рисованных иконок (содержимое — 238 px по большей стороне, по центру).
# Прозрачность берётся из яркости, так что сглаженные края и тонкие прорези сохраняются.
# Запуск: python3 import_icon.py картинка.jpg имя [картинка2.png имя2 ...]
import sys, os
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Resources', 'UI', 'Icons')
SIZE, CONTENT = 256, 238

def convert(src, name):
    lum = np.asarray(Image.open(src).convert('L')).astype(np.float32) / 255
    alpha = np.clip((lum - 0.08) / 0.84, 0, 1)            # чистим JPEG-шум фона, белое -> 1
    ys, xs = np.where(alpha > 0.5)
    if len(xs) == 0: raise SystemExit(f'{src}: на картинке нет светлого рисунка')
    pad = 4
    x0, x1 = max(0, xs.min() - pad), min(alpha.shape[1], xs.max() + pad + 1)
    y0, y1 = max(0, ys.min() - pad), min(alpha.shape[0], ys.max() + pad + 1)
    crop = Image.fromarray((alpha[y0:y1, x0:x1] * 255).astype(np.uint8))
    k = CONTENT / max(crop.size)
    crop = crop.resize((max(1, round(crop.width * k)), max(1, round(crop.height * k))), Image.LANCZOS)
    a = Image.new('L', (SIZE, SIZE), 0)
    a.paste(crop, ((SIZE - crop.width) // 2, (SIZE - crop.height) // 2))
    img = Image.merge('RGBA', (Image.new('L', a.size, 255),) * 3 + (a,))
    dst = os.path.join(OUT, name + '.png')
    img.save(dst, optimize=True)
    print(f'{src} -> {os.path.normpath(dst)}')

args = sys.argv[1:]
if not args or len(args) % 2: raise SystemExit(__doc__ or 'python3 import_icon.py картинка имя [...]')
for i in range(0, len(args), 2): convert(args[i], args[i + 1])
