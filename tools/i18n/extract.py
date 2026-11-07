# Pulls every piece of on-screen English out of index.html's script: plain strings and strings built with + (as templates with {0}, {1}...).
import re, json, sys, html as H
src = open(sys.argv[1] if len(sys.argv) > 1 else 'index.html', encoding='utf-8').read()
a = src.index('<script>', src.index('</style>')); b = src.rindex('</script>')
js = src[a+8:b]
# not interface text: patch notes, the built-in game list, colour tables, the TV keyboard's keys
def blank(js, start, end):
    i = js.find(start)
    if i < 0: return js
    j = js.find(end, i)
    if j < 0: return js
    return js[:i] + re.sub(r'[^\n]', ' ', js[i:j]) + js[j:]
for st, en in (('/*I18N*/', '/*/I18N*/'), ('var NOTES=[', '\n];'), ('  var L=[\n', '\n  return L.map('), ('var ACC=[', '\n'), ('var AV=[', '\n'), ('var PAL=[', '\n'), ('var TVKB=[', '\n'), ('var FAN=', '\n')):
    js = blank(js, st, en)

def tokens(s):
    i, n, out, prev = 0, len(s), [], None
    regex_ok = lambda p: p is None or (p[0] == 'p' and p[1] in '(,=:[!&|?{};+-*%<>~^') or (p[0] == 'id' and p[1] in ('return', 'typeof', 'case', 'in', 'of', 'new', 'delete', 'void', 'else'))
    while i < n:
        c = s[i]
        if c in ' \t\r\n': i += 1; continue
        if s.startswith('//', i): j = s.find('\n', i); i = n if j < 0 else j; continue
        if s.startswith('/*', i): j = s.find('*/', i + 2); i = n if j < 0 else j + 2; continue
        if c in '"\'':
            j = i + 1
            while j < n and s[j] != c:
                j += 2 if s[j] == '\\' else 1
            raw = s[i+1:j]
            try: val = json.loads('"' + raw.replace('\\\'', "'").replace('"', '\\"').replace('\\\\"', '\\"') + '"') if c == "'" else json.loads('"' + raw + '"')
            except Exception: val = raw
            prev = ('s', val, i); out.append(prev); i = j + 1; continue
        if c == '/' and regex_ok(prev):
            j, cls = i + 1, False
            while j < n:
                if s[j] == '\\': j += 2; continue
                if s[j] == '[': cls = True
                elif s[j] == ']': cls = False
                elif s[j] == '/' and not cls: break
                elif s[j] == '\n': break
                j += 1
            j += 1
            while j < n and s[j].isalpha(): j += 1
            prev = ('re', s[i:j], i); out.append(prev); i = j; continue
        m = re.match(r'[A-Za-z_$][\w$]*', s[i:])
        if m: prev = ('id', m.group(0), i); out.append(prev); i += len(m.group(0)); continue
        m = re.match(r'\d+(\.\d+)?', s[i:])
        if m: prev = ('num', m.group(0), i); out.append(prev); i += len(m.group(0)); continue
        m = re.match(r'===|!==|==|!=|<=|>=|&&|\|\||\+\+|--|\+=|-=', s[i:])
        if m: prev = ('p', m.group(0), i); out.append(prev); i += len(m.group(0)); continue
        prev = ('p', c, i); out.append(prev); i += 1
    return out

T = tokens(js)
WORD = re.compile(r'[A-Za-z]{2,}')
def ui_like(v):
    if not WORD.search(v): return False
    if re.match(r'^\s*[#.]?[\w-]+\s*[{:]', v) and ('{' in v or ';' in v): return False          # css
    if re.search(r'https?://|\.itch\.io|^[\w-]+\.(png|jpg|json|exe|zip|html|txt|bin|key|dll)$|^[a-z]+:[a-z]', v): return False
    if re.match(r'^[a-z][a-zA-Z0-9_-]*$', v) and not re.match(r'^(ok|no|yes|on|off)$', v): return False  # ids, keys, class names
    if re.match(r'^[a-z0-9]+( [a-z0-9-]+)+$', v) and len(v) < 30 and not re.search(r'\b(the|a|to|and|of|your|you|is|in|on|for|with|it|this|games?)\b', v): return False  # class lists
    if re.search(r'^(xaly-|onHttp|function|var |return )', v): return False
    if '<path' in v or '<svg' in v or 'viewBox' in v: return False
    if re.match(r'^[#.][\w-]', v) or re.match(r'^#[0-9a-fA-F]{3,6}$', v): return False
    if re.match(r'^/|^[a-z]+/[\w/]+', v) or '/rest/v1/' in v: return False
    if re.match(r'^[A-Z]{1,3}$', v): return False
    return True

