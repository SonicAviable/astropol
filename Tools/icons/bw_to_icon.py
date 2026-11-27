"""Белый рисунок на чёрном фоне → белый значок с прозрачностью 512×512 (игра сама красит в цвет направления).
python3 Tools/icons/bw_to_icon.py <картинка> <выходной.png> [<картинка> <выходной.png> ...]"""
import sys
from PIL import Image
import numpy as np

def convert(src, dst, size=512):
    a = np.asarray(Image.open(src).convert('L').resize((size, size), Image.LANCZOS)).astype(np.float32)
    alpha = np.clip((a - 18) / (255 - 18) * 255 * 1.15, 0, 255).astype(np.uint8)
    rgba = np.zeros((size, size, 4), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = alpha
    Image.fromarray(rgba, 'RGBA').save(dst, optimize=True)

if __name__ == '__main__':
    args = sys.argv[1:]
    for i in range(0, len(args), 2):
        convert(args[i], args[i + 1])
        print(args[i + 1])
