# Пиратский корабль (GLB со Sketchfab) -> Assets/Resources/Models/Ships/Pirate: pirate.bytes (формат PLM1, как у планет —
# читает PirateShipModel.cs), pirate_config.json (сопло двигателя) и текстуры (до 2048) с .meta-файлами.
# Запуск: python3 export_pirate.py space_ship.glb ../../Assets/Resources/Models/Ships/Pirate
# Нужны numpy и Pillow. Модель «Space ship», автор jkazulyblanco, CC BY 4.0 — автор указан в CREDITS.txt и в «Об игре».
#
# Оси: в GLB нос смотрит по +Z (стволы и кристалл впереди, кольцо двигателя сзади), верх — +Y. glTF (правая СК) -> Unity
# (левая): X меняет знак, обход треугольников наоборот, v -> 1 − v. Длина корпуса по Z -> 1, центр — в нуле.
import sys, os, io, json, struct, uuid
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'planets'))
from export_planets import GLB, fit   # чтение GLB и уменьшение картинок — общие с моделями планет

Image.MAX_IMAGE_PIXELS = None
COLOR_META = 'Assets/Resources/Planets/Real/earth_albedo.png.meta'
NORMAL_META = 'Assets/Resources/Planets/Real/earth_normal.png.meta'

def meta_for(path, root, normal=False, srgb=True):
    if os.path.exists(path + '.meta'): return
    tmpl = open(os.path.join(root, NORMAL_META if normal else COLOR_META)).read()
    out = []
    for l in tmpl.splitlines():
        if l.startswith('guid:'): l = 'guid: ' + uuid.uuid4().hex
        elif l.strip().startswith('sRGBTexture:'): l = l.split(':')[0] + ': ' + ('1' if srgb and not normal else '0')
        out.append(l)
    open(path + '.meta', 'w', newline='\n').write('\n'.join(out) + '\n')

def text_meta(path):
    if not os.path.exists(path + '.meta'):
        open(path + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)

def main(src, dst):
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
    dst = os.path.normpath(dst)
    os.makedirs(dst, exist_ok=True)
    if not os.path.exists(dst + '.meta'):
        open(dst + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    g = GLB(src)

    hull = g.mesh(1); crystal = g.mesh(0)
    zmin, zmax = hull[0][:, 2].min(), hull[0][:, 2].max()
    length = zmax - zmin
    center = np.array([(hull[0][:, 0].min() + hull[0][:, 0].max()) / 2, (hull[0][:, 1].min() + hull[0][:, 1].max()) / 2, (zmin + zmax) / 2])

    def prep(m):
        pos, nrm, uv, idx = m
        pos = (pos - center) / length
        pos[:, 0] *= -1; nrm = nrm.copy(); nrm[:, 0] *= -1
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
        return pos, nrm, np.stack([uv[:, 0], 1.0 - uv[:, 1]], 1), idx[:, [0, 2, 1]]

    layers = [('hull',) + prep(hull), ('crystal',) + prep(crystal)]

    # Сопло: кольцо двигателя у кормы по центру — вершины в последних 6% длины и не дальше 25% ширины от оси
    hp = layers[0][1]
    half_w = np.abs(hp[:, 0]).max()
    ring = hp[(hp[:, 2] < hp[:, 2].min() + 0.06) & (np.abs(hp[:, 0]) < half_w * 0.25)]
    if len(ring) == 0: ring = hp[hp[:, 2] < hp[:, 2].min() + 0.06]
    nozzle = [0.0, float(ring[:, 1].mean()), float(hp[:, 2].min())]
    nozzle_r = float(max(0.02, (ring[:, 1].max() - ring[:, 1].min()) * 0.45))

    with open(os.path.join(dst, 'pirate.bytes'), 'wb') as f:
        f.write(b'PLM1'); f.write(struct.pack('<i', len(layers)))
        for name, pos, nrm, uv, idx in layers:
            nb = name.encode('utf-8')
            f.write(struct.pack('<B', len(nb))); f.write(nb)
            f.write(struct.pack('<iiB', len(pos), idx.size, 1))
            f.write(pos.astype('<f4').tobytes())
            f.write(np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes())
            f.write(uv.astype('<f4').tobytes())
            f.write(idx.astype('<u4').tobytes())
    text_meta(os.path.join(dst, 'pirate.bytes'))

    # Текстуры корпуса: альбедо, металл (R) + затенение (G) + гладкость (A) одной картой, свечение, нормали
    def save_jpg(img, name, size=2048, normal=False, srgb=True, q=92):
        p = os.path.join(dst, name); fit(img.convert('RGB'), size).save(p, quality=q, optimize=True); meta_for(p, root, normal, srgb)
    save_jpg(g.image(0), 'hull_albedo.jpg')
    mr = np.asarray(fit(g.image(1).convert('RGB'), 1024)).astype(np.float32) / 255.0   # glTF: R — затенение, G — шероховатость, B — металл
    ms = np.zeros(mr.shape[:2] + (4,), np.uint8)
    ms[..., 0] = np.round(mr[..., 2] * 255); ms[..., 1] = np.round(mr[..., 0] * 255); ms[..., 3] = np.round((1 - mr[..., 1]) * 255)
    p = os.path.join(dst, 'hull_ms.png'); Image.fromarray(ms, 'RGBA').save(p, optimize=True); meta_for(p, root, srgb=False)
    save_jpg(g.image(2), 'hull_emission.jpg')
    save_jpg(g.image(3), 'hull_normal.jpg', normal=True, srgb=False, q=95)
    # Кристалл светится однотонно — его цвет задаёт игра (цвет владельца), текстура не нужна

    cfg = dict(nozzle=nozzle, nozzleRadius=nozzle_r, width=float(half_w * 2), height=float(hp[:, 1].max() - hp[:, 1].min()))
    p = os.path.join(dst, 'pirate_config.json'); open(p, 'w', encoding='utf-8', newline='\n').write(json.dumps(cfg, indent=1)); text_meta(p)

    ex = g.js.get('asset', {}).get('extras', {})
    p = os.path.join(dst, 'CREDITS.txt')
    open(p, 'w', encoding='utf-8', newline='\n').write(
        'Пиратский корабль — «%s», автор %s, лицензия %s, %s\nСконвертирован Tools/ships/export_pirate.py; текстуры уменьшены.\n'
        % (ex.get('title', '?'), ex.get('author', '?'), ex.get('license', '?'), ex.get('source', '')))
    text_meta(p)
    print('корпус', len(layers[0][1]), 'вершин, кристалл', len(layers[1][1]), '; сопло', nozzle, 'r', nozzle_r, cfg)

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
