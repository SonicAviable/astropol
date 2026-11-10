# Научный корабль Доминиона Ксарн («Око Доминиона», класс РК) — модель целиком строится этим скриптом в Blender.
#
# Что делает:
#   1) процедурно собирает корабль из деталей (корпус-клин, слоёные бронеплиты, раздвоенный нос с сенсорным «оком»,
#      дорсальная тарелка дальнего сканирования на турели, крылья с сенсорными гондолами, киль, двигательный блок
#      с тремя соплами и радиаторами, рубка, трубы, вентиляция, антенны, огни);
#   2) сохраняет редактируемый .blend (детали отдельными объектами, модификаторы не применены);
#   3) сшивает всё в один меш, делает развёртку и запекает Cycles'ом PBR-атлас: альбедо (с AO и потёртыми кромками),
#      металл/AO/гладкость, нормали (швы панелей + скругление кромок), свечение (окна, сопла, сенсоры, огни);
#   4) пишет Assets/Resources/Models/Ships/XarnScience: xarn_science.bytes (PLM1 — читает XarnScienceModel.cs;
#      слои hull / accent / beacon), xarn_science_config.json (сопла), текстуры с .meta;
#   5) по желанию рендерит превью.
#
# Запуск (нужен модуль bpy: pip install bpy==5.2.3 numpy pillow; Python 3.13):
#   python xarn_science.py [--out ../../Assets/Resources/Models/Ships/XarnScience] [--blend xarn_science.blend]
#                          [--tex 2048] [--render папка] [--no-bake] [--samples 16]
#
# Оси Blender: нос +Y, верх +Z, правый борт +X. Unity: (x, y, z) = (x_b, z_b, y_b), обход треугольников наоборот.
import sys, os, math, json, struct, uuid, argparse, time
import bpy, bmesh
from mathutils import Vector, Matrix
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
COLOR_META = 'Assets/Resources/Planets/Real/earth_albedo.png.meta'
NORMAL_META = 'Assets/Resources/Planets/Real/earth_normal.png.meta'

# Материалы: (базовый цвет linear, металл, шероховатость, панели, слой экспорта)
MATS = {
    'hull':    dict(col=(0.105, 0.108, 0.115), metal=0.55, rough=0.42, panels=1.0),   # воронёная сталь корпуса
    'armor':   dict(col=(0.20, 0.205, 0.215), metal=0.5, rough=0.38, panels=1.0),     # навесные бронеплиты
    'accent':  dict(col=(0.62, 0.62, 0.62), metal=0.15, rough=0.5, panels=0.7),       # краска фракции (в игре × цвет владельца)
    'metal':   dict(col=(0.32, 0.31, 0.30), metal=0.95, rough=0.3, panels=0.0),       # механизмы, трубы, сопла
    'vent':    dict(col=(0.06, 0.06, 0.065), metal=0.8, rough=0.55, panels=0.0),      # решётки
    'dish':    dict(col=(0.5, 0.5, 0.51), metal=0.15, rough=0.45, panels=0.0),      # зеркало тарелки, обтекатели
    'bridge':  dict(col=(0.20, 0.205, 0.215), metal=0.5, rough=0.38, panels=1.0),     # рубка с окнами
    'hazard':  dict(col=(0.75, 0.55, 0.05), metal=0.2, rough=0.5, panels=0.0),        # предупреждающие полосы
    'eye':     dict(col=(0.25, 0.02, 0.01), metal=0.0, rough=0.2, panels=0.0, emit=(1.0, 0.16, 0.05)),
    'engine':  dict(col=(0.3, 0.1, 0.03), metal=0.0, rough=0.3, panels=0.0, emit=(1.0, 0.5, 0.22)),
    'nav_red':   dict(col=(0.3, 0.02, 0.02), metal=0.0, rough=0.2, panels=0.0, emit=(1.0, 0.08, 0.05)),
    'nav_green': dict(col=(0.02, 0.3, 0.05), metal=0.0, rough=0.2, panels=0.0, emit=(0.1, 1.0, 0.3)),
    'beacon':  dict(col=(0.9, 0.9, 0.9), metal=0.0, rough=0.2, panels=0.0, emit=(1.0, 0.95, 0.9)),
}
LAYER_OF = {'accent': 'accent', 'beacon': 'beacon'}   # остальное — «hull»

R90X = Matrix.Rotation(-math.pi / 2, 4, 'X')   # ось Z примитива -> +Y (вдоль корабля)


# ============================================================================ геометрия

class Part:
    """Деталь: bmesh + материал + модификаторы (bevel/solidify/mirror)."""
    all = []

    def __init__(self, name, mat, bevel=0.0, seg=1, mirror=False, solid=0.0, angle=35):
        self.name, self.mat, self.bevel, self.seg, self.mirror, self.solid, self.angle = name, mat, bevel, seg, mirror, solid, angle
        self.bm = bmesh.new()
        Part.all.append(self)


def P(name, mat, **kw):
    return Part(name, mat, **kw)


def box(p, center, size, rot=None):
    m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((size[0], size[1], size[2], 1))
    bmesh.ops.create_cube(p.bm, size=1.0, matrix=m)


def cyl(p, center, r1, r2, length, seg=16, axis='Y', rot=None):
    if axis == 'Y': base = R90X
    elif axis == 'X': base = Matrix.Rotation(math.pi / 2, 4, 'Y')
    else: base = Matrix.Identity(4)
    m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ base
    bmesh.ops.create_cone(p.bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=length, matrix=m)


def sphere(p, center, r, seg=16, rings=8, half=False):
    m = Matrix.Translation(Vector(center))
    res = bmesh.ops.create_uvsphere(p.bm, u_segments=seg, v_segments=rings, radius=r, matrix=m)
    if half:
        kill = [v for v in res['verts'] if v.co.z < center[2] - 1e-5]
        bmesh.ops.delete(p.bm, geom=kill, context='VERTS')
        edge = [e for e in p.bm.edges if e.is_boundary]
        if edge: bmesh.ops.holes_fill(p.bm, edges=edge, sides=0)


def rod(p, a, b, r, seg=6):
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot
    bmesh.ops.create_cone(p.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=m)


def loft(p, rings, closed=True, caps=True):
    """Лофт по сечениям (списки точек одинаковой длины)."""
    bm = p.bm
    vr = [[bm.verts.new(Vector(v)) for v in ring] for ring in rings]
    n = len(rings[0])
    span = n if closed else n - 1
    for i in range(len(vr) - 1):
        for j in range(span):
            k = (j + 1) % n
            bm.faces.new((vr[i][j], vr[i][k], vr[i + 1][k], vr[i + 1][j]))
    if closed and caps:
        bm.faces.new(vr[0][::-1]); bm.faces.new(vr[-1])
    return vr


