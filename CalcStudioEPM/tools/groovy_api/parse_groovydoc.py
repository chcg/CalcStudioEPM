"""Builds groovy_api.json (types, methods with parameters and return types, enum constants,
inheritance) from the downloaded EPM Groovy javadoc pages. Signatures only — no descriptions."""
import html, json, os, re, sys

doc, out_path = sys.argv[1], sys.argv[2]
pages = os.path.join(doc, 'pages')

def text(fragment):
    s = re.sub(r'<[^>]+>', '', fragment)
    s = html.unescape(s).replace('\xa0', ' ')
    return re.sub(r'\s+', ' ', s).strip()

def short(type_name):
    """java.util.List<oracle.epm.api.model.Member> -> List<Member>"""
    return re.sub(r'\b(?:[a-z_][a-z0-9_]*\.)+([A-Z][\w]*)', r'\1', type_name)

types = {}
for f in sorted(os.listdir(pages)):
    t = open(os.path.join(pages, f), encoding='utf-8').read()
    full = f[:-5].replace('_', '.')                    # oracle.epm.api.model.DataGrid.Cell
    pkg = re.match(r'(oracle(?:\.[a-z]+)+)\.', full).group(1)
    name = full[len(pkg) + 1:]                          # DataGrid.Cell
    m = re.search(r'<h2 title="([A-Za-z]+) ', t)
    kind = m.group(1).lower() if m else 'class'

    # Supertypes from the declaration <pre>
    decl = re.search(r'<pre>(.*?)</pre>', t[t.find('<div class="description">'):], re.S)
    parents = []
    if decl:
        d = text(decl.group(1))
        mm = re.search(r'\b(?:extends|implements)\s+(.*)$', d)
        if mm:
            parents = [short(p.strip()) for p in re.split(r',\s*(?![^<]*>)|\bimplements\b', mm.group(1)) if p.strip()]
            parents = [re.sub(r'<.*>', '', p).strip() for p in parents]

    methods = []
    summ = t.find('<!-- ========== METHOD SUMMARY')
    if summ >= 0:
        end = t.find('<!-- ====', summ + 10)
        block = t[summ:end if end > 0 else len(t)]
        for row in re.findall(r'<tr id="i\d+"[^>]*>(.*?)</tr>', block, re.S):
            cols = re.findall(r'<td class="col(?:First|Last)">(.*?)</td>', row, re.S)
            if len(cols) < 2:
                continue
            ret = text(cols[0])
            static = ret.startswith('static ')
            ret = short(re.sub(r'^(static|default|abstract|final)\s+', '', ret))
            ret = re.sub(r'^<[^>]+>\s+', '', ret)       # <T> T  ->  T
            code = re.search(r'<code>(.*?)</code>', cols[1], re.S).group(1)
            mname = text(re.search(r'<span class="memberNameLink">(.*?)</span>', code, re.S).group(1))
            params = text(code[code.find('</span>') + 7:])
            deprecated = 'deprecatedLabel' in cols[1]
            methods.append({'name': mname, 'params': short(params), 'returns': ret, 'static': static, 'deprecated': deprecated})

    # Inherited EPM methods (javadoc lists them by name only, with the owning type)
    inherited = re.findall(r'Methods inherited from (?:class|interface)&nbsp;(oracle\.[\w.]+)', t)
    for owner in inherited:
        oname = owner.split('.')[-1]
        if oname not in parents:
            parents.append(oname)

    constants = []
    ec = t.find('<!-- =========== ENUM CONSTANT SUMMARY')
    if ec >= 0:
        end = t.find('<!-- ====', ec + 10)
        constants = re.findall(r'<span class="memberNameLink"><a [^>]*>(\w+)</a></span>', t[ec:end])
    fs = t.find('<!-- =========== FIELD SUMMARY')
    if fs >= 0:
        end = t.find('<!-- ====', fs + 10)
        constants += re.findall(r'<span class="memberNameLink"><a [^>]*>(\w+)</a></span>', t[fs:end])

    parents = [p for p in parents if p and p != 'Object']
    types[name] = {'package': pkg, 'kind': kind, 'parents': parents, 'methods': methods, 'constants': constants}

# Script bindings documented for EPM Groovy rules
bindings = {'operation': 'Operation', 'rule': 'Rule'}
api = {
    'source': 'https://docs.oracle.com/en/cloud/saas/enterprise-performance-management-common/groov/index-all.html',
    'note': 'Signatures compiled from the Oracle EPM Groovy Scripting Reference (names, parameters, return types only).',
    'bindings': bindings,
    'types': types,
}
json.dump(api, open(out_path, 'w', encoding='utf-8'), indent=None, separators=(',', ':'), ensure_ascii=False)
n = sum(len(v['methods']) for v in types.values())
print(len(types), 'types,', n, 'methods,', sum(len(v['constants']) for v in types.values()), 'constants')
for k in ('Operation', 'Cube', 'DataGrid', 'DataGrid.DataCell', 'Application'):
    if k in types:
        v = types[k]
        print(f"{k} ({v['kind']}) parents={v['parents']} methods={len(v['methods'])}: " +
              ', '.join(f"{x['name']}({x['params']}) -> {x['returns']}" for x in v['methods'][:4]))
