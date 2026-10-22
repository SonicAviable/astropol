#!/usr/bin/env python3
"""Собирает website/data.js из исходников игры, чтобы вики не расходилась с кодом.

Запуск из корня репозитория:  python3 website/tools/build_data.py
Читает: TechnologyManager1.cs (дерево технологий), TechFlavorDatabase.cs (описания),
        ShipDesignManager.cs (корпуса и модули), LeaderManager.cs (черты лидеров),
        FactionData.cs и LeaderPortraitView.cs (фракции и правители).
"""
import json, re, sys, os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
def read(p): return open(os.path.join(ROOT, p), encoding='utf-8').read()

# ---------------------------------------------------------------- C# helpers
def csharp_strings(chunk):
    """Склеивает все строковые литералы из куска кода (учитывает \\" и \\n)."""
    out = []
    for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', chunk):
        s = m.group(1).replace('\\"', '"').replace('\\n', '\n').replace('\\\\', '\\')
        out.append(s)
    return ''.join(out)

def split_args(s):
    """Делит аргументы вызова по запятым верхнего уровня."""
    args, depth, cur, instr, esc = [], 0, '', False, False
    for ch in s:
        if instr:
            cur += ch
            if esc: esc = False
            elif ch == '\\': esc = True
            elif ch == '"': instr = False
            continue
        if ch == '"': instr = True; cur += ch; continue
        if ch in '({[': depth += 1
        if ch in ')}]': depth -= 1
        if ch == ',' and depth == 0: args.append(cur.strip()); cur = ''; continue
        cur += ch
    if cur.strip(): args.append(cur.strip())
    return args

def calls(src, name):
    """Все вызовы name( ... ) с аргументами."""
    res = []
    for m in re.finditer(r'\b' + name + r'\(', src):
        i = m.end(); depth = 1; instr = False; esc = False
        while i < len(src) and depth:
            ch = src[i]
            if instr:
                if esc: esc = False
                elif ch == '\\': esc = True
                elif ch == '"': instr = False
            else:
                if ch == '"': instr = True
                elif ch == '(': depth += 1
                elif ch == ')': depth -= 1
            i += 1
        res.append(split_args(src[m.end():i - 1]))
    return res

def strip_tags(s):
    s = re.sub(r'<color=[^>]*>', '', s); s = re.sub(r'</color>|<b>|</b>|<i>|</i>', '', s)
    return s

# ---------------------------------------------------------------- технологии
tm = read('Assets/Scripts/Core/TechnologyManager1.cs')
body = tm[tm.index('public static List<Technology> BuildTechTree'):]
body = body[:body.index('return list;')]
years = {'Y0': 2200, 'Y1': 2208, 'Y2': 2216, 'Y3': 2229, 'Y4': 2242}
cat_branch = {'Reactor': 'physics', 'Sensors': 'physics', 'Propulsion': 'physics',
              'Society': 'society', 'Colonization': 'society', 'Doctrine': 'society',
              'Weapons': 'engineering', 'Defense': 'engineering', 'Industry': 'engineering', 'Construction': 'engineering'}
tier_factor = lambda t: 1 if t <= 0 else 4.4 if t == 1 else 9 + (t - 2) * 4
CPD = float(re.search(r'CostPerRawDay = ([\d.]+)f', tm).group(1))

flav_src = read('Assets/Scripts/Rendering/TechFlavorDatabase.cs')
flavor = {}
for m in re.finditer(r'\["(\w+)"\]\s*=\s*((?:\s*(?:Loc\.T\(\s*)?"(?:[^"\\]|\\.)*"\s*\)?\s*\+?)+)', flav_src):
    flavor[m.group(1)] = csharp_strings(m.group(2))

techs = []
for a in calls(body, 'Add'):
    if len(a) < 10 or not a[0].startswith('"'): continue
    tid = csharp_strings(a[0]); name = csharp_strings(a[1]); cat = a[2].split('.')[-1]
    desc = csharp_strings(a[3]); tier = int(a[4]); row = int(a[5]); days = float(a[6].rstrip('f'))
    reqs = re.findall(r'"(\w+)"', a[7]) if a[7] != 'null' else []
    year = years[a[8]]
    techs.append(dict(id=tid, name=name, cat=cat, branch=cat_branch[cat], desc=desc, tier=tier, row=row,
                      cost=round(days * CPD * tier_factor(tier)), year=year, req=reqs, flavor=flavor.get(tid, '')))
ids = {t['id'] for t in techs}
for t in techs:
    for r in t['req']: assert r in ids, (t['id'], r)
    t['unlocks'] = [u['id'] for u in techs if t['id'] in u['req']]
print('techs', len(techs), file=sys.stderr)

# ---------------------------------------------------------------- корпуса и модули
sd = read('Assets/Scripts/Core/ShipDesignManager.cs')
hulls = []
for m in re.finditer(r'Class = ShipClass\.(\w+), DisplayName = (?:Loc\.T\()?"([^"]+)"\)?,\s*WeaponSlots = (\d+), DefenseSlots = (\d+), UtilitySlots = (\d+),\s*BaseHull = ([\d.]+)f, BaseArmor = ([\d.]+)f, BaseShields = ([\d.]+)f,\s*BaseEvasion = ([\d.]+)f, BaseSpeed = ([\d.]+)f, BaseAlloyCost = ([\d.]+)f(?:,\s*RequiredTechId = "(\w+)")?', sd):
    hulls.append(dict(id=m.group(1).lower(), name=m.group(2), weapon=int(m.group(3)), defense=int(m.group(4)), utility=int(m.group(5)),
                      hull=float(m.group(6)), armor=float(m.group(7)), shields=float(m.group(8)), evasion=float(m.group(9)),
                      speed=float(m.group(10)), cost=float(m.group(11)), tech=m.group(12) or ''))