def lathe(p, prof, seg=24, center=(0, 0, 0), rot=None, cap0=False, cap1=False):
    """Тело вращения вокруг локальной Z: prof = [(r, z)]; r=0 — полюс."""
    m = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4))
    rings = []
    for r, z in prof:
        if r < 1e-6: rings.append([p.bm.verts.new(m @ Vector((0, 0, z)))])
        else: rings.append([p.bm.verts.new(m @ Vector((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z))) for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1:
            for i in range(seg): p.bm.faces.new((a[0], b[i], b[(i + 1) % seg]))
        elif len(b) == 1:
            for i in range(seg): p.bm.faces.new((a[i], b[0], a[(i + 1) % seg]))
        else:
            for i in range(seg): p.bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    if cap0 and len(rings[0]) > 1: p.bm.faces.new(rings[0][::-1])
    if cap1 and len(rings[-1]) > 1: p.bm.faces.new(rings[-1])


# --- сечение корпуса: восьмигранник с плоским верхом и скосами
STATIONS = [  # y, полуширина, верх, низ
    (-3.70, 0.86, 0.56, 0.50),
    (-2.40, 0.96, 0.62, 0.55),
    (-0.60, 0.92, 0.60, 0.51),
    (1.40, 0.78, 0.52, 0.44),
    (2.80, 0.58, 0.40, 0.33),
    (3.55, 0.44, 0.28, 0.24),
]


def hp(y):
    s = STATIONS
    if y <= s[0][0]: return s[0][1:]
    for a, b in zip(s, s[1:]):
        if y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(a[i] + (b[i] - a[i]) * t for i in (1, 2, 3))
    return s[-1][1:]


def octa(w, ht, hb):
    return [(w * 0.56, ht), (w, ht * 0.38), (w, -hb * 0.35), (w * 0.6, -hb),
            (-w * 0.6, -hb), (-w, -hb * 0.35), (-w, ht * 0.38), (-w * 0.56, ht)]


def ring_at(y, d=0.0, idx=None):
    w, ht, hb = hp(y)
    pts = octa(w + d, ht + d, hb + d)
    if idx is not None: pts = [pts[i % 8] for i in idx]
    return [(x, y, z) for x, z in pts]


def plate(p, y0, y1, idx, d=0.035, bev=0.06):
    """Бронеплита поверх корпуса: полоса граней idx, концы со скосом (вниз к корпусу)."""
    rings = [ring_at(y0, d * 0.15, idx), ring_at(y0 + bev, d, idx), ring_at(y1 - bev, d, idx), ring_at(y1, d * 0.15, idx)]
    loft(p, rings, closed=False)


def build():
    Part.all.clear()
    # ---------------------------------------------------------------- корпус
    hull = P('Hull', 'hull', bevel=0.03, seg=2)
    ys = [-3.7, -3.0, -2.4, -1.5, -0.6, 0.4, 1.4, 2.1, 2.8, 3.2, 3.55]
    loft(hull, [ring_at(y) for y in ys])

    # бронеплиты: верх (со скосами), борта, низ — с зазорами
    top = P('ArmorTop', 'armor', bevel=0.012, solid=0.035)
    for y0, y1 in [(-3.45, -2.15), (-2.05, -0.1)]:
        plate(top, y0, y1, [7, 0])
    for y0, y1 in [(0.0, 1.25), (1.35, 2.75), (2.85, 3.5)]:
        plate(top, y0, y1, [6, 7, 0, 1])
    side = P('ArmorSide', 'armor', bevel=0.012, solid=0.035, mirror=True)
    for y0, y1 in [(-0.05, 0.75), (2.0, 2.9)]:
        plate(side, y0, y1, [1, 2])
    belly = P('ArmorBelly', 'armor', bevel=0.012, solid=0.035)
    for y0, y1 in [(-3.3, -1.0), (-0.9, 1.6), (1.7, 3.3)]:
        plate(belly, y0, y1, [2, 3, 4, 5])
    # краска фракции: полосы по бортам, продольная полоса по хребту, шевроны
    acc = P('AccentSide', 'accent', bevel=0.01, solid=0.03, mirror=True)
    for y0, y1 in [(0.85, 1.9), (-1.65, -0.15)]:
        plate(acc, y0, y1, [1, 2], d=0.05)
    accm = P('AccentSpine', 'accent', bevel=0.008, solid=0.025)
    for y0, y1 in [(-3.35, -2.25), (-2.05, -1.5), (-0.35, 0.8)]:
        rings = []
        for y in (y0, y0 + 0.05, y1 - 0.05, y1):
            w, ht, hb = hp(y)
            dz = 0.075 if y in (y0 + 0.05, y1 - 0.05) else 0.05
            rings.append([(-0.12, y, ht + dz), (0.12, y, ht + dz)])
        loft(accm, rings, closed=False)

    # ---------------------------------------------------------------- нос: вилка и сенсорное копьё
    prong = P('Prong', 'hull', bevel=0.02, seg=2, mirror=True)
    rings = []
    for t, y in [(0.0, 2.6), (0.35, 3.6), (0.7, 4.6), (0.92, 5.35), (1.0, 5.65)]:
        cx = 0.40 + 0.06 * math.sin(t * math.pi) - 0.1 * t * t
        w = 0.21 * (1 - t) + 0.025
        ht = 0.24 * (1 - t) + 0.03
        hb = 0.2 * (1 - t) + 0.03
        rings.append([(cx + x, y, z - 0.05 * t) for x, z in octa(w, ht, hb)])
    loft(prong, rings)
    pacc = P('ProngAccent', 'accent', bevel=0.008, solid=0.025, mirror=True)
    rings = []
    for t, y in [(0.08, 2.95), (0.15, 3.15), (0.5, 4.15), (0.6, 4.45)]:
        cx = 0.40 + 0.06 * math.sin(t * math.pi) - 0.1 * t * t
        w = 0.21 * (1 - t) + 0.025 + 0.03
        ht = 0.24 * (1 - t) + 0.03 + 0.03
        hb = 0.2 * (1 - t) + 0.03 + 0.03
        pts = octa(w, ht, hb)
        rings.append([(cx + pts[i][0], y, pts[i][1] - 0.05 * t) for i in (0, 1, 2)])
    loft(pacc, rings, closed=False)

    lance = P('Lance', 'metal', bevel=0.012)
    cyl(lance, (0, 3.9, 0.02), 0.17, 0.15, 0.9, seg=20)
    for y in (3.55, 3.85, 4.15):
        cyl(lance, (0, y, 0.02), 0.205, 0.205, 0.07, seg=20)
    cyl(lance, (0, 4.42, 0.02), 0.15, 0.25, 0.18, seg=24)      # раструб линзы
    cyl(lance, (0, 4.56, 0.02), 0.25, 0.25, 0.1, seg=24)
    eye = P('Eye', 'eye', solid=0.01)
    cyl(eye, (0, 3.70, 0.02), 0.18, 0.18, 0.08, seg=20)
    cyl(eye, (0, 4.00, 0.02), 0.18, 0.18, 0.08, seg=20)
    lathe(eye, [(0.0, 0.17), (0.08, 0.15), (0.16, 0.1), (0.215, 0.0)], seg=24, center=(0, 4.56, 0.02), rot=R90X)
    # подложка между зубцами вилки
    yoke = P('ProwBrace', 'hull', bevel=0.015)
    box(yoke, (0, 3.45, -0.17), (0.75, 0.5, 0.12))

    # ---------------------------------------------------------------- рубка
    br = P('Bridge', 'bridge', bevel=0.02, seg=2)
    rings = []
    for y, w, top, bot in [(0.95, 0.36, 0.86, 0.42), (1.15, 0.38, 0.9, 0.42), (2.0, 0.33, 0.88, 0.4), (2.4, 0.24, 0.66, 0.38), (2.55, 0.18, 0.5, 0.36)]:
        rings.append([(w * 0.7, y, top), (w, y, top - 0.12), (w, y, bot), (-w, y, bot), (-w, y, top - 0.12), (-w * 0.7, y, top)])
    loft(br, rings)
    bmet = P('BridgeGear', 'metal', bevel=0.008)
    box(bmet, (0, 1.45, 0.94), (0.3, 0.45, 0.08))
    rod(bmet, (0.12, 1.4, 0.95), (0.12, 1.4, 1.5), 0.016)
    rod(bmet, (-0.1, 1.7, 0.95), (-0.1, 1.7, 1.28), 0.014)
    rod(bmet, (0.12, 1.4, 1.3), (0.26, 1.4, 1.3), 0.01)
    sphere(bmet, (0.0, 1.95, 0.88), 0.09, seg=14, rings=7, half=True)
    bc1 = P('BeaconBridge', 'beacon')
    sphere(bc1, (0.12, 1.4, 1.52), 0.035, seg=8, rings=5)

    # ---------------------------------------------------------------- дорсальная тарелка на турели
    tur = P('Turret', 'metal', bevel=0.012)
    cyl(tur, (0, -0.95, 0.66), 0.46, 0.42, 0.16, seg=28, axis='Z')
    cyl(tur, (0, -0.95, 0.78), 0.32, 0.3, 0.12, seg=24, axis='Z')
    for sx in (-1, 1):
        box(tur, (sx * 0.27, -0.95, 0.96), (0.08, 0.26, 0.34))
    tacc = P('TurretRing', 'accent', bevel=0.006)
    cyl(tacc, (0, -0.95, 0.75), 0.435, 0.435, 0.04, seg=28, axis='Z')

    dish_c = Vector((0, -0.95, 1.12))
    drot = Matrix.Translation(dish_c) @ Matrix.Rotation(-math.radians(38), 4, 'X')
    R, D = 0.9, 0.3
    dish = P('Dish', 'dish', solid=0.035, bevel=0.0)
    prof = [(R * i / 10, D * (i / 10) ** 2) for i in range(11)]
    lathe(dish, prof, seg=32, rot=drot)
    rim = P('DishRim', 'armor', bevel=0.01)
    lathe(rim, [(R - 0.02, D - 0.02), (R + 0.07, D - 0.03), (R + 0.08, D + 0.03), (R - 0.01, D + 0.035), (R - 0.02, D - 0.02)], seg=32, rot=drot)
    # рёбра жёсткости сзади и ступица
    ribs = P('DishRibs', 'metal', bevel=0.006)
    for i in range(8):
        a = 2 * math.pi * i / 8 + math.pi / 8
        r0, r1 = 0.18, R - 0.05
        p0 = drot @ Vector((r0 * math.cos(a), r0 * math.sin(a), D * (r0 / R) ** 2 - 0.07))
        p1 = drot @ Vector((r1 * math.cos(a), r1 * math.sin(a), D * (r1 / R) ** 2 - 0.05))
        rod(ribs, p0, p1, 0.022, seg=4)
    lathe(ribs, [(0.0, -0.18), (0.16, -0.16), (0.22, -0.04), (0.2, 0.02)], seg=20, rot=drot)
    # облучатель: три штанги к фокусу
    focus = drot @ Vector((0, 0, 0.62))
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 2
        rod(ribs, drot @ Vector(((R - 0.03) * math.cos(a), (R - 0.03) * math.sin(a), D + 0.01)), focus, 0.016, seg=6)
    for rr in (0.36, 0.64):
        lathe(ribs, [(rr - 0.012, D * (rr / R) ** 2 + 0.004), (rr + 0.012, D * (rr / R) ** 2 + 0.006), (rr + 0.01, D * (rr / R) ** 2 + 0.03),
                     (rr - 0.01, D * (rr / R) ** 2 + 0.028), (rr - 0.012, D * (rr / R) ** 2 + 0.004)], seg=32, rot=drot)
    feed = P('Feed', 'metal', bevel=0.004)
    lathe(feed, [(0.0, 0.56), (0.07, 0.56), (0.075, 0.64), (0.05, 0.72), (0.0, 0.74)], seg=16, rot=drot)
    feye = P('FeedEye', 'eye', solid=0.01)
    lathe(feye, [(0.0, 0.5), (0.05, 0.53), (0.065, 0.56)], seg=16, rot=drot)

    # ---------------------------------------------------------------- крылья с сенсорными гондолами
    wing = P('Wing', 'hull', bevel=0.018, seg=2, mirror=True)
    WS = [(0.6, 0.35, -2.15, 0.16, 0.0), (1.35, -0.05, -1.75, 0.12, -0.05), (2.2, -0.4, -1.3, 0.08, -0.1)]

    def wsec(x):
        for a, b in zip(WS, WS[1:]):
            if x <= b[0]:
                t = (x - a[0]) / (b[0] - a[0])
                return tuple(a[i] + (b[i] - a[i]) * t for i in range(1, 5))
        return WS[-1][1:]
    rings = []
    for x, yf, yb, t, z in WS:
        c = yf - yb
        rings.append([(x, yb, z), (x, yb + 0.15 * c, z + t / 2), (x, yf - 0.25 * c, z + t / 2), (x, yf, z),
                      (x, yf - 0.25 * c, z - t / 2), (x, yb + 0.15 * c, z - t / 2)])
    loft(wing, rings)
    wacc = P('WingAccent', 'accent', bevel=0.006, solid=0.02, mirror=True)
    rings = []
    for x, e in [(1.5, 0.0), (1.56, 0.012), (2.04, 0.012), (2.1, 0.0)]:
        yf, yb, t, z = wsec(x)
        c = yf - yb
        rings.append([(x, yb + 0.15 * c + 0.06, z + t / 2 + 0.006 + e), (x, yf - 0.25 * c - 0.04, z + t / 2 + 0.006 + e)])
    loft(wacc, rings, closed=False)

    pod = P('Pod', 'armor', bevel=0.012, mirror=True)
    PX, PZ = 2.38, -0.1
    cyl(pod, (PX, -0.55, PZ), 0.22, 0.22, 1.9, seg=20)
    lathe(pod, [(0.22, 0.0), (0.2, 0.18), (0.12, 0.42), (0.04, 0.58), (0.0, 0.6)], seg=20, center=(PX, 0.4, PZ), rot=R90X)
    cyl(pod, (PX, -1.6, PZ), 0.17, 0.22, 0.2, seg=20)
    pmet = P('PodGear', 'metal', bevel=0.006, mirror=True)
    for y in (-1.25, -0.7, -0.15):
        cyl(pmet, (PX, y, PZ), 0.245, 0.245, 0.06, seg=20)
    cyl(pmet, (PX, -1.78, PZ), 0.12, 0.1, 0.12, seg=16)
    rod(pmet, (PX, 0.95, PZ), (PX, 1.55, PZ), 0.012)
    rod(pmet, (PX, -0.9, PZ + 0.2), (PX, -0.9, PZ + 0.48), 0.012)
    rod(pmet, (PX + 0.15, -1.2, PZ + 0.12), (PX + 0.33, -1.2, PZ + 0.3), 0.01)
    box(pmet, (PX, -0.42, PZ - 0.21), (0.08, 0.5, 0.06))
    pdome = P('PodDome', 'dish', mirror=True)
    sphere(pdome, (PX, -0.42, PZ + 0.19), 0.13, seg=16, rings=8, half=True)
    peye = P('PodEye', 'eye', mirror=True)
    cyl(peye, (PX, 0.22, PZ), 0.23, 0.23, 0.1, seg=20)
    navr = P('NavRed', 'nav_red')
    sphere(navr, (-PX, -1.42, PZ + 0.235), 0.04, seg=8, rings=5)
    navg = P('NavGreen', 'nav_green')
    sphere(navg, (PX, -1.42, PZ + 0.235), 0.04, seg=8, rings=5)

    # ---------------------------------------------------------------- киль
    keel = P('Keel', 'hull', bevel=0.015, seg=2)
    rings = []
    for z, yf, yb, t in [(-0.35, -0.5, -3.3, 0.16), (-0.75, -1.7, -3.45, 0.11), (-1.0, -2.55, -3.5, 0.07)]:
        c = yf - yb
        rings.append([(0, yb, z), (t / 2, yb + 0.12 * c, z), (t / 2, yf - 0.3 * c, z), (0, yf, z), (-t / 2, yf - 0.3 * c, z), (-t / 2, yb + 0.12 * c, z)])
    loft(keel, rings)
    kacc = P('KeelAccent', 'accent', bevel=0.005, solid=0.015, mirror=True)
    rings = []
    for z, e in [(-0.55, 0.0), (-0.58, 0.01), (-0.72, 0.01), (-0.75, 0.0)]:
        t = 0.16 + (0.11 - 0.16) * (-0.35 - z) / 0.4
        rings.append([(t / 2 + 0.006 + e, -3.0, z), (t / 2 + 0.006 + e, -2.3, z)])
    loft(kacc, rings, closed=False)

    # ---------------------------------------------------------------- двигательный блок
    eng = P('EngineBlock', 'armor', bevel=0.025, seg=2)
    rings = []
    for y, w, ht, hb in [(-3.3, 0.94, 0.6, 0.53), (-3.5, 1.1, 0.72, 0.62), (-4.35, 1.1, 0.72, 0.62), (-4.5, 1.0, 0.64, 0.55)]:
        rings.append([(x, y, z) for x, z in octa(w, ht, hb)])
    loft(eng, rings)
    eacc = P('EngineBand', 'accent', bevel=0.008, solid=0.03)
    rings = []
    for y, d in [(-3.65, 0.0), (-3.7, 0.025), (-3.95, 0.025), (-4.0, 0.0)]:
        rings.append([(x, y, z) for x, z in [octa(1.1 + 0.015 + d, 0.72 + 0.015 + d, 0.62 + 0.015 + d)[i] for i in (5, 6, 7, 0, 1, 2)]])
    loft(eacc, rings, closed=False)
    noz = P('Nozzles', 'metal', bevel=0.0, solid=0.03)
    nglow = P('NozzleGlow', 'engine', solid=0.01)
    NOZ = [(0.0, -0.02, 0.36), (0.62, -0.16, 0.25), (-0.62, -0.16, 0.25)]
    for x, z, r in NOZ:
        rot = Matrix.Rotation(math.pi / 2, 4, 'X')   # локальная +Z -> −Y (назад)
        lathe(noz, [(r * 0.72, -0.05), (r * 0.82, 0.04), (r * 0.93, 0.2), (r * 1.08, 0.42), (r * 1.16, 0.52)], seg=24, center=(x, -4.5, z), rot=rot)
        lathe(nglow, [(0.0, 0.16), (r * 0.5, 0.12), (r * 0.8, 0.06)], seg=24, center=(x, -4.5, z), rot=rot)
    ncol = P('NozzleCollar', 'metal', bevel=0.008)
    for x, z, r in NOZ:
        cyl(ncol, (x, -4.52, z), r * 0.98, r * 0.98, 0.07, seg=24)
    # радиаторы: пакеты рёбер по верхним углам
    # радиаторы: пакеты рёбер на верхних скосах двигательного блока, рёбра стоят перпендикулярно скосу
    rad = P('Radiators', 'metal', bevel=0.004, mirror=True)
    c0 = Vector(octa(1.1, 0.72, 0.62)[0]); c1 = Vector(octa(1.1, 0.72, 0.62)[1])
    u = (c1 - c0).normalized(); v = Vector((-u.y, u.x))
    if v.y < 0: v = -v

    def sk(a, h):   # точка на скосе: a — вдоль, h — по нормали
        q = c0 + u * a + v * h
        return q.x, q.y
    bm = rad.bm
    for i in range(7):
        y = -3.58 - i * 0.12
        quad = [sk(0.08, 0.02), sk(0.6, 0.02), sk(0.55, 0.3), sk(0.13, 0.36)]
        f = [bm.verts.new((x, y - 0.018, z)) for x, z in quad]
        b = [bm.verts.new((x, y + 0.018, z)) for x, z in quad]
        bm.faces.new(f); bm.faces.new(b[::-1])
        for j in range(4):
            k = (j + 1) % 4
            bm.faces.new((f[j], b[j], b[k], f[k]))
    base = [sk(0.04, -0.01), sk(0.64, -0.01), sk(0.64, 0.035), sk(0.04, 0.035)]
    loft(rad, [[(x, y, z) for x, z in base] for y in (-3.5, -4.34)])
    # RCS-блоки
    rcs = P('RCS', 'metal', bevel=0.01, mirror=True)
    for y, z in [(-3.42, 0.42), (-3.42, -0.42)]:
        box(rcs, (1.13, y, z), (0.12, 0.18, 0.14))
        cyl(rcs, (1.2, y, z), 0.035, 0.05, 0.08, seg=8, axis='X')
    for y, z in [(2.3, 0.12), (2.3, -0.12)]:
        box(rcs, (0.73, y, z), (0.1, 0.16, 0.1))
        cyl(rcs, (0.79, y, z), 0.03, 0.04, 0.07, seg=8, axis='X')
    haz = P('Hazard', 'hazard', bevel=0.004, mirror=True)
    box(haz, (1.105, -4.2, -0.05), (0.03, 0.24, 0.3))
    eant = P('EngineMast', 'metal', bevel=0.004)
    rod(eant, (0.0, -3.6, 0.72), (0.0, -3.6, 1.15), 0.016)
    rod(eant, (0.0, -3.6, 1.0), (0.0, -3.4, 1.0), 0.01)
    bc2 = P('BeaconEngine', 'beacon')
    sphere(bc2, (0.0, -3.6, 1.17), 0.035, seg=8, rings=5)

    # ---------------------------------------------------------------- трубы, вентиляция, купола
    # трубы по верхним скосам (там, где сверху только плиты хребта)
    pipes = P('Pipes', 'metal', bevel=0.0, mirror=True)
    for a, r in [(0.3, 0.032), (0.55, 0.026)]:
        y0, y1 = -3.3, -0.4
        w, ht, hb = hp(-2.0)
        c0 = Vector(octa(w, ht, hb)[0]); c1 = Vector(octa(w, ht, hb)[1])
        u = (c1 - c0); n = Vector((-u.y, u.x)).normalized()
        if n.y < 0: n = -n
        q = c0 + u * a + n * (r + 0.004)
        rod(pipes, (q.x, y0, q.y), (q.x, y1, q.y), r, seg=8)
        for y in np.linspace(y0 + 0.15, y1 - 0.15, 5):
            qq = c0 + u * a + n * 0.02
            box(pipes, (qq.x, float(y), qq.y), (0.085, 0.045, 0.085), rot=Matrix.Rotation(math.atan2(u.y, u.x), 4, 'Y').inverted())
    vent = P('Vents', 'vent', bevel=0.006, mirror=True)
    for y0, y1 in [(-3.25, -2.8), (-2.7, -2.25)]:
        w, ht, hb = hp((y0 + y1) / 2)
        box(vent, (w + 0.015, (y0 + y1) / 2, -0.1), (0.05, y1 - y0, 0.14))
    domes = P('Domes', 'dish')
    sphere(domes, (0, 0.9, -0.5), 0.13, seg=16, rings=8)
    sphere(domes, (0, 2.1, -0.42), 0.1, seg=16, rings=8)
    # точечная оборона: двуствольная турель на носу — Доминион не летает безоружным даже в науке
    pd = P('PDTurret', 'metal', bevel=0.008)
    cyl(pd, (0, 3.0, 0.335), 0.14, 0.12, 0.06, seg=20, axis='Z')
    box(pd, (0, 3.0, 0.41), (0.16, 0.2, 0.1))
    for sx in (-0.04, 0.04):
        rod(pd, (sx, 3.05, 0.42), (sx, 3.42, 0.42), 0.014, seg=8)
    pant = P('ProngGear', 'metal', bevel=0.004, mirror=True)
    rod(pant, (0.585, 3.55, -0.02), (0.56, 4.75, -0.06), 0.011, seg=6)
    for y in (3.65, 4.2):
        box(pant, (0.57, y, -0.03), (0.05, 0.08, 0.05))
    fin = P('DorsalFin', 'hull', bevel=0.012, seg=2, mirror=True)
    rings = []
    for z, x, yf, yb, t in [(0.66, 0.34, -3.4, -4.4, 0.1), (0.95, 0.42, -3.85, -4.48, 0.07), (1.22, 0.5, -4.25, -4.56, 0.035)]:
        c = yf - yb
        rings.append([(x, yb, z), (x + t / 2, yb + 0.1 * c, z), (x + t / 2, yf - 0.35 * c, z), (x, yf, z), (x - t / 2, yf - 0.35 * c, z), (x - t / 2, yb + 0.1 * c, z)])
    loft(fin, rings)
    facc = P('FinAccent', 'accent', bevel=0.004, solid=0.012, mirror=True)
    rings = []
    for z, e in [(0.9, 0.0), (0.93, 0.008), (1.08, 0.008), (1.11, 0.0)]:
        k = (z - 0.95) / 0.27
        x = 0.42 + 0.08 * k; t = 0.07 - 0.035 * k
        rings.append([(x + t / 2 + 0.004 + e, -4.4, z), (x + t / 2 + 0.004 + e, -4.0 + 0.3 * k, z)])
    loft(facc, rings, closed=False)
    hatch = P('Hatches', 'metal', bevel=0.006)
    box(hatch, (0, 0.25, -0.53), (0.42, 0.5, 0.03))
    return Part.all


# ============================================================================ объекты Blender

def make_objects(parts, mats):
    col = bpy.context.scene.collection
    objs = []
    for p in parts:
        bmesh.ops.remove_doubles(p.bm, verts=p.bm.verts, dist=1e-5)
        if p.solid == 0:
            bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces)
        me = bpy.data.meshes.new(p.name)
        p.bm.to_mesh(me); p.bm.free()
        ob = bpy.data.objects.new(p.name, me)
        col.objects.link(ob)
        me.materials.append(mats[p.mat])
        if p.mirror:
            m = ob.modifiers.new('Mirror', 'MIRROR'); m.use_axis = (True, False, False); m.use_clip = False
        if p.solid:
            m = ob.modifiers.new('Solidify', 'SOLIDIFY'); m.thickness = p.solid; m.offset = 0.0; m.use_even_offset = True
        if p.bevel:
            m = ob.modifiers.new('Bevel', 'BEVEL'); m.width = p.bevel; m.segments = p.seg
            m.limit_method = 'ANGLE'; m.angle_limit = math.radians(p.angle); m.use_clamp_overlap = True
        objs.append(ob)
    return objs


def join_all(objs):
    for o in bpy.context.scene.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = 'XarnScienceShip'
    me = ob.data
    for f in me.polygons: f.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(32))
    m = ob.modifiers.new('WN', 'WEIGHTED_NORMAL'); m.keep_sharp = True; m.weight = 50
    bpy.ops.object.modifier_apply(modifier='WN')
    return ob


