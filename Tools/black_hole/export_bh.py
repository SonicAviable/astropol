# Слои чёрной дыры из исходного FBX -> Assets/Resources/Models/BlackHole/blackhole.bytes (формат BHM1, читает BlackHoleModel.cs).
# Запуск: python3 export_bh.py "black hole.fbx" ../../Assets/Resources/Models/BlackHole/blackhole.bytes
# Нужен только numpy: двоичный FBX (7.x) разбирается здесь же. Лишняя «Planet» и тёмная подложка (blackoutside) не берутся,
# из облака обломков остаётся каждый третий кусок. Оси: FBX Z-up -> Unity Y-up, радиус внешнего кольца -> 1.
import sys, struct, zlib
import numpy as np

def parse(path):
    d=open(path,'rb').read()
    ver=struct.unpack_from('<I',d,23)[0]
    big=ver>=7500
    def rd_node(o):
        if big: end,np,pl=struct.unpack_from('<QQQ',d,o); o+=24
        else: end,np,pl=struct.unpack_from('<III',d,o); o+=12
        nl=d[o]; o+=1
        if end==0: return None,o
        name=d[o:o+nl].decode('latin1'); o+=nl
        props=[]
        for _ in range(np):
            t=chr(d[o]); o+=1
            if t=='Y': props.append(struct.unpack_from('<h',d,o)[0]); o+=2
            elif t=='C': props.append(d[o]); o+=1
            elif t=='I': props.append(struct.unpack_from('<i',d,o)[0]); o+=4
            elif t=='F': props.append(struct.unpack_from('<f',d,o)[0]); o+=4
            elif t=='D': props.append(struct.unpack_from('<d',d,o)[0]); o+=8
            elif t=='L': props.append(struct.unpack_from('<q',d,o)[0]); o+=8
            elif t in 'SR':
                n=struct.unpack_from('<I',d,o)[0]; o+=4; v=d[o:o+n]; o+=n
                props.append(v.decode('latin1') if t=='S' else v)
            else:
                n,enc,cl=struct.unpack_from('<III',d,o); o+=12
                raw=d[o:o+cl]; o+=cl
                if enc: raw=zlib.decompress(raw)
                fmt={'f':'f','d':'d','l':'q','i':'i','b':'B'}[t]
                props.append(list(struct.unpack('<%d%s'%(n,fmt),raw)))
        kids=[]
        while o<end:
            k,o=rd_node(o)
            if k is None: break
            kids.append(k)
        return (name,props,kids),end
    o=27; nodes=[]
    while o<len(d)-200:
        n,o=rd_node(o)
        if n is None: break
        nodes.append(n)
    return ver,nodes
def find(n,name): return [k for k in n[2] if k[0]==name]

def load(path):
    ver,nodes=parse(path)
    top={n[0]:n for n in nodes}
    G={}
    for o in top['Objects'][2]:
        if o[0]!='Geometry': continue
        name=o[1][1].split('\x00')[0]
        if name=='Planet': continue
        v=np.array(find(o,'Vertices')[0][1][0]).reshape(-1,3)
        pi=np.array(find(o,'PolygonVertexIndex')[0][1][0])
        uvl=find(o,'LayerElementUV')[0]
        uv=np.array(find(uvl,'UV')[0][1][0]).reshape(-1,2); uvi=np.array(find(uvl,'UVIndex')[0][1][0])
        mats=np.array(find(find(o,'LayerElementMaterial')[0],'Materials')[0][1][0])
        idx=np.where(pi<0,~pi,pi); ends=np.where(pi<0)[0]; starts=np.r_[0,ends[:-1]+1]
        G[name]=dict(v=v,idx=idx,uv=uv,uvi=uvi,starts=starts,ends=ends,mats=mats)
    return G


FBX, OUT = sys.argv[1], sys.argv[2]
G = load(FBX)
OUTER = 1694.0  # внешний радиус кольца в единицах FBX -> 1.0

