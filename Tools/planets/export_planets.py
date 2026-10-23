# Модели планет (GLB со Sketchfab) -> Assets/Resources/Planets/Models: planets.bytes (формат PLM1, читает PlanetModelLibrary.cs),
# planets_config.json (материалы и какие типы планет заменяет модель) и текстуры (до 2048, JPG/PNG) с .meta-файлами.
# Запуск: python3 export_planets.py <папка с .glb> ../../Assets/Resources/Planets/Models [--allow-nc]
# Лицензии (из asset.extras каждого GLB) — в CREDITS.txt рядом с моделями. Модели с некоммерческой лицензией
# (CC BY-NC) в платную игру не берутся: lava_planet пропускается, пока не указан --allow-nc (нужно разрешение автора).
# Нужны numpy и Pillow; для упрощения тяжёлой planet_one — fast_simplification (иначе она берётся как есть).
#
# Оси: полюс модели — по UV (вершины с v≈0 — верх картинки) поворачивается в +Y, затем glTF (правая СК) -> Unity (левая):
# X меняет знак, обход треугольников переворачивается. Радиус поверхности -> 1, облака и полосы — своей оболочкой.
import sys, os, io, json, struct, uuid
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

# ---------------------------------------------------------------- что и как берём из каждой модели
# types — какие типы планет (PlanetType) получают модель и с каким весом (доля планет этого типа).
# surface/clouds — индексы мешей; tex — индексы картинок glTF. Цвета и силы подобраны под освещение игры.
MODELS = {
    'lava_planet': dict(key='lava', title='Лавовый мир', types={'Molten': 0.4}, noncommercial=True,
        surface=0, tex=dict(albedo=0, ms=1, emission=2, normal=3),
        smoothness=0.45, emission=[1.0, 0.42, 0.12], emissionIntensity=2.2, normalScale=1.0,
        atmo=dict(color=[1.0, 0.35, 0.10], intensity=0.85, shell=1.045)),
    'fire_planet': dict(key='fire', title='Огненный мир', types={'Molten': 0.5},
        surface=0, tex=dict(albedo=0, ms=1, emission=2),
        smoothness=0.25, emission=[1.0, 0.55, 0.18], emissionIntensity=2.6,
        atmo=dict(color=[1.0, 0.5, 0.18], intensity=0.7, shell=1.04)),
    'green_planet': dict(key='verdant', title='Зелёный мир', types={'Continental': 0.35},
        surface=0, clouds=1, tex=dict(albedo=0, ms=1, emission=2, normal=3, clouds=5),
        smoothness=0.35, emission=[1.0, 0.9, 0.7], emissionIntensity=0.26, normalScale=0.8,
        cloudsTint=[0.86, 0.92, 0.78], cloudsOpacity=0.85, cloudsShell=1.012,
        atmo=dict(color=[0.5, 0.9, 0.55], intensity=1.05, shell=1.055)),
    'neptune': dict(key='ice_giant', title='Ледяной гигант', types={'GasGiant': 0.4},
        surface=0, clouds=1, tex=dict(albedo=0, ms=1, clouds=2),
        smoothness=0.15, albedoTint=[0.72, 0.72, 0.72],
        cloudsTint=[0.82, 0.9, 1.0], cloudsOpacity=0.45, cloudsShell=1.006,
        atmo=dict(color=[0.35, 0.6, 1.0], intensity=1.25, shell=1.06)),
    'planet_one': dict(key='obsidian', title='Обсидиановый мир', types={'Barren': 0.3},
        surfaceAll=True, reduce=0.75, tex=dict(),
        albedoTint=[0.10, 0.10, 0.12], metallic=0.15, smoothness=0.85,
        atmo=dict(color=[0.55, 0.65, 0.85], intensity=0.35, shell=1.035)),
}

