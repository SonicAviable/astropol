# Научный корабль Иридийского Оракула («Взор», по концепту) — модель целиком строится этим скриптом в Blender.
#
# Корпус-капля из перламутровых пластин поверх фиолетового основания с филигранью, ромбовидные кристаллы в оправах,
# перистые лопасти в X-схеме, две гондолы двигателей, тонкие шпили. На спине — гироскоп-оракул: три вложенных
# кольца вокруг парящего кристалла. Кольца и кристалл — отдельные слои с общей точкой вращения: игра анимирует их
# (IridiaScienceModel.cs), кристаллы корпуса пульсируют.
#
# Общие функции (детали, лофт, запекание, PLM1, .meta, рендер) — из xarn_science.py.
# Запуск (bpy 5.2.3, numpy, pillow; Python 3.13):
#   python iridia_science.py [--out ../../Assets/Resources/Models/Ships/IridiaScience] [--blend iridia_science.blend]
#                            [--tex 2048] [--render папка] [--quick папка] [--gif папка]
# Оси Blender: нос +Y, верх +Z.
import sys, os, math, json, argparse, time
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import xarn_science as K
from xarn_science import P, cyl, sphere, rod, loft, lathe, node_math, mix

ROOT = K.ROOT
GYRO = Vector((0.0, -0.45, 1.6))     # центр гироскопа: кольца и кристалл вращаются вокруг него
RINGS = [(0.74, 0.062, 0.03), (0.61, 0.056, 0.028), (0.49, 0.05, 0.026)]   # радиус, полуширина ленты, полутолщина

MATS = {
    'pearl':   dict(col=(0.70, 0.69, 0.74), metal=0.25, rough=0.22, kind='pearl'),
    'accent':  dict(col=(0.62, 0.62, 0.64), metal=0.35, rough=0.3, kind='filigree'),     # в игре × цвет владельца
    'ring0':   dict(col=(0.62, 0.62, 0.66), metal=0.9, rough=0.22, kind='filigree'),
    'ring1':   dict(col=(0.62, 0.62, 0.66), metal=0.9, rough=0.22, kind='filigree'),
    'ring2':   dict(col=(0.62, 0.62, 0.66), metal=0.9, rough=0.22, kind='filigree'),
    'silver':  dict(col=(0.6, 0.6, 0.64), metal=0.9, rough=0.25, kind='plain'),
    'dark':    dict(col=(0.06, 0.045, 0.08), metal=0.6, rough=0.35, kind='plain'),
    'crystal': dict(col=(0.35, 0.15, 0.55), metal=0.0, rough=0.08, kind='crystal', emit=(0.62, 0.3, 1.0)),
    'gem':     dict(col=(0.35, 0.15, 0.55), metal=0.0, rough=0.08, kind='crystal', emit=(0.62, 0.3, 1.0)),
    'engine':  dict(col=(0.3, 0.15, 0.4), metal=0.0, rough=0.3, kind='plain', emit=(0.75, 0.5, 1.0)),
}
LAYER_OF = {'accent': 'accent', 'ring0': 'ring0', 'ring1': 'ring1', 'ring2': 'ring2', 'crystal': 'crystal', 'gem': 'gems'}
EMIT_STRENGTH = 1.0   # запекание — 1 (значения 0..1); быстрый рендер ярче
ORDER = ('hull', 'accent', 'ring0', 'ring1', 'ring2', 'crystal', 'gems')
NOZZLES = [(0.42, -0.02), (-0.42, -0.02)]      # x, z сопел; корма — y = −4.38


# ============================================================================ форма корпуса

KEYS = np.array([  # y, полуширина, верх, низ — капля: острый нос, самое широкое место у гироскопа
    (-3.95, 0.55, 0.36, 0.32), (-3.4, 0.74, 0.46, 0.38), (-2.4, 0.92, 0.56, 0.45), (-1.2, 1.02, 0.6, 0.48),
    (-0.4, 1.04, 0.6, 0.48), (0.6, 0.98, 0.56, 0.44), (1.8, 0.84, 0.47, 0.36), (3.0, 0.62, 0.35, 0.26),
    (4.0, 0.36, 0.2, 0.15), (4.6, 0.16, 0.09, 0.07), (5.0, 0.0, 0.0, 0.0)])


