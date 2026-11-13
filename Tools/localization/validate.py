"""python3 validate.py in_N.json out_N.json — проверка перевода партии."""
import json, re, sys
inp = json.load(open(sys.argv[1], encoding='utf-8'))
out = json.load(open(sys.argv[2], encoding='utf-8'))
PH = re.compile(r'\{\{|\}\}|\{[^{}]*\}')
TAG = re.compile(r'</?(?:b|i|color|size)(?:=[^>]*)?>')
bad = 0
for e in inp:
    ru = e['ru']
    if ru not in out or not isinstance(out[ru], str) or not out[ru].strip():
        print('MISSING:', repr(ru)[:100]); bad += 1; continue
    en = out[ru]
    if sorted(PH.findall(ru)) != sorted(PH.findall(en)):
        print('PLACEHOLDERS:', repr(ru)[:80], '=>', repr(en)[:80]); bad += 1
    if sorted(TAG.findall(ru)) != sorted(TAG.findall(en)):
        print('TAGS:', repr(ru)[:80], '=>', repr(en)[:80]); bad += 1
    if re.search(r'[А-Яа-яЁё]', en):
        print('CYRILLIC LEFT:', repr(en)[:100]); bad += 1
    if ru[:1].isspace() != en[:1].isspace() or ru[-1:].isspace() != en[-1:].isspace():
        print('EDGE SPACES:', repr(ru)[:60], '=>', repr(en)[:60]); bad += 1
print('checked', len(inp), 'problems', bad)