# ---------------------------------------------------------------- чтение GLB
class GLB:
    def __init__(s, path):
        d = open(path, 'rb').read()
        _, _, L = struct.unpack_from('<III', d, 0); o = 12
        while o < L:
            cl, ct = struct.unpack_from('<II', d, o); o += 8; ch = d[o:o + cl]; o += cl
            if ct == 0x4E4F534A: s.js = json.loads(ch)
            elif ct == 0x004E4942: s.bin = ch

    def acc(s, i):
        a = s.js['accessors'][i]; bv = s.js['bufferViews'][a['bufferView']]
        dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0); stride = bv.get('byteStride', 0); cnt = a['count']
        if stride and stride != dt.itemsize * n:
            raw = np.frombuffer(s.bin, dtype=np.uint8, count=stride * cnt, offset=off).reshape(cnt, stride)
            arr = raw[:, :dt.itemsize * n].copy().view(dt).reshape(cnt, n)
        else:
            arr = np.frombuffer(s.bin, dtype=dt, count=cnt * n, offset=off).reshape(cnt, n)
        return np.array(arr)

    def image(s, i):
        bv = s.js['bufferViews'][s.js['images'][i]['bufferView']]; o = bv.get('byteOffset', 0)
        return Image.open(io.BytesIO(s.bin[o:o + bv['byteLength']]))

    def mesh(s, m):
        p = s.js['meshes'][m]['primitives'][0]; at = p['attributes']
        pos = s.acc(at['POSITION']).astype(np.float64)
        nrm = s.acc(at['NORMAL']).astype(np.float64) if 'NORMAL' in at else None
        uv = s.acc(at['TEXCOORD_0']).astype(np.float64) if 'TEXCOORD_0' in at else None
        idx = s.acc(p['indices']).astype(np.int64).reshape(-1, 3)
        return pos, nrm, uv, idx

# ---------------------------------------------------------------- геометрия
def rot_to_y(p):
    """Поворот, переводящий единичный вектор p в +Y (формула Родрига)."""
    y = np.array([0.0, 1.0, 0.0]); p = p / np.linalg.norm(p)
    c = float(np.dot(p, y))
    if c > 0.9999: return np.eye(3)
    if c < -0.9999: return np.diag([1.0, -1.0, -1.0])   # 180° вокруг X
    v = np.cross(p, y); s = np.linalg.norm(v); k = v / s
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    return np.eye(3) + s * K + (1 - c) * (K @ K)

def vertex_normals(pos, idx):
    n = np.zeros_like(pos)
    fn = np.cross(pos[idx[:, 1]] - pos[idx[:, 0]], pos[idx[:, 2]] - pos[idx[:, 0]])
    for k in range(3): np.add.at(n, idx[:, k], fn)
    l = np.linalg.norm(n, axis=1, keepdims=True); l[l == 0] = 1
    return n / l

def prepare(pos, nrm, uv, idx, R, radius):
    """Поворот полюсом вверх, радиус, переход в левую СК Unity (X -> −X, обход наоборот), UV: v -> 1 − v."""
    pos = pos @ R.T / radius
    nrm = (nrm @ R.T) if nrm is not None else vertex_normals(pos, idx)
    pos[:, 0] *= -1; nrm[:, 0] *= -1
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    idx = idx[:, [0, 2, 1]]
    if uv is not None: uv = np.stack([uv[:, 0], 1.0 - uv[:, 1]], axis=1)
    return pos, nrm, uv, idx

def pole_rotation(pos, uv):
    """Верх картинки (v≈0) — северный полюс: направление на такие вершины поворачиваем в +Y."""
    if uv is None: return np.eye(3)
    top = pos[uv[:, 1] < 0.02]
    if len(top) == 0: return np.eye(3)
    return rot_to_y(top.mean(0))

def write_layer(f, name, pos, nrm, uv, idx):
    nb = name.encode('utf-8')
    f.write(struct.pack('<B', len(nb))); f.write(nb)
    f.write(struct.pack('<iiB', len(pos), idx.size, 1 if uv is not None else 0))
    f.write(pos.astype('<f4').tobytes())
    f.write(np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes())
    if uv is not None: f.write(uv.astype('<f4').tobytes())
    f.write(idx.astype('<u4').tobytes())