def hull_at(y):
    return tuple(float(np.interp(y, KEYS[:, 0], KEYS[:, i])) for i in (1, 2, 3))


def sect(y, th, d=0.0):
    """Точка сечения: th — угол (0 — правый борт, 90° — верх). Сечение — линза с острым боковым краем."""
    w, ht, hb = hull_at(y)
    c, s = math.cos(th), math.sin(th)
    h = ht if s >= 0 else hb
    x = (w + d) * math.copysign(abs(c) ** 0.85, c)
    z = (h + d) * math.copysign(abs(s) ** 1.15, s)
    if s > 0:   # хребет: верх чуть приподнят к середине
        z += 0.06 * max(0.0, 1 - abs(x) / max(w, 1e-3) / 0.35) * h / max(ht, 1e-3)
    return (x, y, z)


YS = list(np.linspace(-3.95, 4.6, 34)) + [4.85, 5.0]
SEG = 40


def shell(p, y0, y1, th0, th1, d=0.025, n=12, taper=None):
    """Пластина поверх корпуса между углами th0..th1 (град), концы скошены; taper(y)->доля ширины полосы."""
    ys = [y0, y0 + 0.06] + list(np.linspace(y0 + 0.12, y1 - 0.12, n)) + [y1 - 0.06, y1]
    rings = []
    for i, y in enumerate(ys):
        dd = d * (0.2 if i in (0, len(ys) - 1) else 1.0)
        k = taper(y) if taper else 1.0
        mid, half = (th0 + th1) / 2, (th1 - th0) / 2 * k
        rings.append([sect(y, math.radians(mid + half * t), dd) for t in np.linspace(-1, 1, 9)])
    loft(p, rings, closed=False)


def gem(pg, pb, center, normal, length, width, height, along=(0, 1, 0)):
    """Ромбовидный кристалл в оправе: верх — грани к вершине, низ утоплен в корпус."""
    n = Vector(normal).normalized(); a = Vector(along)
    a = (a - n * a.dot(n)).normalized(); b = n.cross(a)
    c = Vector(center)
    us = (1.0, 0.6, 0.2, -0.2, -0.6)   # линза с острыми концами — «глаз»
    girdle = [(u, 1 - u * u) for u in us] + [(-1.0, 0.0)] + [(u, -(1 - u * u)) for u in us[::-1][:-1]]
    G = len(girdle)
    pts = [c + a * (u * length / 2) + b * (v * width / 2) for u, v in girdle]
    bm = pg.bm
    g = [bm.verts.new(q) for q in pts]
    crown = [bm.verts.new(c + n * height * 0.7 + a * (u * length / 2 * 0.45) + b * (v * width / 2 * 0.45)) for u, v in girdle]
    top = bm.verts.new(c + n * height)
    bot = bm.verts.new(c - n * height * 0.4)
    for i in range(G):
        j = (i + 1) % G
        bm.faces.new((g[i], g[j], crown[j], crown[i]))
        bm.faces.new((crown[i], crown[j], top))
        bm.faces.new((g[j], g[i], bot))
    # оправа — серебряная рамка по контуру
    outer = [c + a * (u * (length / 2 + 0.05)) + b * (v * (width / 2 + 0.05)) - n * 0.02 for u, v in girdle]
    inner = [c + a * (u * (length / 2 + 0.01)) + b * (v * (width / 2 + 0.01)) + n * 0.025 for u, v in girdle]
    o = [pb.bm.verts.new(q) for q in outer]
    i_ = [pb.bm.verts.new(q) for q in inner]
    ob = [pb.bm.verts.new(q - n * 0.05) for q in outer]
    for i in range(G):
        j = (i + 1) % G
        pb.bm.faces.new((o[i], o[j], i_[j], i_[i]))
        pb.bm.faces.new((ob[i], ob[j], o[j], o[i]))


