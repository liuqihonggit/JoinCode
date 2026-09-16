import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build3.log')
ROOT = Path(r'D:\project\w1')

cs_errors = {}
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\((\d+),(\d+)\):\s*error\s+(CS\d+|JCC\d+)', line)
    if m:
        fname, lineno, code = m.group(1), int(m.group(2)), m.group(3)
        cs_errors.setdefault(fname, set()).add((lineno, code))

def find_method_range(lines, sig_re):
    for i, ln in enumerate(lines):
        if sig_re.search(ln):
            for j in range(i, len(lines)):
                if '{' in lines[j]:
                    brace_start = j
                    break
            else:
                continue
            depth = 0
            for j in range(brace_start, len(lines)):
                depth += lines[j].count('{') - lines[j].count('}')
                if depth == 0:
                    return i, j
            break
    return None, None

sig_re = re.compile(r'(public|protected|internal|private)?\s*(override\s+|new\s+)?(async\s+)?ValueTask\s+Dispose(Async)?\s*\(')

total = 0
for fname in sorted(cs_errors):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches:
        continue
    fpath = matches[0]
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    changed = False

    s, e = find_method_range(new, sig_re)
    if s is None:
        continue

    for i in range(s, e + 1):
        ln = new[i]
        indent = len(ln) - len(ln.lstrip())
        ind = ln[:indent]

        m = re.search(r'\breturn\s*;', ln)
        if m and 'ValueTask' not in ln and 'return ValueTask' not in ln:
            new[i] = ln.replace('return;', 'return ValueTask.CompletedTask;')
            changed = True

        m = re.match(r'\s*await\s+(.+?)\.ConfigureAwait\(false\);\s*$', ln)
        if not m:
            m = re.match(r'\s*await\s+(.+?);\s*$', ln)
        if m:
            expr = m.group(1).strip()
            new[i] = f'{ind}_ = {expr};'
            changed = True

        if re.match(r'\s*await\s+ValueTask\.CompletedTask.*;\s*$', ln):
            new[i] = ''
            changed = True

    if changed:
        diff = list(difflib.unified_diff(orig, new, fromfile=fname, tofile=fname, lineterm=''))
        if diff:
            print('\n'.join(diff))
            print()
            total += 1
            if not DRY:
                fpath.write_text('\n'.join(new) + '\n', encoding='utf-8')

mode = 'DRY-RUN' if DRY else 'APPLIED'
print(f'=== {mode}: {total} files ===')