# ---------------------------------------------------------------- текстуры
def meta_for(out_path, normal=False, srgb=True):
    if os.path.exists(out_path + '.meta'): return   # guid уже есть — не меняем
    tmpl = TEMPLATES['normal' if normal else 'color']
    lines = []
    for l in tmpl.splitlines():
        if l.startswith('guid:'): l = 'guid: ' + uuid.uuid4().hex
        elif l.strip().startswith('sRGBTexture:'): l = l.split(':')[0] + ': ' + ('1' if srgb and not normal else '0')
        lines.append(l)
    open(out_path + '.meta', 'w', newline='\n').write('\n'.join(lines) + '\n')

def fit(img, maxw):
    w, h = img.size
    if w <= maxw: return img
    return img.resize((maxw, max(1, round(h * maxw / w))), Image.LANCZOS)

def save_jpg(img, path, size, normal=False, srgb=True, q=92):
    img = fit(img.convert('RGB'), size)
    img.save(path, quality=q, optimize=True)
    meta_for(path, normal=normal, srgb=srgb)

def save_ms(img, path, metallic_default, size=1024):
    """glTF: G — шероховатость, B — металл -> Unity URP: R — металл, A — гладкость."""
    img = fit(img.convert('RGB'), size)
    a = np.asarray(img).astype(np.float32) / 255.0
    out = np.zeros(a.shape[:2] + (4,), np.uint8)
    out[..., 0] = np.round(a[..., 2] * 255)
    out[..., 3] = np.round((1.0 - a[..., 1]) * 255)
    Image.fromarray(out, 'RGBA').save(path, optimize=True)
    meta_for(path, srgb=False)

def save_mask(img, path, size=2048):
    """Облака/полосы: маска плотности в R (из альфы, если она есть)."""
    img = fit(img, size)
    if img.mode in ('LA', 'RGBA'): m = img.getchannel('A')
    else: m = img.convert('L')
    m.save(path, quality=92)
    meta_for(path, srgb=False)

