#!/usr/bin/env python3
"""Готовит картинки сайта из ресурсов игры (уменьшает и конвертирует в webp/png).
Запуск из корня репозитория:  python3 website/tools/build_images.py"""
import os, glob
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
R = os.path.join(ROOT, 'Assets', 'Resources')
O = os.path.join(ROOT, 'website', 'img')
for d in ['', 'leaders', 'emblems', 'events', 'icons']: os.makedirs(os.path.join(O, d), exist_ok=True)

def fit(im, w):
    if im.width <= w: return im
    return im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)

def save(im, name, fmt, **kw):
    p = os.path.join(O, name); im.save(p, fmt, **kw); return os.path.getsize(p)

total = 0
total += save(fit(Image.open(f'{R}/Menu/menu_bg.jpg').convert('RGB'), 1800), 'nebula.jpg', 'JPEG', quality=78, optimize=True, progressive=True)
total += save(fit(Image.open(f'{R}/Menu/menu_planet.png').convert('RGBA'), 1100), 'planet.webp', 'WEBP', quality=82, method=6)
total += save(fit(Image.open(f'{R}/UI/LoadingArt.jpg').convert('RGB'), 1600), 'galaxy.jpg', 'JPEG', quality=78, optimize=True, progressive=True)
total += save(fit(Image.open(f'{R}/UI/Tutor.jpg').convert('RGB'), 640), 'advisor.webp', 'WEBP', quality=82, method=6)
for k in ['astrea', 'xarn', 'aquila', 'iridia', 'terraan', 'nol']:
    total += save(fit(Image.open(f'{R}/Leaders/leader_{k}.jpg').convert('RGB'), 700), f'leaders/{k}.webp', 'WEBP', quality=82, method=6)
    total += save(fit(Image.open(f'{R}/Factions/emblem_{k}.png').convert('RGBA'), 320), f'emblems/{k}.png', 'PNG', optimize=True)
for f in sorted(glob.glob(f'{R}/UI/Events/*.jpg')):
    total += save(fit(Image.open(f).convert('RGB'), 1100), 'events/' + os.path.basename(f)[:-4].lower() + '.webp', 'WEBP', quality=78, method=6)
for f in sorted(glob.glob(f'{R}/UI/Icons/*.png')):
    total += save(fit(Image.open(f).convert('RGBA'), 112), 'icons/' + os.path.basename(f), 'PNG', optimize=True)
print('готово,', total // 1024, 'КБ')
