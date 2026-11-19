"""
Локализация Astropolity: находит в C# пользовательские русские строки и оборачивает их в Loc.T / Loc.F.

  extract <root> <out.json>      — уникальные ключи (строка во время выполнения) + файл-источник
  apply   <root> [--dry]         — переписать файлы

Правила:
  "текст"            -> Loc.T("текст")
  $"текст {x:0.0}"   -> Loc.F("текст {0:0.0}", x)      (ключ — шаблон string.Format)
Пропускаются: атрибуты, const, значения параметров по умолчанию, case/шаблоны, сравнения ==/!=,
аргументы Contains/StartsWith/... и Debug.Log*, ключи индексаторов, строки без кириллицы в тексте.
"""
import sys, os, re, json
import tree_sitter_c_sharp as tscs
from tree_sitter import Language, Parser

P = Parser(Language(tscs.language()))
CYR = re.compile(r'[А-Яа-яЁё]')

SKIP_CALLS = {'Contains', 'StartsWith', 'EndsWith', 'IndexOf', 'LastIndexOf', 'Equals', 'Replace', 'Split',
              'Trim', 'TrimStart', 'TrimEnd', 'Log', 'LogWarning', 'LogError', 'LogFormat', 'LogWarningFormat',
              'LogErrorFormat', 'LogException', 'Assert', 'Find', 'Load', 'LoadAll', 'GetInt', 'SetInt', 'GetFloat',
              'SetFloat', 'GetString', 'SetString', 'HasKey', 'DeleteKey', 'nameof', 'T', 'F'}
SKIP_ANCESTORS = {'attribute', 'attribute_list', 'parameter', 'constant_pattern', 'case_switch_label',
                  'switch_expression_arm_pattern', 'is_pattern_expression', 'relational_pattern',
                  'bracketed_argument_list', 'element_access_expression', 'implicit_element_access',
                  'preprocessor_call', 'using_directive'}


def unescape_regular(s):
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            m = {'n': '\n', 't': '\t', 'r': '\r', '"': '"', "'": "'", '\\': '\\', '0': '\0', 'a': '\a', 'b': '\b', 'f': '\f', 'v': '\v'}
            if n in m:
                out.append(m[n]); i += 2; continue
            if n == 'u':
                out.append(chr(int(s[i + 2:i + 6], 16))); i += 6; continue
            if n == 'x':
                j = i + 2
                while j < len(s) and j < i + 6 and s[j] in '0123456789abcdefABCDEF': j += 1
                out.append(chr(int(s[i + 2:j], 16))); i = j; continue
        out.append(c); i += 1
    return ''.join(out)


def escape_regular(s):
    return (s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r')
             .replace('\t', '\\t').replace('\0', '\\0'))


def is_const_decl(n):
    # field_declaration / local_declaration_statement с модификатором const
    for c in n.children:
        if c.type == 'modifier' and c.text == b'const': return True
        if c.type == 'const': return True
    return False


def skip_context(node):
    n = node.parent
    child = node
    while n is not None:
        t = n.type
        if t in SKIP_ANCESTORS: return True
        if t in ('field_declaration', 'local_declaration_statement') and is_const_decl(n): return True
        if t == 'binary_expression':
            op = [c for c in n.children if c.type in ('==', '!=')]
            if op: return True
        if t == 'switch_expression_arm':
            # шаблон — первый ребёнок; значение после => можно переводить
            if n.children and n.children[0] == child: return True
        if t == 'argument_list' and n.parent is not None and n.parent.type == 'invocation_expression':
            fn = n.parent.children[0]
            name = fn.text.decode('utf-8', 'replace')
            last = re.split(r'[.\s]', name)[-1]
            if last in SKIP_CALLS: return True
            if 'Debug.' in name: return True
        if t in ('interpolation',):
            pass
        if t in ('method_declaration', 'class_declaration', 'struct_declaration', 'compilation_unit'):
            break
        child = n
        n = n.parent
    return False


def literal_info(node, src):
    """(runtime_value, kind) для string_literal / verbatim_string_literal."""
    text = src[node.start_byte:node.end_byte].decode('utf-8')
    if node.type == 'verbatim_string_literal':
        return text[2:-1].replace('""', '"'), 'verbatim'
    if node.type == 'string_literal':
        return unescape_regular(text[1:-1]), 'regular'
    return None, None