def unwrap(ob):
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0015, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.0025, rotate=True, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')


# ============================================================================ материалы (процедурные — для запекания)

class Mat:
    pass


def node_math(nt, op, a, b=None, c=None, clamp=False):
    n = nt.nodes.new('ShaderNodeMath'); n.operation = op; n.use_clamp = clamp
    for i, v in enumerate((a, b, c)):
        if v is None: continue
        if isinstance(v, (int, float)): n.inputs[i].default_value = v
        else: nt.links.new(v, n.inputs[i])
    return n.outputs[0]


def mix(nt, fac, a, b, blend='MIX'):
    n = nt.nodes.new('ShaderNodeMix'); n.data_type = 'RGBA'; n.blend_type = blend
    ins = {i.identifier: i for i in n.inputs}
    for sock, v in ((ins['Factor_Float'], fac), (ins['A_Color'], a), (ins['B_Color'], b)):
        if isinstance(v, (int, float)): sock.default_value = v
        elif isinstance(v, tuple): sock.default_value = v if len(v) == 4 else v + (1.0,)
        else: nt.links.new(v, sock)
    return {o.identifier: o for o in n.outputs}['Result_Color']


def triplanar_brick(nt, pos, nrm, scale, w, h, mortar, seed=0.0):
    """Панели: кирпичная текстура в трёх проекциях, выбор по доминирующей оси нормали. -> (шов, тон панели)."""
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(pos, sep.inputs[0])
    sn = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(nrm, sn.inputs[0])
    ax = [node_math(nt, 'ABSOLUTE', sn.outputs[i]) for i in range(3)]
    outs = []
    for (u, v) in ((1, 2), (0, 2), (1, 0)):   # X-грани: (y,z); Y-грани: (x,z); Z-грани: (y,x) — панели вдоль корабля
        c = nt.nodes.new('ShaderNodeCombineXYZ')
        nt.links.new(sep.outputs[u], c.inputs[0]); nt.links.new(sep.outputs[v], c.inputs[1])
        c.inputs[2].default_value = seed
        b = nt.nodes.new('ShaderNodeTexBrick')
        b.offset = 0.5; b.squash = 1.0
        b.inputs['Scale'].default_value = scale
        b.inputs['Mortar Size'].default_value = mortar
        b.inputs['Mortar Smooth'].default_value = 0.15
        b.inputs['Brick Width'].default_value = w
        b.inputs['Row Height'].default_value = h
        b.inputs['Bias'].default_value = 0.0
        b.inputs['Color1'].default_value = (0, 0, 0, 1); b.inputs['Color2'].default_value = (1, 1, 1, 1)
        b.inputs['Mortar'].default_value = (1, 1, 1, 1)
        nt.links.new(c.outputs[0], b.inputs['Vector'])
        outs.append(b)
    # доминирующая ось
    isx = node_math(nt, 'MULTIPLY', node_math(nt, 'GREATER_THAN', ax[0], ax[1]), node_math(nt, 'GREATER_THAN', ax[0], ax[2]))
    isy = node_math(nt, 'MULTIPLY', node_math(nt, 'SUBTRACT', 1.0, isx), node_math(nt, 'GREATER_THAN', ax[1], ax[2]))
    seam = mix(nt, isy, mix(nt, isx, outs[2].outputs['Fac'], outs[0].outputs['Fac']), outs[1].outputs['Fac'])
    tone = mix(nt, isy, mix(nt, isx, outs[2].outputs['Color'], outs[0].outputs['Color']), outs[1].outputs['Color'])
    return seam, tone


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
    noise = nt.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value = 3.0; noise.inputs['Detail'].default_value = 6.0
    nt.links.new(pos, noise.inputs['Vector'])
    fine = nt.nodes.new('ShaderNodeTexNoise'); fine.inputs['Scale'].default_value = 40.0; fine.inputs['Detail'].default_value = 4.0
    nt.links.new(pos, fine.inputs['Vector'])

    # кромки: разница между скруглённой (bevel) и настоящей нормалью -> потёртость
    bev = nt.nodes.new('ShaderNodeBevel'); bev.samples = 8; bev.inputs['Radius'].default_value = 0.02
    dot = nt.nodes.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
    nt.links.new(bev.outputs[0], dot.inputs[0]); nt.links.new(nrm, dot.inputs[1])
    edge = node_math(nt, 'MULTIPLY', node_math(nt, 'SUBTRACT', 1.0, dot.outputs['Value']), 14.0, clamp=True)
    edge = node_math(nt, 'MULTIPLY', edge, node_math(nt, 'MULTIPLY_ADD', fine.outputs['Fac'], 1.6, -0.3), clamp=True)

    col = mix(nt, noise.outputs['Fac'], tuple(c * 0.85 for c in base), tuple(c * 1.12 for c in base))
    rough = node_math(nt, 'MULTIPLY_ADD', fine.outputs['Fac'], 0.16, spec['rough'] - 0.08)
    height = None
    if spec['panels'] > 0:
        seam, tone = triplanar_brick(nt, pos, nrm, 1.0, 0.62, 0.26, 0.012)
        seam2, tone2 = triplanar_brick(nt, pos, nrm, 1.0, 1.9, 0.78, 0.016, seed=0.37)
        t = node_math(nt, 'MULTIPLY_ADD', tone, 0.22 * spec['panels'], 1.0 - 0.11 * spec['panels'])
        t2 = node_math(nt, 'MULTIPLY_ADD', tone2, 0.12 * spec['panels'], 1.0 - 0.06 * spec['panels'])
        col = mix(nt, 1.0, col, t, 'MULTIPLY'); col = mix(nt, 1.0, col, t2, 'MULTIPLY')
        anyseam = node_math(nt, 'MAXIMUM', seam, seam2)
        col = mix(nt, node_math(nt, 'MULTIPLY', anyseam, 0.75), col, (0.01, 0.01, 0.012))
        rough = node_math(nt, 'MULTIPLY_ADD', tone, 0.12, rough)
        height = node_math(nt, 'SUBTRACT', 1.0, anyseam)

    if name == 'vent':   # решётка: частые прорези вдоль корабля
        wave = nt.nodes.new('ShaderNodeTexWave'); wave.wave_type = 'BANDS'; wave.bands_direction = 'Z'
        wave.inputs['Scale'].default_value = 14.0; wave.inputs['Distortion'].default_value = 0.0
        nt.links.new(pos, wave.inputs['Vector'])
        sl = node_math(nt, 'GREATER_THAN', wave.outputs['Fac'], 0.55)
        col = mix(nt, sl, col, (0.004, 0.004, 0.004))
        height = node_math(nt, 'SUBTRACT', 1.0, sl)
    if name == 'hazard':   # жёлто-чёрные диагональные полосы
        wave = nt.nodes.new('ShaderNodeTexWave'); wave.wave_type = 'BANDS'; wave.bands_direction = 'DIAGONAL'
        wave.inputs['Scale'].default_value = 6.0; wave.inputs['Distortion'].default_value = 0.0
        nt.links.new(pos, wave.inputs['Vector'])
        col = mix(nt, node_math(nt, 'GREATER_THAN', wave.outputs['Fac'], 0.5), col, (0.01, 0.01, 0.01))

    # потёртые светлые кромки (на краске — до металла)
    worn = (0.42, 0.41, 0.4) if name in ('accent', 'hazard') else tuple(min(1.0, c * 2.6 + 0.04) for c in base)
    if 'emit' not in spec and name != 'beacon':
        col = mix(nt, node_math(nt, 'MULTIPLY', edge, 0.7), col, worn)

    # грязь в углублениях — тянется вниз пятнами
    if spec['panels'] > 0 or name in ('metal', 'armor'):
        streak = nt.nodes.new('ShaderNodeTexNoise'); streak.inputs['Scale'].default_value = 5.0; streak.inputs['Detail'].default_value = 8.0
        mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1.0, 8.0, 0.4)
        nt.links.new(pos, mp.inputs['Vector']); nt.links.new(mp.outputs[0], streak.inputs['Vector'])
        dirt = node_math(nt, 'MULTIPLY', node_math(nt, 'SUBTRACT', streak.outputs['Fac'], 0.55), 2.2, clamp=True)
        col = mix(nt, node_math(nt, 'MULTIPLY', dirt, 0.35), col, (0.02, 0.018, 0.016))

    emit = None
    if 'emit' in spec:
        emit = spec['emit']
    if name in ('hull', 'armor', 'bridge'):   # освещённые иллюминаторы: ряды окон на бортах / полоса на рубке
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(pos, sep.inputs[0])
        sn = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(nrm, sn.inputs[0])
        wb = nt.nodes.new('ShaderNodeTexBrick'); wb.offset = 0.0
        side_win = name != 'bridge'
        wb.inputs['Scale'].default_value = 1.0; wb.inputs['Mortar Size'].default_value = 0.012 if side_win else 0.02
        wb.inputs['Brick Width'].default_value = 0.075 if side_win else 0.1; wb.inputs['Row Height'].default_value = 0.03 if side_win else 0.06
        wb.inputs['Color1'].default_value = (0, 0, 0, 1); wb.inputs['Color2'].default_value = (1, 1, 1, 1)
        wb.inputs['Bias'].default_value = 0.0
        cv = nt.nodes.new('ShaderNodeCombineXYZ')
        if side_win:   # два ряда иллюминаторов на вертикальных бортах корпуса (не на гондолах и двигателе)
            nt.links.new(sep.outputs[1], cv.inputs[0]); nt.links.new(sep.outputs[2], cv.inputs[1])
            zb = node_math(nt, 'MULTIPLY', node_math(nt, 'GREATER_THAN', sep.outputs[2], 0.07), node_math(nt, 'LESS_THAN', sep.outputs[2], 0.1))
            zb2 = node_math(nt, 'MULTIPLY', node_math(nt, 'GREATER_THAN', sep.outputs[2], 0.13), node_math(nt, 'LESS_THAN', sep.outputs[2], 0.16))
            band = node_math(nt, 'MAXIMUM', zb, zb2)
            side = node_math(nt, 'GREATER_THAN', node_math(nt, 'ABSOLUTE', sn.outputs[0]), 0.85)
            ys = node_math(nt, 'MULTIPLY', node_math(nt, 'GREATER_THAN', sep.outputs[1], -3.3), node_math(nt, 'LESS_THAN', sep.outputs[1], 3.0))
            xs = node_math(nt, 'LESS_THAN', node_math(nt, 'ABSOLUTE', sep.outputs[0]), 1.15)
            onoff = node_math(nt, 'GREATER_THAN', wb.outputs['Color'], 0.35)
            mask = node_math(nt, 'MULTIPLY', node_math(nt, 'MULTIPLY', band, side), node_math(nt, 'MULTIPLY', node_math(nt, 'MULTIPLY', ys, xs), onoff))
        else:
            nt.links.new(sep.outputs[1], cv.inputs[0]); nt.links.new(sep.outputs[0], cv.inputs[1])
            cv2 = nt.nodes.new('ShaderNodeCombineXYZ')
            nt.links.new(sep.outputs[0], cv2.inputs[0]); nt.links.new(sep.outputs[2], cv2.inputs[1])
            band = node_math(nt, 'MULTIPLY', node_math(nt, 'GREATER_THAN', sep.outputs[2], 0.66), node_math(nt, 'LESS_THAN', sep.outputs[2], 0.74))
            notop = node_math(nt, 'LESS_THAN', sn.outputs[2], 0.6)
            onoff = node_math(nt, 'GREATER_THAN', wb.outputs['Color'], 0.15)
            mask = node_math(nt, 'MULTIPLY', node_math(nt, 'MULTIPLY', band, notop), onoff)
            nt.links.new(cv2.outputs[0], wb.inputs['Vector'])
        if side_win: nt.links.new(cv.outputs[0], wb.inputs['Vector'])
        win = node_math(nt, 'MULTIPLY', mask, node_math(nt, 'SUBTRACT', 1.0, wb.outputs['Fac']))
        emit_col = mix(nt, win, (0, 0, 0), (1.0, 0.62, 0.3))
        col = mix(nt, win, col, (0.02, 0.02, 0.02))
        e = Mat(); e.sock = emit_col
        emit = e

    nt.links.new(col, bsdf.inputs['Base Color'])
    bsdf.inputs['Metallic'].default_value = spec['metal']
    if isinstance(rough, float): bsdf.inputs['Roughness'].default_value = rough
    else: nt.links.new(rough, bsdf.inputs['Roughness'])
    if isinstance(emit, tuple):
        bsdf.inputs['Emission Color'].default_value = emit + (1.0,); bsdf.inputs['Emission Strength'].default_value = 1.0
    elif emit is not None:
        nt.links.new(emit.sock, bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 1.0

    # нормали: скруглённые кромки + вдавленные швы
    if height is not None:
        bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = 0.6; bump.inputs['Distance'].default_value = 0.004
        nt.links.new(height, bump.inputs['Height']); nt.links.new(bev.outputs[0], bump.inputs['Normal'])
        nt.links.new(bump.outputs[0], bsdf.inputs['Normal'])
    else:
        nt.links.new(bev.outputs[0], bsdf.inputs['Normal'])
    m['out'] = out.name
    m['bsdf'] = bsdf.name
    return m


# ============================================================================ запекание

def bake(ob, mats, size, samples):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = samples
    sc.render.bake.margin = 6
    sc.render.bake.margin_type = 'EXTEND'
    imgs = {}

    def target(key, noncolor):
        img = bpy.data.images.new('bake_' + key, size, size, alpha=False, float_buffer=True)
        img.colorspace_settings.name = 'Non-Color' if noncolor else 'Linear Rec.709'
        for m in mats.values():
            nt = m.node_tree
            n = nt.nodes.get('BAKE') or nt.nodes.new('ShaderNodeTexImage')
            n.name = 'BAKE'; n.image = img
            nt.nodes.active = n
        imgs[key] = img
        return img

    for o in bpy.context.scene.objects: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob

    def run(key, typ, noncolor=False, **kw):
        t = time.time()
        target(key, noncolor)
        bpy.ops.object.bake(type=typ, **kw)
        print('  bake', key, '%.0fs' % (time.time() - t), flush=True)

    run('albedo', 'DIFFUSE', pass_filter={'COLOR'})
    run('rough', 'ROUGHNESS', noncolor=True)
    run('emit', 'EMIT')
    run('normal', 'NORMAL', noncolor=True, normal_space='TANGENT')
    # металл: временно выводим значение Metallic через Emission
    for m in mats.values():
        nt = m.node_tree; b = nt.nodes[m['bsdf']]; o = nt.nodes[m['out']]
        em = nt.nodes.new('ShaderNodeEmission'); em.name = 'TMP'
        em.inputs['Color'].default_value = (b.inputs['Metallic'].default_value,) * 3 + (1.0,)
        nt.links.new(em.outputs[0], o.inputs['Surface'])
    run('metal', 'EMIT', noncolor=True)
    for m in mats.values():
        nt = m.node_tree
        nt.links.new(nt.nodes[m['bsdf']].outputs[0], nt.nodes[m['out']].inputs['Surface'])
        nt.nodes.remove(nt.nodes['TMP'])
    sc.cycles.samples = max(64, samples * 4)
    sc.world = sc.world or bpy.data.worlds.new('W')
    run('ao', 'AO', noncolor=True)
    sc.cycles.samples = samples

    def arr(key):
        a = np.empty(size * size * 4, np.float32)
        imgs[key].pixels.foreach_get(a)
        return a.reshape(size, size, 4)[::-1]   # верхняя строка картинки — v = 1
    return {k: arr(k) for k in imgs}


def lin2srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


# ============================================================================ экспорт

def meta_for(path, normal=False, srgb=True):
    if os.path.exists(path + '.meta'): return
    tmpl = open(os.path.join(ROOT, NORMAL_META if normal else COLOR_META)).read()
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


def extract_layers(ob):
    """Меш -> слои {имя: (pos, nrm, uv, idx)} в осях Unity, вершины расщеплены по нормали/UV."""
    me = ob.data
    me.calc_loop_triangles()
    co = np.array([v.co[:] for v in me.vertices], np.float64)
    cn = np.empty(len(me.loops) * 3, np.float32); me.corner_normals.foreach_get('vector', cn); cn = cn.reshape(-1, 3)
    uvl = me.uv_layers.active.data
    uv = np.empty(len(me.loops) * 2, np.float32); uvl.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    tl = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get('loops', tl); tl = tl.reshape(-1, 3)
    tm = np.empty(len(me.loop_triangles), np.int32); me.loop_triangles.foreach_get('material_index', tm)
    names = [m.name for m in me.materials]
    layer_of_tri = np.array([LAYER_OF.get(names[i], 'hull') for i in tm])

    # Blender (x, y, z) -> Unity (x, z, y); длина по Z -> 1, центр габарита — в нуле
    P = co[:, [0, 2, 1]]
    lo, hi = P.min(0), P.max(0)
    center = (lo + hi) / 2; length = hi[2] - lo[2]
    P = (P - center) / length
    N = cn[:, [0, 2, 1]]
    layers = {}
    for name in ('hull', 'accent', 'beacon'):
        loops = tl[layer_of_tri == name]
        if len(loops) == 0: continue
        flat = loops.reshape(-1)
        key = np.concatenate([lv[flat][:, None].astype(np.float64), np.round(cn[flat] * 1000), np.round(uv[flat] * 100000)], 1)
        uniq, inv = np.unique(key, axis=0, return_inverse=True)
        first = np.zeros(len(uniq), np.int64); first[inv[::-1]] = np.arange(len(flat))[::-1]
        src = flat[first]
        pos = P[lv[src]].astype(np.float32)
        nrm = N[src]; nrm = nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
        idx = inv.reshape(-1, 3)[:, [0, 2, 1]]   # левая СК Unity: обход наоборот
        # проверка: геометрическая нормаль cross(b−a, c−a) должна смотреть вдоль нормали вершин
        a, b, c = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
        gn = np.cross(b - a, c - a)
        ok = (np.einsum('ij,ij->i', gn, nrm[idx[:, 0]] + nrm[idx[:, 1]] + nrm[idx[:, 2]]) > 0).mean()
        print('  слой %-6s вершин %6d треугольников %6d, нормали согласованы: %.1f%%' % (name, len(pos), len(idx), ok * 100))
        layers[name] = (pos, nrm.astype(np.float32), uv[src].astype(np.float32), idx.astype(np.uint32))
    return layers, center, length


def write_plm(path, layers):
    with open(path, 'wb') as f:
        f.write(b'PLM1'); f.write(struct.pack('<i', len(layers)))
        for name, (pos, nrm, uv, idx) in layers.items():
            nb = name.encode('utf-8')
            f.write(struct.pack('<B', len(nb))); f.write(nb)
            f.write(struct.pack('<iiB', len(pos), idx.size, 1))
            f.write(pos.astype('<f4').tobytes())
            f.write(np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes())
            f.write(uv.astype('<f4').tobytes())
            f.write(idx.astype('<u4').tobytes())


def export(ob, maps, dst):
    from PIL import Image
    os.makedirs(dst, exist_ok=True)
    if not os.path.exists(dst + '.meta'):
        open(dst + '.meta', 'w', newline='\n').write(
            'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    layers, center, length = extract_layers(ob)
    p = os.path.join(dst, 'xarn_science.bytes'); write_plm(p, layers); text_meta(p)

    ao = maps['ao'][..., 0]
    alb = maps['albedo'][..., :3] * (0.45 + 0.55 * ao[..., None])   # AO в альбедо — как в стратегиях: детали читаются издалека
    def save(arr, name, normal=False, srgb=True, q=93):
        p = os.path.join(dst, name)
        img = Image.fromarray(np.round(np.clip(arr, 0, 1) * 255).astype(np.uint8))
        if name.endswith('.jpg'): img.convert('RGB').save(p, quality=q, optimize=True)
        else: img.save(p, optimize=True)
        meta_for(p, normal, srgb)
    save(lin2srgb(alb), 'xarn_albedo.jpg')
    ms = np.zeros(ao.shape + (4,), np.float32)
    ms[..., 0] = maps['metal'][..., 0]; ms[..., 1] = ao; ms[..., 3] = 1.0 - maps['rough'][..., 0]
    small = Image.fromarray(np.round(np.clip(ms, 0, 1) * 255).astype(np.uint8), 'RGBA')
    if small.width > 1024: small = small.resize((1024, 1024), Image.LANCZOS)
    p = os.path.join(dst, 'xarn_ms.png'); small.save(p, optimize=True); meta_for(p, srgb=False)
    save(lin2srgb(maps['emit'][..., :3]), 'xarn_emission.jpg')
    save(maps['normal'][..., :3], 'xarn_normal.jpg', normal=True, srgb=False, q=96)

    # сопла (Blender-координаты -> Unity, нормированные)
    noz = []
    for x, z, r in [(0.0, -0.02, 0.36), (0.62, -0.16, 0.25), (-0.62, -0.16, 0.25)]:
        u = (np.array([x, z, -4.98]) - center) / length
        noz += [float(round(c, 4)) for c in u]
    hull = layers['hull'][0]
    cfg = dict(nozzles=noz, nozzleRadius=round(0.3 / length, 4),
               width=float(round(np.ptp(hull[:, 0]), 4)), height=float(round(np.ptp(hull[:, 1]), 4)), lengthUnits=float(round(length, 3)))
    p = os.path.join(dst, 'xarn_science_config.json'); open(p, 'w', encoding='utf-8', newline='\n').write(json.dumps(cfg, indent=1)); text_meta(p)
    print('  config', cfg)


# ============================================================================ превью

def preview(ob, maps, outdir, size, accent=(0.75, 0.13, 0.11)):
    """Рендер готового (запечённого) ассета: тот же атлас, что попадёт в игру."""
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene

    def img(key, arr, noncolor):
        im = bpy.data.images.new('pv_' + key, size, size, alpha=True, float_buffer=True)
        im.colorspace_settings.name = 'Non-Color' if noncolor else 'Linear Rec.709'
        a = np.ones((size, size, 4), np.float32); a[..., :arr.shape[2]] = arr[..., :4] if arr.shape[2] >= 4 else arr
        im.pixels.foreach_set(a[::-1].reshape(-1))
        return im
    ao = maps['ao'][..., :1]
    alb = img('alb', maps['albedo'][..., :3] * (0.45 + 0.55 * ao), False)
    mr = img('mr', np.concatenate([maps['metal'][..., :1], maps['rough'][..., :1], ao], 2), True)
    nm = img('nm', maps['normal'][..., :3], True)
    em = img('em', maps['emit'][..., :3], False)

    def game_mat(name, tint=None, beacon=False):
        m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
        b = nt.nodes['Principled BSDF']
        if beacon:
            b.inputs['Emission Color'].default_value = (1, 0.95, 0.9, 1); b.inputs['Emission Strength'].default_value = 6
            return m
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = alb
        col = t.outputs[0]
        if tint:
            col = mix(nt, 1.0, col, tint, 'MULTIPLY')
        nt.links.new(col, b.inputs['Base Color'])
        t2 = nt.nodes.new('ShaderNodeTexImage'); t2.image = mr
        s = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(t2.outputs[0], s.inputs[0])
        nt.links.new(s.outputs[0], b.inputs['Metallic']); nt.links.new(s.outputs[1], b.inputs['Roughness'])
        t3 = nt.nodes.new('ShaderNodeTexImage'); t3.image = nm
        n = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t3.outputs[0], n.inputs['Color']); nt.links.new(n.outputs[0], b.inputs['Normal'])
        t4 = nt.nodes.new('ShaderNodeTexImage'); t4.image = em
        nt.links.new(t4.outputs[0], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 3.0
        return m
    gm = {'hull': game_mat('G_hull'), 'accent': game_mat('G_accent', accent), 'beacon': game_mat('G_beacon', beacon=True)}
    me = ob.data
    names = [m.name for m in me.materials]
    me.materials.clear()
    for k in ('hull', 'accent', 'beacon'): me.materials.append(gm[k])
    remap = [('hull', 'accent', 'beacon').index(LAYER_OF.get(n, 'hull')) for n in names]
    mi = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('material_index', mi)
    mi = np.array(remap, np.int32)[mi]
    if 'material_index' not in me.attributes: me.attributes.new('material_index', 'INT', 'FACE')
    me.attributes['material_index'].data.foreach_set('value', mi)
    me.update()
    render_shots(outdir)


def render_shots(outdir, samples=96, res=(1600, 1000), only=None):
    os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene
    w = sc.world or bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.004, 0.006, 0.012, 1)
    w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
    def light(name, rot, energy, color):
        ld = bpy.data.lights.new(name, 'SUN'); ld.energy = energy; ld.color = color; ld.angle = math.radians(2)
        lo = bpy.data.objects.new(name, ld); lo.rotation_euler = [math.radians(a) for a in rot]; sc.collection.objects.link(lo)
    light('Key', (50, 10, 35), 4.0, (1.0, 0.95, 0.88))
    light('Rim', (-60, 0, 200), 2.5, (0.55, 0.7, 1.0))
    light('Fill', (120, 0, -60), 0.5, (0.8, 0.85, 1.0))
    cam_d = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cam_d); sc.collection.objects.link(cam); sc.camera = cam
    sc.render.engine = 'CYCLES'; sc.cycles.samples = samples; sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError: pass
    shots = {'three_quarter': ((7.5, 7.0, 5.0), 34), 'top': ((0.0, 0.0, 15.0), 40), 'side': ((11.0, 0.0, 0.6), 40),
             'rear': ((-5.0, -8.5, 3.2), 34), 'front': ((-6.0, 7.8, 1.6), 30)}
    for name, (loc, lens) in shots.items():
        if only and name not in only: continue
        cam.location = loc; cam_d.lens = lens
        cam_d.type = 'ORTHO' if name == 'top' else 'PERSP'; cam_d.ortho_scale = 12.5
        d = Vector((0, 0, 0)) - Vector(loc)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler() if name != 'top' else (0, 0, math.pi / 2)
        sc.render.filepath = os.path.join(outdir, name + '.png')
        bpy.ops.render.render(write_still=True)
        print('  render', name, flush=True)