def html_texts(v):   # text between tags of an HTML string literal
    return [H.unescape(t) for t in re.split(r'<[^>]*>', v) if t.strip()]

exact, temps = {}, {}
def add(v, where):
    v2 = v.strip()
    if v2 and ui_like(v2) and len(v2) < 400: exact.setdefault(v2, where)

# 1) concatenations: a + "b" + c ... at the same depth -> template with {0}, {1}
STOP = (',', ';', ':', '?', '&&', '||', '===', '!==', '==', '!=', '=', '+=', '(', '[', '{', 'return', 'case')
def operand_left(k):            # index of the first token of the operand that ends at k
    tk = T[k]
    if tk[0] == 'p' and tk[1] in ')]':
        d, k2 = 0, k
        while k2 >= 0:
            if T[k2][0] == 'p' and T[k2][1] in ')]': d += 1
            elif T[k2][0] == 'p' and T[k2][1] in '([':
                d -= 1
                if d == 0: break
            k2 -= 1
        k = k2 - 1
        if k >= 0 and T[k][0] == 'id' and T[k][1] not in ('return', 'case', 'typeof'): return operand_left(k)   # f(...) / a.b(...)
        return k + 1
    if tk[0] in ('id', 'num', 's'):
        while k - 2 >= 0 and T[k-1][0] == 'p' and T[k-1][1] == '.' and T[k-2][0] in ('id', 'p'):
            if T[k-2][0] == 'p' and T[k-2][1] in ')]': return operand_left(k - 2)
            k -= 2
        return k
    return k + 1
used = set()
for i0, t in enumerate(T):
    if t[0] != 's' or i0 in used: continue
    # walk left over "operand +"
    start = i0
    while start - 1 >= 0 and T[start-1][0] == 'p' and T[start-1][1] == '+':
        l = operand_left(start - 2)
        if l > start - 2: break
        start = l
    # walk right
    seq, cur, j, bal = [], [], start, 0
    while j < len(T):
        tk = T[j]
        if tk[0] == 'p' and tk[1] in '([{': bal += 1
        elif tk[0] == 'p' and tk[1] in ')]}':
            if bal == 0: break
            bal -= 1
        elif bal == 0 and ((tk[0] == 'p' and tk[1] in STOP) or (tk[0] == 'id' and tk[1] in ('return', 'case'))):
            break
        if bal == 0 and tk[0] == 'p' and tk[1] == '+': seq.append(cur); cur = []
        else: cur.append(tk)
        j += 1
    seq.append(cur)
    for o in seq:
        for tk in o:
            if tk[0] == 's': used.add(T.index(tk) if False else None)
    k2 = start
    while k2 < j:
        if T[k2][0] == 's': used.add(k2)
        k2 += 1
    if len(seq) > 1 and any(len(o) == 1 and o[0][0] == 's' and WORD.search(o[0][1]) for o in seq):
        tpl, n = '', 0
        for o in seq:
            if len(o) == 1 and o[0][0] == 's': tpl += o[0][1]
            else: tpl += '{%d}' % n; n += 1
        if '<' in tpl:
            for x in html_texts(tpl):
                if '{' in x: temps.setdefault(x.strip(), t[2])
                else: add(x, t[2])
        elif ui_like(tpl.replace('{', '').replace('}', '')): temps.setdefault(tpl.strip(), t[2])
        for o in seq:
            if len(o) == 1 and o[0][0] == 's':
                if '<' in o[0][1]: [add(x, t[2]) for x in html_texts(o[0][1]) if '{' not in x]
                else: add(o[0][1], t[2])
    else:
        if '<' in t[1]: [add(x, t[2]) for x in html_texts(t[1])]
        else: add(t[1], t[2])
temps = {k: v for k, v in temps.items() if re.search(r'[A-Za-z]{2,}', re.sub(r'\{\d+\}', '', k)) and not re.match(r'^\{\d+\}$', k)}
json.dump({'exact': sorted(exact), 'temps': sorted(temps)}, open(sys.argv[2] if len(sys.argv) > 2 else 'strings.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(len(exact), 'strings', len(temps), 'templates')