class Rewriter:
    def __init__(self, src, path):
        self.src = src
        self.path = path
        self.keys = []      # (key, path)

    def text(self, a, b):
        return self.src[a:b].decode('utf-8')

    def rewrite(self, node):
        """Исходный текст узла с заменами внутри."""
        t = node.type
        if t in ('string_literal', 'verbatim_string_literal'):
            val, kind = literal_info(node, self.src)
            if val is not None and CYR.search(val) and not skip_context(node):
                self.keys.append((val, self.path))
                return 'Loc.T(' + self.text(node.start_byte, node.end_byte) + ')'
            return self.text(node.start_byte, node.end_byte)
        if t == 'interpolated_string_expression':
            res = self.interpolated(node)
            if res is not None: return res
        # общий случай: склеиваем детей
        out = []
        pos = node.start_byte
        for c in node.children:
            out.append(self.text(pos, c.start_byte))
            out.append(self.rewrite(c))
            pos = c.end_byte
        out.append(self.text(pos, node.end_byte))
        return ''.join(out)

    def interpolated(self, node):
        head = self.text(node.start_byte, node.end_byte)
        verbatim = '@' in head[:3]
        parts = []      # шаблон
        args = []
        has_cyr = False
        idx = 0
        for c in node.children:
            ct = c.type
            if ct in ('string_content', 'escape_sequence'):
                raw = self.text(c.start_byte, c.end_byte)
                if verbatim:
                    val = raw.replace('""', '"')
                else:
                    val = unescape_regular(raw)
                if CYR.search(val): has_cyr = True
                parts.append(val)       # двойные {{ }} в тексте интерполяции сохраняются как есть — это и есть формат
            elif ct == 'interpolation':
                expr = None; align = ''; fmt = ''
                for cc in c.children:
                    if cc.type == 'interpolation_brace': continue
                    if cc.type == 'interpolation_alignment_clause': align = self.text(cc.start_byte, cc.end_byte)
                    elif cc.type == 'interpolation_format_clause': fmt = self.text(cc.start_byte, cc.end_byte)
                    elif expr is None: expr = cc
                if expr is None: return None
                parts.append('{' + str(idx) + align + fmt + '}')
                args.append(self.rewrite(expr))
                idx += 1
        if not has_cyr or skip_context(node):
            return None
        tmpl = ''.join(parts)
        self.keys.append((tmpl, self.path))
        lit = '"' + escape_regular(tmpl) + '"'
        if not args:
            # без подстановок {{ }} → { } уже в рантайме не нужны: строка без аргументов, string.Format не вызываем
            plain = tmpl.replace('{{', '{').replace('}}', '}')
            self.keys[-1] = (plain, self.path)
            return 'Loc.T("' + escape_regular(plain) + '")'
        return 'Loc.F(' + lit + ', ' + ', '.join(args) + ')'


def files(root):
    for d, _, fs in os.walk(root):
        if os.sep + 'Editor' in d: continue
        for f in fs:
            if f.endswith('.cs') and f not in ('Loc.cs',): yield os.path.join(d, f)


def process(path, write):
    src = open(path, 'rb').read()
    tree = P.parse(src)
    rw = Rewriter(src, path)
    new = rw.rewrite(tree.root_node)
    if write and rw.keys:
        if 'using StellarisClone.Core;' not in new and 'namespace StellarisClone.Core' not in new:
            # Loc живёт в StellarisClone.Core
            m = re.search(r'^(using [^\n]+;\n)+', new, re.M)
            if m: new = new[:m.end()] + 'using StellarisClone.Core;\n' + new[m.end():]
            else: new = 'using StellarisClone.Core;\n' + new
        open(path, 'w', encoding='utf-8', newline='').write(new)
    return rw.keys


if __name__ == '__main__':
    cmd, root = sys.argv[1], sys.argv[2]
    allkeys = {}
    for f in sorted(files(root)):
        for k, p in process(f, write=(cmd == 'apply' and '--dry' not in sys.argv)):
            allkeys.setdefault(k, os.path.relpath(p, root))
    if cmd == 'extract':
        json.dump([{'ru': k, 'file': v} for k, v in allkeys.items()], open(sys.argv[3], 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
    print('keys', len(allkeys))