mods = []
for a in calls(sd, 'new ShipModule'):
    if len(a) < 15 or not a[0].startswith('"'): continue
    nums = [float(x.rstrip('f')) for x in a[5:15]]
    slot = a[3].split('.')[-1].lower()
    dtype = a[15].split('.')[-1].lower() if len(a) > 15 else 'kinetic'
    mid = csharp_strings(a[0])
    acc = float(re.search(r'ShipModule\("%s".*?\)\s*\{\s*Accuracy = ([\d.]+)f' % mid, sd, re.S).group(1)) if re.search(r'ShipModule\("%s"[^;]*?\)\s*\{\s*Accuracy' % mid, sd, re.S) else 0.0
    mods.append(dict(id=mid, name=csharp_strings(a[1]), desc=csharp_strings(a[2]), slot=slot, tech=csharp_strings(a[4]),
                     hull=nums[0], armor=nums[1], shields=nums[2], damage=nums[3], rate=nums[4], evasion=nums[5], speed=nums[6],
                     power=nums[7], draw=nums[8], cost=nums[9], dtype=dtype if slot == 'weapon' else '', acc=acc))
print('hulls', len(hulls), 'mods', len(mods), file=sys.stderr)

# ---------------------------------------------------------------- черты лидеров
lm = read('Assets/Scripts/Core/LeaderManager.cs')
traits = []
for m in re.finditer(r'new TraitDef \{([^}]*)\}', lm):
    f = m.group(1)
    g = lambda k: (re.search(k + r' = (?:Loc\.T\()?"([^"]*)"', f) or [None, ''])[1]
    cls = re.search(r'Class = LeaderClass\.(\w+)', f).group(1)
    traits.append(dict(id=g('Id'), cls=cls.lower(), name=g('Name'), desc=g('Desc'),
                       event='EventOnly = true' in f, negative='Negative = true' in f))
print('traits', len(traits), file=sys.stderr)

# ---------------------------------------------------------------- фракции и правители
fd = read('Assets/Scripts/Core/FactionData.cs')
fac_calls = calls(fd[fd.index('AvailableFactions'):], 'new FactionInfo')
lp = read('Assets/Scripts/Rendering/LeaderPortraitView.cs')
leaders = {}
for key in ['Xarn', 'Astrea', 'Aquila', 'Iridia', 'Terraan', 'Nol']:
    blk = lp[lp.index('public static readonly Entry %s' % key):]
    blk = blk[:blk.index('};')]
    g = lambda k: csharp_strings(re.search(k + r' =\s*(?:Loc\.T\()?((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)', blk).group(1))
    acc = re.search(r'Accent = new Color\(([\d.f, ]+)\)', blk).group(1)
    r, gg, b = [float(x.strip().rstrip('f')) for x in acc.split(',')][:3]
    leaders[key.lower()] = dict(name=g('Name'), title=g('Title'), quote=g('Quote'),
                                accent='#%02x%02x%02x' % (round(r * 255), round(gg * 255), round(b * 255)))
keys = ['astrea', 'xarn', 'aquila', 'iridia', 'terraan', 'nol']
factions = []
for k, a in zip(keys, fac_calls):
    desc = csharp_strings(a[2])
    lore, _, rest = desc.partition('\n\n')
    c = re.search(r'new Color\(([\d.f, ]+)\)', a[3]).group(1)
    cr, cg, cb = [float(x.strip().rstrip('f')) for x in c.split(',')][:3]
    factions.append(dict(key=k, name=csharp_strings(a[0]), title=csharp_strings(a[1]), lore=lore,
                         perks=[strip_tags(x) for x in rest.split('\n') if x.strip()],
                         color='#%02x%02x%02x' % (round(cr * 255), round(cg * 255), round(cb * 255)), leader=leaders[k]))
print('factions', len(factions), file=sys.stderr)

# ---------------------------------------------------------------- характеры ИИ
ap = read('Assets/Scripts/Core/AIPersonality.cs')
personalities = {}
def parse_personality(blk):
    nm = re.search(r'p\.Name = (?:Loc\.T\()?"([^"]*)"', blk); sm = re.search(r'p\.Summary = (.*?);', blk, re.S)
    return dict(name=nm.group(1), summary=csharp_strings(sm.group(1))) if nm and sm else None
for m in re.finditer(r'case AIPersonality\.(\w+):\s*(.*?)break;', ap, re.S):
    pr = parse_personality(m.group(2))
    if pr: personalities[m.group(1).lower()] = pr
m = re.search(r'default:\s*(.*?)break;', ap, re.S)
if m:
    pr = parse_personality(m.group(1))
    if pr: personalities['militarist'] = pr
print('personalities', list(personalities), file=sys.stderr)

out = dict(version=re.search(r'bundleVersion: (\S+)', read('ProjectSettings/ProjectSettings.asset')).group(1),
           techs=techs, hulls=hulls, modules=mods, traits=traits, factions=factions, personalities=personalities)
with open(os.path.join(ROOT, 'website', 'data.js'), 'w', encoding='utf-8') as f:
    f.write('/* Сгенерировано website/tools/build_data.py из исходников игры. Не править вручную. */\n')
    f.write('window.GAME_DATA = ' + json.dumps(out, ensure_ascii=False, separators=(',', ':')) + ';\n')
print('ok', os.path.getsize(os.path.join(ROOT, 'website', 'data.js')) // 1024, 'KB', file=sys.stderr)