# ============================================================================

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(ROOT, 'Assets/Resources/Models/Ships/XarnScience'))
    ap.add_argument('--blend', default=os.path.join(HERE, 'xarn_science.blend'))
    ap.add_argument('--tex', type=int, default=2048)
    ap.add_argument('--samples', type=int, default=16)
    ap.add_argument('--render', default='')
    ap.add_argument('--no-bake', action='store_true')
    ap.add_argument('--cache', default='', help='npz с запечёнными картами: есть — берём, нет — сохраняем')
    ap.add_argument('--quick', default='', help='быстрый рендер процедурных материалов без запекания')
    a = ap.parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    if a.quick: MATS['accent']['col'] = (0.5, 0.06, 0.05)   # в игре краску задаёт цвет владельца
    mats = {k: build_material(k, v) for k, v in MATS.items()}
    parts = build()
    objs = make_objects(parts, mats)
    if a.blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.blend), compress=True)
        print('blend:', a.blend)
    ob = join_all(objs)
    print('треугольников:', sum(len(p.vertices) - 2 for p in ob.data.polygons))
    if a.quick:
        render_shots(a.quick, samples=24, res=(1000, 625)); return
    unwrap(ob)
    if a.no_bake: return
    t = time.time()
    if a.cache and os.path.exists(a.cache):
        maps = dict(np.load(a.cache))
    else:
        maps = bake(ob, mats, a.tex, a.samples)
        if a.cache: np.savez(a.cache, **maps)
    print('запекание %.0fs' % (time.time() - t))
    export(ob, maps, a.out)
    if a.render: preview(ob, maps, a.render, a.tex)


if __name__ == '__main__':
    main()
