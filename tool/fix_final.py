import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build10.log')
ROOT = Path(r'D:\project\w1')

target_files = set()
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\(', line)
    if m and ('JCC9200' in line or 'JCC3001' in line or 'CS0126' in line or 'CS4032' in line or 'CS0161' in line):
        target_files.add(m.group(1))

sig_re = re.compile(r'(public|protected|internal|private)?\s*(override\s+|new\s+)?(async\s+)?(ValueTask|Task)\s+Dispose(Async)?\s*\(')

def find_methods(lines):
    results = []
    for i, ln in enumerate(lines):
        if sig_re.search(ln):
            for j in range(i, len(lines)):
                if '{' in lines[j]: bs = j; break
            else: continue
            depth = 0
            for j in range(bs, len(lines)):
                depth += lines[j].count('{') - lines[j].count('}')
                if depth == 0:
                    results.append((i, bs, j))
                    break
    return results

total = 0
for fname in sorted(target_files):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches: continue
    fpath = matches[0]
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    changed = False

    for s, bs, e in find_methods(new):
        is_valuetask = 'ValueTask' in new[s]
        ret_val = 'ValueTask.CompletedTask' if is_valuetask else 'Task.CompletedTask'
        if 'async ' in new[s]:
            new[s] = new[s].replace('async ', '', 1)
            changed = True

        for i in range(bs, e):
            ln = new[i]
            indent = len(ln) - len(ln.lstrip())
            ind = ln[:indent]

            if re.match(r'\s*await\s+ValueTask\.CompletedTask', ln):
                new[i] = ''; changed = True; continue

            m = re.match(r'\s*await\s+(.+?)\.ConfigureAwait\((?:false|true)\);\s*$', ln)
            if not m: m = re.match(r'\s*await\s+(.+?);\s*$', ln)
            if m:
                new[i] = f'{ind}_ = {m.group(1).strip()};'
                changed = True; continue

            mm = re.match(r'\s*_\s*=\s*(\w+)\(\);\s*$', ln)
            if mm:
                new[i] = f'{ind}_ = {mm.group(1)}(CancellationToken.None);'
                changed = True; continue

            if re.search(r'\breturn\s*;', ln) and 'ValueTask' not in ln and 'Task' not in ln:
                new[i] = ln.replace('return;', f'return {ret_val};')
                changed = True

        last_content = e - 1
        while last_content > bs and new[last_content].strip() == '': last_content -= 1
        if new[last_content].strip() and not new[last_content].strip().startswith('return'):
            if new[e].strip() == '}':
                new.insert(e, f'        return {ret_val};')
                changed = True

    if changed:
        diff = list(difflib.unified_diff(orig, new, fromfile=fname, tofile=fname, lineterm=''))
        if diff:
            print('\n'.join(diff)); print(); total += 1
            if not DRY: fpath.write_text('\n'.join(new) + '\n', encoding='utf-8')

mode = 'DRY-RUN' if DRY else 'APPLIED'
print(f'=== {mode}: {total} files ===')
