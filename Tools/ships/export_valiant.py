# Эсминец Доминиона Ксарн «Валиант» (Valiant.fbx из набора Valiant_Unity) -> Assets/Resources/Models/Ships/Valiant:
# valiant.bytes (PLM1, читает ValiantModel.cs) и valiant_config.json (сопла).
# Запуск через Blender как модуль Python (pip install bpy):
#   python -I export_valiant.py Valiant.fbx ../../Assets/Resources/Models/Ships/Valiant [доля_треугольников]
#
# Исходник — 345 тыс. треугольников со скелетом (64 кости, башни на костях). Для карты скелет не нужен: поза покоя
# запекается, сетка упрощается (Decimate), FX-ленты выхлопа и вспышек отбрасываются — выхлоп рисует сама игра по соплам.
# Материалы в FBX — однотонные PBR без UV, поэтому слои делятся по материалам, а цвета задаёт ValiantModel.cs.
# Оси: Blender (правая СК, Z вверх, нос по +Y) -> Unity (левая): (x, y, z) -> (x, z, y), обход треугольников наоборот.
# Длина по Z -> 1, центр габаритов — в нуле.
import sys, os, json, math, struct, uuid
import numpy as np
import bpy

# Слой PLM -> материалы FBX
LAYERS = {
    'hull':   ['MAIN_ARMOR'],
    'accent': ['SECONDARY_ARMOR'],                 # окрашивается цветом владельца
    'dark':   ['DARK_MECHANICAL', 'ENGINE_METAL'],
    'steel':  ['MACHINED_STEEL'],
    'glow':   ['ORANGE_EMISSION'],
    'glass':  ['GLASS_BRIDGE'],
}

def text_meta(path):
    if not os.path.exists(path + '.meta'):
        open(path + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)

def baked(obj, ratio):
    """Меш объекта в позе покоя (модификатор Armature применён), упрощённый и со сглаживанием по углу."""
    dec = obj.modifiers.new('dec', 'DECIMATE'); dec.ratio = ratio; dec.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, depsgraph=dg)
    me.transform(obj.matrix_world)
    # Жёсткие грани сохраняют резкость брони после упрощения
    for p in me.polygons: p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(35))
    me.calc_loop_triangles()
    return me

def collect(me, mats):
    """Вершины по углам треугольников (свои нормали у жёстких граней), затем слияние одинаковых."""
    names = [m.name.split('.')[0] if m else '' for m in me.materials]
    pos, nrm = [], []
    cn = me.corner_normals
    for t in me.loop_triangles:
        if names[t.material_index] not in mats: continue
        for li in t.loops:
            pos.append(me.vertices[me.loops[li].vertex_index].co[:])
            nrm.append(cn[li].vector[:])
    if not pos: return None
    return np.array(pos, np.float64), np.array(nrm, np.float64)

def main(src, dst, ratio):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.name.endswith('_FX')]
    fx = next((o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith('_FX')), None)
    baked_meshes = [baked(o, ratio) for o in meshes]

    raw = {}
    for name, mats in LAYERS.items():
        parts = [c for c in (collect(me, mats) for me in baked_meshes) if c is not None]
        if parts: raw[name] = (np.concatenate([p[0] for p in parts]), np.concatenate([p[1] for p in parts]))

    # Сопла: центры трёх лент выхлопа (материал FX_Exhaust_Soft) у кормы
    nozzles_b = []
    if fx is not None:
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(fx.evaluated_get(dg), depsgraph=dg); me.transform(fx.matrix_world)
        names = [m.name.split('.')[0] if m else '' for m in me.materials]
        vs = set()
        for p in me.polygons:
            if names[p.material_index] == 'FX_Exhaust_Soft': vs.update(p.vertices)
        ex = np.array([me.vertices[i].co[:] for i in vs])
        if len(ex):
            # Двигатели стоят в ряд: группы по ширине (x), разрыв больше 0.8 м — новое сопло;
            # сопло — передний (ближний к корпусу) край ленты
            order = np.argsort(ex[:, 0])
            groups, cur = [], [order[0]]
            for a, b in zip(order[:-1], order[1:]):
                if ex[b, 0] - ex[a, 0] > 0.8: groups.append(cur); cur = []
                cur.append(b)
            groups.append(cur)
            for g in groups:
                pts = ex[g]
                nozzles_b.append(((pts[:, 0].min() + pts[:, 0].max()) / 2, pts[:, 1].max(), (pts[:, 2].min() + pts[:, 2].max()) / 2,
                                  (pts[:, 0].max() - pts[:, 0].min()) * 0.4))

    allp = np.concatenate([v[0] for v in raw.values()])
    lo, hi = allp.min(0), allp.max(0)
    # Нос — противоположный соплам конец (по README нос по +Y; проверяем по выхлопу)
    flip = bool(nozzles_b) and np.mean([n[1] for n in nozzles_b]) > (lo[1] + hi[1]) / 2
    center = (lo + hi) / 2
    length = hi[1] - lo[1]

    def to_unity(p, normal=False):
        p = p if normal else (p - center) / length
        u = np.stack([p[:, 0], p[:, 2], p[:, 1]], 1)
        if flip: u[:, 0] *= -1; u[:, 2] *= -1
        return u

    layers = []
    for name, (p, n) in raw.items():
        up, un = to_unity(p), to_unity(n, True)
        un /= np.maximum(np.linalg.norm(un, axis=1, keepdims=True), 1e-9)
        key = np.concatenate([np.round(up * 2e5), np.round(un * 127)], 1).astype(np.int64)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
        idx = inv.reshape(-1, 3)[:, [0, 2, 1]].astype(np.uint32)
        layers.append((name, up[first].astype(np.float32), un[first], idx.reshape(-1)))

    os.makedirs(dst, exist_ok=True)
    if not os.path.exists(dst + '.meta'):
        open(dst + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    path = os.path.join(dst, 'valiant.bytes')
    with open(path, 'wb') as f:
        f.write(b'PLM1'); f.write(struct.pack('<i', len(layers)))
        for name, pos, nrm, idx in layers:
            nb = name.encode('utf-8')
            f.write(struct.pack('<B', len(nb))); f.write(nb)
            f.write(struct.pack('<iiB', len(pos), idx.size, 0))
            f.write(pos.astype('<f4').tobytes())
            f.write(np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes())
            f.write(idx.astype('<u4').tobytes())
    text_meta(path)

    nz = to_unity(np.array([n[:3] for n in nozzles_b])) if nozzles_b else np.zeros((0, 3))
    radius = float(np.mean([n[3] for n in nozzles_b]) / length) if nozzles_b else 0.02
    size = (hi - lo) / length
    cfg = dict(nozzles=[round(float(x), 4) for x in nz.reshape(-1)], nozzleRadius=round(radius, 4),
               width=round(float(size[0]), 4), height=round(float(size[2]), 4))
    p = os.path.join(dst, 'valiant_config.json')
    open(p, 'w', encoding='utf-8', newline='\n').write(json.dumps(cfg, indent=1)); text_meta(p)
    tris = sum(l[3].size // 3 for l in layers)
    print('слои', {l[0]: (len(l[1]), l[3].size // 3) for l in layers}, 'треугольников', tris, 'нос перевёрнут' if flip else '', cfg)

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 0.12)