def blade(p, root, up_deg, span, sweep, c0, t0, side=1, curl=0.15):
    """Перистая лопасть: из точки root наружу под углом up_deg к горизонту, отогнута назад."""
    r = math.radians(up_deg)
    d = Vector((side * math.cos(r), 0.0, math.sin(r)))
    nrm = Vector((0, 1, 0)).cross(d).normalized()
    rings = []
    N = 9
    for i in range(N + 1):
        s = i / N
        spine = Vector(root) + d * span * s + Vector((0, -sweep * s ** 1.4, 0)) + nrm * (curl * math.sin(s * math.pi) * 0.5)
        c = c0 * (0.4 + 0.6 * math.sin(math.pi * min(1.0, s * 1.15))) * (1 - s) ** 0.35 + 0.02   # лист: узкий у корня, острый конец
        t = t0 * (1 - s) + 0.006
        le = spine + Vector((0, c * 0.3, 0))
        te = spine + Vector((0, -c * 0.7, 0)) - d * (0.12 * c * s)
        mid = spine + Vector((0, -c * 0.15, 0))
        rings.append([le, mid + nrm * t, te, mid - nrm * t])
    loft(p, rings)


def build():
    K.Part.all.clear()
    # ---------------------------------------------------------------- основание корпуса (фиолетовое, с филигранью)
    base = P('Hull', 'accent', bevel=0.0)
    loft(base, [[sect(y, 2 * math.pi * i / SEG) for i in range(SEG)] for y in YS[:-1]], caps=False)
    bm = base.bm
    last = [v for v in bm.verts if abs(v.co.y - YS[-2]) < 1e-4]
    tip = bm.verts.new((0, 5.0, 0))
    ring = sorted(last, key=lambda v: math.atan2(v.co.z, v.co.x))
    for i in range(len(ring)):
        bm.faces.new((ring[i], ring[(i + 1) % len(ring)], tip))
    first = [v for v in bm.verts if abs(v.co.y - YS[0]) < 1e-4]
    bm.faces.new(sorted(first, key=lambda v: math.atan2(v.co.z, v.co.x)))

    # ---------------------------------------------------------------- перламутровые пластины
    sh = P('ShellTop', 'pearl', bevel=0.012, solid=0.03)
    shell(sh, 0.35, 4.55, 22, 158, taper=lambda y: float(np.interp(y, [0.35, 1.0, 3.6, 4.55], [0.55, 1.0, 0.6, 0.12])))
    shell(sh, -3.7, -1.2, 25, 155, taper=lambda y: float(np.interp(y, [-3.7, -3.0, -1.6, -1.2], [0.5, 1.0, 1.0, 0.55])))
    shf = P('ShellFlank', 'pearl', bevel=0.01, solid=0.028)
    for a0, a1 in ((-62, -12), (192, 242)):
        shell(shf, -3.3, 3.2, a0, a1, taper=lambda y: float(np.interp(y, [-3.3, -2.6, 2.2, 3.2], [0.3, 1.0, 0.9, 0.15])))
    shb = P('ShellBelly', 'pearl', bevel=0.01, solid=0.028)
    shell(shb, -2.6, 2.4, 238, 302, taper=lambda y: float(np.interp(y, [-2.6, -1.8, 1.4, 2.4], [0.3, 1.0, 1.0, 0.25])))

    # ---------------------------------------------------------------- воротник гироскопа и опора
    w, ht, hb = hull_at(GYRO.y)
    ztop = ht + 0.1
    col = P('Collar', 'silver', bevel=0.008)
    lathe(col, [(0.66, ztop - 0.12), (0.7, ztop - 0.02), (0.66, ztop + 0.05), (0.58, ztop + 0.06), (0.56, ztop - 0.02), (0.66, ztop - 0.12)],
          seg=48, center=(0, GYRO.y, 0))
    rec = P('CollarWell', 'dark', solid=0.01)
    lathe(rec, [(0.0, ztop), (0.57, ztop), (0.57, ztop - 0.12)], seg=48, center=(0, GYRO.y, 0))
    eng = P('WellGlow', 'engine', solid=0.01)
    lathe(eng, [(0.18, ztop + 0.012), (0.26, ztop + 0.012)], seg=32, center=(0, GYRO.y, 0))
    cr = P('Cradle', 'silver', bevel=0.006)
    bot = GYRO.z - RINGS[0][0] - RINGS[0][2] - 0.01
    cyl(cr, (0, GYRO.y, (ztop + bot) / 2), 0.07, 0.045, bot - ztop + 0.04, seg=16, axis='Z')
    for sx in (-1, 1):   # изогнутые лапы-держатели
        rod(cr, (sx * 0.42, GYRO.y, ztop), (sx * 0.18, GYRO.y, bot - 0.02), 0.022, seg=8)
        rod(cr, (0, GYRO.y + sx * 0.42, ztop), (0, GYRO.y + sx * 0.16, bot - 0.02), 0.018, seg=8)

    # ---------------------------------------------------------------- гироскоп: три кольца и кристалл
    rots = [Matrix.Rotation(math.pi / 2, 4, 'Y'),                                    # внешнее — в плоскости YZ
            Matrix.Rotation(math.pi / 2, 4, 'X'),                                    # среднее — в плоскости XZ
            Matrix.Rotation(math.radians(45), 4, 'Z') @ Matrix.Rotation(math.radians(55), 4, 'X')]   # внутреннее — наклонное
    for k, ((R, bw, bt), rot) in enumerate(zip(RINGS, rots)):
        rp = P('Ring%d' % k, 'ring%d' % k, bevel=0.006)
        m = Matrix.Translation(GYRO) @ rot
        lathe(rp, [(R - bt, -bw), (R + bt, -bw), (R + bt + 0.01, 0.0), (R + bt, bw), (R - bt, bw), (R - bt - 0.008, 0.0), (R - bt, -bw)],
              seg=64, rot=m)
        for j in range(4):   # заклёпки-шарниры на кольце
            a = math.pi / 4 + j * math.pi / 2
            q = m @ Vector((math.cos(a) * (R + bt), math.sin(a) * (R + bt), 0))
            sphere(rp, q, 0.03, seg=10, rings=6)
    cp = P('Crystal', 'crystal')
    m = Matrix.Translation(GYRO)
    gird = [(0.15 * math.cos(2 * math.pi * i / 8), 0.15 * math.sin(2 * math.pi * i / 8)) for i in range(8)]
    bmc = cp.bm
    g = [bmc.verts.new(m @ Vector((x, y, 0.0))) for x, y in gird]
    cu = [bmc.verts.new(m @ Vector((x * 0.62, y * 0.62, 0.17))) for x, y in gird]
    cl = [bmc.verts.new(m @ Vector((x * 0.55, y * 0.55, -0.15))) for x, y in gird]
    top = bmc.verts.new(m @ Vector((0, 0, 0.36))); botv = bmc.verts.new(m @ Vector((0, 0, -0.3)))
    for i in range(8):
        j = (i + 1) % 8
        bmc.faces.new((g[i], g[j], cu[j], cu[i])); bmc.faces.new((cu[i], cu[j], top))
        bmc.faces.new((g[j], g[i], cl[i], cl[j])); bmc.faces.new((cl[j], cl[i], botv))

    # ---------------------------------------------------------------- кристаллы корпуса в оправах
    gm = P('Gems', 'gem')
    gb = P('GemBezels', 'silver', solid=0.01)
    def on_top(y, x):
        w, ht, hb = hull_at(y)
        th = math.acos(max(-1.0, min(1.0, (abs(x) / w) ** (1 / 0.85)))) if w > 0 else math.pi / 2
        q = Vector(sect(y, th, 0.05)); q.x = math.copysign(abs(q.x), x)
        n = (Vector(sect(y, th + 0.02, 0.05)) - Vector(sect(y, th - 0.02, 0.05))).cross(Vector((0, 1, 0)))
        n = n.normalized()
        if n.z < 0: n = -n
        n.x = math.copysign(abs(n.x), x)
        return q, n
    q, n = on_top(3.05, 0.0); gem(gm, gb, q, n, 0.55, 0.2, 0.08)
    for sx in (-1, 1):
        q, n = on_top(1.35, sx * 0.48); gem(gm, gb, q, n, 0.5, 0.22, 0.09)
        q, n = on_top(-2.35, sx * 0.5); gem(gm, gb, q, n, 0.42, 0.2, 0.08)
        q = Vector(sect(0.0, 0.0 if sx > 0 else math.pi, 0.04))
        gem(gm, gb, q, Vector((sx, 0, 0)), 0.6, 0.16, 0.07)   # бортовой «глаз» на остром краю
    q = Vector(sect(-0.4, -math.pi / 2, 0.05)); gem(gm, gb, q, Vector((0, 0, -1)), 0.6, 0.24, 0.09)

    # ---------------------------------------------------------------- двигатели
    nac = P('Nacelles', 'dark', bevel=0.01, mirror=True)
    x, z = NOZZLES[0]
    lathe(nac, [(0.0, -1.25), (0.12, -1.22), (0.2, -1.0), (0.22, -0.5), (0.21, 0.0), (0.24, 0.04), (0.24, 0.12), (0.19, 0.14), (0.17, 0.05)],
          seg=28, center=(x, -4.25, z), rot=Matrix.Rotation(math.pi / 2, 4, 'X'))
    nacc = P('NacelleBands', 'accent', bevel=0.004, mirror=True)
    for y in (-3.55, -3.95):
        cyl(nacc, (x, y, z), 0.225, 0.225, 0.06, seg=28)
    ng = P('NozzleGlow', 'engine', solid=0.008, mirror=True)
    lathe(ng, [(0.0, 0.1), (0.1, 0.07), (0.17, 0.03)], seg=28, center=(x, -4.25, z), rot=Matrix.Rotation(math.pi / 2, 4, 'X'))
    tail = P('Tail', 'silver', bevel=0.004)
    lathe(tail, [(0.0, -0.1), (0.13, 0.0), (0.09, 0.35), (0.03, 0.8), (0.0, 0.9)], seg=16, center=(0, -3.9, 0.02), rot=Matrix.Rotation(math.pi / 2, 4, 'X'))

    # ---------------------------------------------------------------- перистые лопасти (X-схема) и шпили
    bl = P('Blades', 'pearl', bevel=0.004, mirror=True)
    blade(bl, sect(-2.3, math.radians(35), -0.05), 40, 2.0, 1.9, 0.62, 0.045)
    blade(bl, sect(-2.9, math.radians(15), -0.05), 12, 1.55, 1.7, 0.5, 0.04)
    blade(bl, sect(-2.6, math.radians(-40), -0.05), -50, 1.5, 1.5, 0.5, 0.04)
    blade(bl, sect(-0.9, math.radians(8), -0.05), 6, 1.05, 1.1, 0.42, 0.03, curl=0.08)
    blade(bl, sect(1.2, math.radians(30), -0.03), 24, 0.7, 0.8, 0.32, 0.022, curl=0.05)
    sp = P('Spires', 'silver', mirror=True)
    rod(sp, sect(-0.4, 0.0, -0.05), (2.05, -0.55, -0.02), 0.014, seg=6)          # боковые шпили
    rod(sp, (x, -4.3, z + 0.12), (x + 0.12, -5.3, z + 0.16), 0.012, seg=6)       # кормовые
    sphere(sp, (2.07, -0.56, -0.02), 0.03, seg=8, rings=5)
    spc = P('SpiresC', 'silver')
    rod(spc, (0, -3.0, 0.3), (0, -3.25, 1.05), 0.012, seg=6)
    rod(spc, (0, -1.6, -0.32), (0, -1.9, -1.0), 0.012, seg=6)
    return K.Part.all


