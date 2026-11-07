# Embeds the Spanish (Latin America) and Portuguese (Brazil) translations into index.html.
# source.json holds the English text; tr/*.tsv hold "index<TAB>Spanish<TAB>Portuguese" (e = exact text, t = template with {0}..).
# "-" means "no translation needed" (a fragment, or text that is the same in every language).
import json, glob, os, re, sys
here = os.path.dirname(os.path.abspath(__file__))
src = json.load(open(os.path.join(here, 'source.json'), encoding='utf-8'))
x, t = {}, []
for path in sorted(glob.glob(os.path.join(here, 'tr', '*.tsv'))):
    kind = os.path.basename(path)[0]
    for ln in open(path, encoding='utf-8'):
        ln = ln.rstrip('\n')
        if not ln.strip(): continue
        p = ln.split('\t')
        if len(p) != 3: sys.exit('bad line in %s: %r' % (path, ln[:80]))
        i, es, pt = int(p[0]), p[1], p[2]
        if es == '-' and pt == '-': continue
        en = (src['exact'] if kind == 'e' else src['temps'])[i]
        if kind == 'e':
            en = en.replace('\n', ' / ') if False else en
            x[en] = [es.replace(' / ', '\n') if '\n' in en else es, pt.replace(' / ', '\n') if '\n' in en else pt]
        else:
            ph = sorted(set(re.findall(r'\{(\d+)', en)))
            for tr in (es, pt):
                if sorted(set(re.findall(r'\{(\d+)', tr))) != ph: sys.exit('placeholders differ in template %d: %r -> %r' % (i, en, tr))
            t.append([en, es, pt])
extra = os.path.join(here, 'extra.json')   # hand-added text found by the coverage test
if os.path.exists(extra):
    e = json.load(open(extra, encoding='utf-8'))
    for k, v in e.get('x', {}).items(): x[k] = v
    for row in e.get('t', []): t.append(row)
blob = json.dumps({'x': x, 't': t}, ensure_ascii=False, separators=(',', ':')).replace('</', '<\\/')
f = os.path.join(here, '..', '..', 'index.html')
h = open(f, encoding='utf-8').read()
a, b = '/*I18N*/', '/*/I18N*/'
i, j = h.index(a), h.index(b)
h = h[:i + len(a)] + 'var I18N=' + blob + ';' + h[j:]
open(f, 'w', encoding='utf-8').write(h)
print(len(x), 'strings,', len(t), 'templates,', len(blob) // 1024, 'KB')
