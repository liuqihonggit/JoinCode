import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build4.log')
ROOT = Path(r'D:\project\w1')

target_files = set()
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\(\d+,\d+\):\s*error\s+(CS\d+|JCC\d+)', line)
    if m:
        target_files.add(m.group(1))

sig_re = re.compile(r'(public|protected|internal|private)?\s*(override\s+|new\s+)?(async\s+)?ValueTask\s+Dispose(Async)?\s*\(')

def find_method_range(lines):
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
                    return i, brace_start, j
            break
    return None, None, None

total = 0
for fname in sorted(target_files):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches:
        continue
    fpath = matches[0]
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    changed = False

    s, bs, e = find_method_range(new)
    if s is None:
        continue

    if 'async ' in new[s]:
        new[s] = new[s].replace('async ', '', 1)
        changed = True

    for i in range(bs, e):
        ln = new[i]
        indent = len(ln) - len(ln.lstrip())
        ind = ln[:indent]

        if re.match(r'\s*await\s+ValueTask\.CompletedTask', ln):
            new[i] = ''
            changed = True
            continue

        m = re.match(r'\s*await\s+(.+?)\.ConfigureAwait\(false\);\s*$', ln)
        if not m:
            m = re.match(r'\s*await\s+(.+?);\s*$', ln)
        if m:
            expr = m.group(1).strip()
            new[i] = f'{ind}_ = {expr};'
            changed = True
            continue

    last_content = e - 1
    while last_content > bs and new[last_content].strip() == '':
        last_content -= 1
    last_line = new[last_content].strip()
    if not last_line.startswith('return'):
        indent_str = '        '
        if new[e].strip() == '}':
            new.insert(e, f'{indent_str}return ValueTask.CompletedTask;')
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