# ============================================================================ материалы

def build_material(name, spec):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    pos, nrm = geo.outputs['Position'], geo.outputs['Normal']
    base = spec['col']
    noise = nt.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 2.5; noise.inputs['Detail'].default_value = 5.0
    nt.links.new(pos, noise.inputs['Vector'])
    col = mix(nt, noise.outputs['Fac'], tuple(c * 0.92 for c in base), tuple(c * 1.06 for c in base))
    rough = spec['rough']
    height = None
    bev = nt.nodes.new('ShaderNodeBevel'); bev.samples = 8; bev.inputs['Radius'].default_value = 0.015

    if spec['kind'] == 'pearl':
        # перламутр: лёгкий перелив в холодный и тёплый оттенок + текучие гравированные линии вдоль корпуса
        hue = nt.nodes.new('ShaderNodeTexNoise'); hue.inputs['Scale'].default_value = 0.9; hue.inputs['Detail'].default_value = 2.0
        nt.links.new(pos, hue.inputs['Vector'])
        col = mix(nt, hue.outputs['Fac'], col, (0.74, 0.7, 0.8))
        mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (2.2, 0.35, 2.2)
        nt.links.new(pos, mp.inputs['Vector'])
        wv = nt.nodes.new('ShaderNodeTexWave'); wv.wave_type = 'BANDS'; wv.bands_direction = 'X'
        wv.inputs['Scale'].default_value = 1.6; wv.inputs['Distortion'].default_value = 3.0; wv.inputs['Detail'].default_value = 1.5
        nt.links.new(mp.outputs[0], wv.inputs['Vector'])
        d = node_math(nt, 'ABSOLUTE', node_math(nt, 'SUBTRACT', wv.outputs['Fac'], 0.5))
        line = node_math(nt, 'LESS_THAN', d, 0.018)
        col = mix(nt, node_math(nt, 'MULTIPLY', line, 0.55), col, (0.28, 0.25, 0.34))
        height = node_math(nt, 'SUBTRACT', 1.0, line)
    elif spec['kind'] == 'filigree':
        # филигрань: завитки (кольцевые волны с искажением), выпуклые светлые нити
        f = nt.nodes.new('ShaderNodeTexWave'); f.wave_type = 'RINGS'; f.rings_direction = 'SPHERICAL'
        f.inputs['Scale'].default_value = 3.2; f.inputs['Distortion'].default_value = 3.0
        f.inputs['Detail'].default_value = 1.0; f.inputs['Detail Scale'].default_value = 1.0
        nt.links.new(pos, f.inputs['Vector'])
        d = node_math(nt, 'ABSOLUTE', node_math(nt, 'SUBTRACT', f.outputs['Fac'], 0.5))
        line = node_math(nt, 'MULTIPLY', node_math(nt, 'SUBTRACT', 0.05, d), 30.0, clamp=True)
        dark = tuple(c * 0.45 for c in base)
        col = mix(nt, 1.0, dark, col)
        col = mix(nt, node_math(nt, 'MULTIPLY', line, 0.85), col, (0.82, 0.8, 0.86))
        rough = node_math(nt, 'MULTIPLY_ADD', line, -0.12, spec['rough'] + 0.08)
        height = line
    elif spec['kind'] == 'crystal':
        vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 9.0
        nt.links.new(pos, vor.inputs['Vector'])
        col = mix(nt, vor.outputs['Distance'], col, tuple(c * 1.6 for c in base))

    # мягко светлые кромки (полировка), без грязи — корабли Оракула ухожены
    dot = nt.nodes.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    nt.links.new(bev.outputs[0], dot.inputs[0]); nt.links.new(nrm, dot.inputs[1])
    edge = node_math(nt, 'MULTIPLY', node_math(nt, 'SUBTRACT', 1.0, dot.outputs['Value']), 10.0, clamp=True)
    if 'emit' not in spec:
        col = mix(nt, node_math(nt, 'MULTIPLY', edge, 0.35), col, tuple(min(1.0, c * 1.4 + 0.05) for c in base))

    nt.links.new(col, bsdf.inputs['Base Color'])
    bsdf.inputs['Metallic'].default_value = spec['metal']
    if isinstance(rough, float): bsdf.inputs['Roughness'].default_value = rough
    else: nt.links.new(rough, bsdf.inputs['Roughness'])
    if 'emit' in spec:
        if spec['kind'] == 'crystal':   # светится сильнее к центру граней
            fr = nt.nodes.new('ShaderNodeLayerWeight'); fr.inputs['Blend'].default_value = 0.4
            ec = mix(nt, fr.outputs['Facing'], spec['emit'], tuple(min(1.0, c * 0.45 + 0.4) for c in spec['emit']))
            nt.links.new(ec, bsdf.inputs['Emission Color'])
        else:
            bsdf.inputs['Emission Color'].default_value = spec['emit'] + (1.0,)
        bsdf.inputs['Emission Strength'].default_value = EMIT_STRENGTH
    if height is not None:
        bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.5; bump.inputs['Distance'].default_value = 0.004
        nt.links.new(height, bump.inputs['Height']); nt.links.new(bev.outputs[0], bump.inputs['Normal'])
        nt.links.new(bump.outputs[0], bsdf.inputs['Normal'])
    else:
        nt.links.new(bev.outputs[0], bsdf.inputs['Normal'])
    m['out'] = out.name
    m['bsdf'] = bsdf.name
    return m