LAYERS = [  # имя, меш, материал, доля обломков (1 = все)
    ('horizon', 'black hole', 0),
    ('light1', 'black hole', 3),
    ('light2', 'black hole', 4),
    ('light3', 'black hole', 5),
    ('photon', 'black hole', 6),
    ('ring', 'black hole_ring', 0),
    ('debris', 'black hole_ring', 1),
]

def components(polys, nverts):
    p = np.arange(nverts)
    def f(x):
        while p[x] != x:
            p[x] = p[p[x]]; x = p[x]
        return x
    for q in polys:
        for a in q[1:]:
            ra, rb = f(q[0]), f(a)
            if ra != rb: p[ra] = rb
    return [f(q[0]) for q in polys]

def build(gname, mat):
    g = G[gname]
    sel = np.where(g['mats'] == mat)[0]
    polys = [np.arange(g['starts'][s], g['ends'][s] + 1) for s in sel]   # индексы угловых вершин полигона
    if gname == 'black hole_ring' and mat == 1:
        comp = components([g['idx'][c] for c in polys], len(g['v']))
        roots = sorted(set(comp))
        keep = set(roots[::3])          # треть обломков: облако остаётся густым, вершин втрое меньше
        polys = [c for c, r in zip(polys, comp) if r in keep]
    key = {}
    verts = []; tris = []
    for c in polys:
        ids = []
        for corner in c:
            k = (g['idx'][corner], g['uvi'][corner])
            if k not in key:
                key[k] = len(verts); verts.append(k)
            ids.append(key[k])
        for i in range(1, len(ids) - 1):
            tris.append((ids[0], ids[i], ids[i + 1]))
    vi = np.array([k[0] for k in verts]); ui = np.array([k[1] for k in verts])
    P = g['v'][vi] / OUTER
    P = P[:, [0, 2, 1]]                 # FBX Z-up -> Unity Y-up (диск в плоскости XZ); зеркало -> обход CW = лицевой в Unity
    UV = g['uv'][ui]
    T = np.array(tris)
    # гладкие нормали по исходным точкам (швы UV не рвут нормаль)
    a, b, c = P[T[:, 0]], P[T[:, 1]], P[T[:, 2]]
    fn = np.cross(b - a, c - a)
    N = np.zeros_like(P)
    for j in range(3): np.add.at(N, T[:, j], fn)
    Nc = np.zeros((len(g['v']), 3)); np.add.at(Nc, vi, N); N = Nc[vi]
    N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
    return P, N, UV, T

with open(OUT, 'wb') as f:
    f.write(b'BHM1'); f.write(struct.pack('<i', len(LAYERS)))
    for name, gname, mat in LAYERS:
        P, N, UV, T = build(gname, mat)
        assert len(P) < 65536, (name, len(P))
        if name == 'horizon':   # лицевая сторона сферы горизонта должна смотреть наружу
            cen = P[T].mean(1); fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
            out = (np.einsum('ij,ij->i', fn, cen) > 0).mean()
            print('horizon outward share', round(out, 3))
            if out < 0.5: T = T[:, [0, 2, 1]]
        nb = name.encode()
        f.write(struct.pack('<B', len(nb))); f.write(nb)
        f.write(struct.pack('<ii', len(P), T.size))
        f.write(np.clip(np.round(P * 32767), -32767, 32767).astype('<i2').tobytes())
        f.write(np.clip(np.round(N * 127), -127, 127).astype('i1').tobytes())
        f.write(np.clip(np.round((UV + 0.25) / 1.5 * 65535), 0, 65535).astype('<u2').tobytes())
        f.write(T.astype('<u2').tobytes())
        print('%-8s verts %6d tris %6d  r<=%.3f |y|<=%.3f uv %s..%s' % (name, len(P), len(T), np.hypot(P[:, 0], P[:, 2]).max(), np.abs(P[:, 1]).max(), UV.min(0).round(2), UV.max(0).round(2)))
