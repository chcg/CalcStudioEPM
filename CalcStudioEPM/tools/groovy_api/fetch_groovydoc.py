"""Downloads the class pages linked from Oracle's EPM Groovy index-all.html (sequentially)."""
import os, re, sys, time, urllib.request

doc = sys.argv[1]                       # groovydoc folder holding index-all.html
base = 'https://docs.oracle.com/en/cloud/saas/enterprise-performance-management-common/groov/'
index = open(os.path.join(doc, 'index-all.html'), encoding='utf-8').read()
pages = sorted(set(p for p in re.findall(r'href="(oracle/[^"#]+\.html)"', index) if not p.endswith('package-summary.html')))
out = os.path.join(doc, 'pages')
os.makedirs(out, exist_ok=True)
for p in pages:
    target = os.path.join(out, p.replace('/', '_'))
    if os.path.exists(target):
        continue
    with urllib.request.urlopen(base + p, timeout=60) as r:
        open(target, 'wb').write(r.read())
    time.sleep(0.2)
print(len(pages), 'pages in', out)