# ============================================================================ экспорт / превью

def export(ob, maps, dst):
    from PIL import Image
    os.makedirs(dst, exist_ok=True)
    if not os.path.exists(dst + '.meta'):
        import uuid
        open(dst + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    piv = {k: tuple(GYRO) for k in ('ring0', 'ring1', 'ring2', 'crystal')}
    layers, center, length = K.extract_layers(ob, LAYER_OF, ORDER, piv)
    p = os.path.join(dst, 'iridia_science.bytes'); K.write_plm(p, layers); K.text_meta(p)

    ao = maps['ao'][..., 0]
    alb = maps['albedo'][..., :3] * (0.55 + 0.45 * ao[..., None])
    def save(arr, name, normal=False, srgb=True, q=93):
        path = os.path.join(dst, name)
        Image.fromarray(np.round(np.clip(arr, 0, 1) * 255).astype(np.uint8)).convert('RGB').save(path, quality=q, optimize=True)
        K.meta_for(path, normal, srgb)
    save(K.lin2srgb(alb), 'iridia_albedo.jpg')
    ms = np.zeros(ao.shape + (4,), np.float32)
    ms[..., 0] = maps['metal'][..., 0]; ms[..., 1] = ao; ms[..., 3] = 1.0 - maps['rough'][..., 0]
    img = Image.fromarray(np.round(np.clip(ms, 0, 1) * 255).astype(np.uint8), 'RGBA')
    if img.width > 1024: img = img.resize((1024, 1024), Image.LANCZOS)
    path = os.path.join(dst, 'iridia_ms.png'); img.save(path, optimize=True); K.meta_for(path, srgb=False)
    save(K.lin2srgb(maps['emit'][..., :3]), 'iridia_emission.jpg')
    save(maps['normal'][..., :3], 'iridia_normal.jpg', normal=True, srgb=False, q=96)

    u = lambda v: [float(round(c, 4)) for c in (np.array([v[0], v[2], v[1]]) - center) / length]
    noz = []
    for x, z in NOZZLES: noz += u((x, -4.38, z))
    cfg = dict(nozzles=noz, nozzleRadius=round(0.17 / length, 4), gyro=u(GYRO), lengthUnits=float(round(length, 3)))
    path = os.path.join(dst, 'iridia_science_config.json')
    open(path, 'w', encoding='utf-8', newline='\n').write(json.dumps(cfg, indent=1)); K.text_meta(path)
    print('  config', cfg)


def game_preview(ob, maps, size, accent=(0.55, 0.3, 0.85)):
    """Меш получает запечённые карты, как в игре; краска — цвет владельца (фиолетовый Оракула)."""
    def img(key, arr, noncolor):
        im = bpy.data.images.new('pv_' + key, size, size, alpha=True, float_buffer=True)
        im.colorspace_settings.name = 'Non-Color' if noncolor else 'Linear Rec.709'
        a = np.ones((size, size, 4), np.float32); a[..., :3] = arr[..., :3]
        im.pixels.foreach_set(a[::-1].reshape(-1)); return im
    ao = maps['ao'][..., :1]
    alb = img('alb', maps['albedo'][..., :3] * (0.55 + 0.45 * ao), False)
    mr = img('mr', np.concatenate([maps['metal'][..., :1], maps['rough'][..., :1], ao], 2), True)
    nm = img('nm', maps['normal'], True)
    em = img('em', maps['emit'], False)

    def gmat(name, tint=None, glow=3.0):
        m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = alb
        c = t.outputs[0]
        if tint: c = mix(nt, 1.0, c, tint, 'MULTIPLY')
        nt.links.new(c, b.inputs['Base Color'])
        t2 = nt.nodes.new('ShaderNodeTexImage'); t2.image = mr
        s = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(t2.outputs[0], s.inputs[0])
        nt.links.new(s.outputs[0], b.inputs['Metallic']); nt.links.new(s.outputs[1], b.inputs['Roughness'])
        t3 = nt.nodes.new('ShaderNodeTexImage'); t3.image = nm
        n = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t3.outputs[0], n.inputs['Color']); nt.links.new(n.outputs[0], b.inputs['Normal'])
        t4 = nt.nodes.new('ShaderNodeTexImage'); t4.image = em
        nt.links.new(t4.outputs[0], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = glow
        return m
    me = ob.data
    names = [m.name for m in me.materials]
    mi = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('material_index', mi)
    me.materials.clear()
    for m in (gmat('G_hull'), gmat('G_accent', accent), gmat('G_glow', glow=5.0)): me.materials.append(m)
    remap = [1 if n == 'accent' else 2 if n in ('crystal', 'gem') else 0 for n in names]
    me.polygons.foreach_set('material_index', np.array(remap, np.int32)[mi]); me.update()


def split_animated(ob):
    """Для GIF: кольца и кристалл — отдельными объектами с центром в гироскопе (как в игре)."""
    parts = {}
    for key in ('ring0', 'ring1', 'ring2', 'crystal'):
        idx = [i for i, m in enumerate(ob.data.materials) if m.name == key]
        if not idx: continue
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT')
        ob.active_material_index = idx[0]; bpy.ops.object.material_slot_select()
        bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
        new = [o for o in bpy.context.selected_objects if o is not ob][0]
        new.name = key
        bpy.context.scene.cursor.location = GYRO
        bpy.ops.object.select_all(action='DESELECT'); new.select_set(True); bpy.context.view_layer.objects.active = new
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
        parts[key] = new
    return parts


def gif(ob, outdir, frames=24):
    """Анимация как в IridiaScienceModel.cs: внешнее кольцо — вокруг вертикали, среднее — вокруг продольной оси
    внутри внешнего, внутреннее — вокруг поперечной внутри среднего; кристалл вращается и пульсирует."""
    from PIL import Image
    parts = split_animated(ob)
    for a, b in (('ring1', 'ring0'), ('ring2', 'ring1')):
        if a in parts and b in parts:
            parts[a].parent = parts[b]; parts[a].matrix_parent_inverse = parts[b].matrix_world.inverted()
    sc = bpy.context.scene
    os.makedirs(outdir, exist_ok=True)
    files = []
    for f in range(frames):
        t = f / frames * 6.0   # 6 секунд игрового времени на цикл
        parts['ring0'].rotation_euler = (0, 0, math.radians(60 * t))
        parts['ring1'].rotation_euler = (0, math.radians(-60 * t), 0)
        parts['ring2'].rotation_euler = (math.radians(120 * t), 0, 0)
        parts['crystal'].rotation_euler = (0, 0, math.radians(-60 * t))
        K.render_shots(outdir, samples=16, res=(800, 500), only=['three_quarter'])
        p = os.path.join(outdir, 'f%02d.png' % f); os.replace(os.path.join(outdir, 'three_quarter.png'), p); files.append(p)
    ims = [Image.open(p).convert('RGB') for p in files]
    ims[0].save(os.path.join(outdir, 'gyro.gif'), save_all=True, append_images=ims[1:], duration=1000 * 6 // frames, loop=0)
    for p in files: os.remove(p)


# ============================================================================

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(ROOT, 'Assets/Resources/Models/Ships/IridiaScience'))
    ap.add_argument('--blend', default=os.path.join(HERE, 'iridia_science.blend'))
    ap.add_argument('--tex', type=int, default=2048)
    ap.add_argument('--samples', type=int, default=16)
    ap.add_argument('--render', default='')
    ap.add_argument('--quick', default='')
    ap.add_argument('--gif', default='')
    a = ap.parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    global EMIT_STRENGTH
    if a.quick: MATS['accent']['col'] = (0.3, 0.12, 0.5); EMIT_STRENGTH = 4.0
    mats = {k: build_material(k, v) for k, v in MATS.items()}
    objs = K.make_objects(build(), mats)
    if a.blend and not a.quick:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.blend), compress=True)
    ob = K.join_all(objs)
    print('треугольников:', sum(len(p.vertices) - 2 for p in ob.data.polygons))
    if a.quick:
        K.render_shots(a.quick, samples=24, res=(1000, 625)); return
    K.unwrap(ob)
    t = time.time()
    maps = K.bake(ob, mats, a.tex, a.samples)
    print('запекание %.0fs' % (time.time() - t))
    export(ob, maps, a.out)
    if a.render or a.gif:
        game_preview(ob, maps, a.tex)
        if a.render: K.render_shots(a.render)
        if a.gif: gif(ob, a.gif)


if __name__ == '__main__':
    main()