# ---------------------------------------------------------------- сборка
def main(src, dst, allow_nc=False):
    global TEMPLATES
    dst = os.path.normpath(dst)
    real = os.path.join(os.path.dirname(dst), 'Real')
    TEMPLATES = {'color': open(os.path.join(real, 'earth_albedo.png.meta')).read(),
                 'normal': open(os.path.join(real, 'earth_normal.png.meta')).read()}
    os.makedirs(dst, exist_ok=True)
    if not os.path.exists(dst + '.meta'):
        open(dst + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    config = {'models': []}
    layers = []
    credits = []
    for fname, m in MODELS.items():
        path = os.path.join(src, fname + '.glb')
        if not os.path.exists(path):
            print('нет файла', path); continue
        if m.get('noncommercial') and not allow_nc:
            print('пропуск (некоммерческая лицензия):', fname); continue
        g = GLB(path); key = m['key']
        ex = g.js.get('asset', {}).get('extras', {})
        if ex: credits.append('%s — «%s», автор %s, лицензия %s, %s' % (m['title'], ex.get('title', fname), ex.get('author', '?'), ex.get('license', '?'), ex.get('source', '')))
        folder = os.path.join(dst, key); os.makedirs(folder, exist_ok=True)
        entry = dict(key=key, title=m['title'], types=[], weights=[],
                     smoothness=m.get('smoothness', 0.3), metallic=m.get('metallic', 0.0),
                     albedoTint=m.get('albedoTint', [1, 1, 1]), emission=m.get('emission', [0, 0, 0]),
                     emissionIntensity=m.get('emissionIntensity', 0.0), normalScale=m.get('normalScale', 1.0),
                     cloudsTint=m.get('cloudsTint', [1, 1, 1]), cloudsOpacity=m.get('cloudsOpacity', 0.0),
                     cloudsShell=m.get('cloudsShell', 1.01),
                     atmoColor=m['atmo']['color'], atmoIntensity=m['atmo']['intensity'], atmoShell=m['atmo']['shell'],
                     albedo='', ms='', emissionMap='', normal='', clouds='')
        for t, w in m['types'].items(): entry['types'].append(t); entry['weights'].append(w)

        # Поверхность
        if m.get('surfaceAll'):
            parts = [g.mesh(i) for i in range(len(g.js['meshes']))]
            allpos = np.concatenate([p[0] for p in parts])
            radius = np.percentile(np.linalg.norm(allpos, axis=1), 5)   # «уровень моря» — основание плит
            for i, (pos, nrm, uv, idx) in enumerate(parts):
                if m.get('reduce'):
                    try:
                        import fast_simplification as fs
                        pos, idx = fs.simplify(pos, idx.astype(np.int32), target_reduction=m['reduce'])
                        nrm = None
                    except ImportError:
                        print('  fast_simplification нет — без упрощения')
                pos, nrm, uv, idx = prepare(pos, nrm, None, idx, np.eye(3), radius)
                layers.append((key + '/surface' + str(i), pos, nrm, None, idx))
                print(f'  {key} surface{i}: {len(pos)} вершин, {len(idx)} треугольников')
        else:
            pos, nrm, uv, idx = g.mesh(m['surface'])
            R = pole_rotation(pos, uv)
            radius = np.linalg.norm(pos, axis=1).max()
            pos, nrm, uv, idx = prepare(pos, nrm, uv, idx, R, radius)
            layers.append((key + '/surface', pos, nrm, uv, idx))
            print(f'  {key} surface: {len(pos)} вершин')
        if 'clouds' in m:
            pos, nrm, uv, idx = g.mesh(m['clouds'])
            R = pole_rotation(pos, uv)
            radius = np.linalg.norm(pos, axis=1).max()
            pos, nrm, uv, idx = prepare(pos, nrm, uv, idx, R, radius)
            layers.append((key + '/clouds', pos, nrm, uv, idx))

        # Текстуры
        t = m['tex']
        if 'albedo' in t:
            save_jpg(g.image(t['albedo']), os.path.join(folder, 'albedo.jpg'), 2048); entry['albedo'] = 'albedo'
        if 'ms' in t:
            save_ms(g.image(t['ms']), os.path.join(folder, 'ms.png'), m.get('metallic', 0.0)); entry['ms'] = 'ms'
        if 'emission' in t:
            save_jpg(g.image(t['emission']), os.path.join(folder, 'emission.jpg'), 2048); entry['emissionMap'] = 'emission'
        if 'normal' in t:
            save_jpg(g.image(t['normal']), os.path.join(folder, 'normal.jpg'), 2048, normal=True, srgb=False, q=95); entry['normal'] = 'normal'
        if 'clouds' in t:
            save_mask(g.image(t['clouds']), os.path.join(folder, 'clouds.jpg')); entry['clouds'] = 'clouds'
        folder_meta = folder + '.meta'
        if not os.listdir(folder):
            os.rmdir(folder)   # текстур нет — папка не нужна
        elif not os.path.exists(folder_meta):
            open(folder_meta, 'w', newline='\n').write(
                'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
        config['models'].append(entry)
        print('готово:', key)

    out = os.path.join(dst, 'planets.bytes')
    with open(out, 'wb') as f:
        f.write(b'PLM1'); f.write(struct.pack('<i', len(layers)))
        for name, pos, nrm, uv, idx in layers: write_layer(f, name, pos, nrm, uv, idx)
    jpath = os.path.join(dst, 'planets_config.json')
    open(jpath, 'w', encoding='utf-8', newline='\n').write(json.dumps(config, ensure_ascii=False, indent=1))
    for p in (out, jpath):
        if not os.path.exists(p + '.meta'):
            open(p + '.meta', 'w', newline='\n').write(
                'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    cpath = os.path.join(dst, 'CREDITS.txt')
    open(cpath, 'w', encoding='utf-8', newline='\n').write(
        'Модели планет (Sketchfab), сконвертированы Tools/planets/export_planets.py; уменьшены текстуры, упрощена сетка.\n\n' + '\n'.join(credits) + '\n')
    if not os.path.exists(cpath + '.meta'):
        open(cpath + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    print('planets.bytes:', os.path.getsize(out) // 1024, 'КБ')

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], '--allow-nc' in sys.argv[3:])
