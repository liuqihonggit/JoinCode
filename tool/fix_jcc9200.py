import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build2.log')
ROOT = Path(r'D:\project\w1')

errors = {}
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\((\d+),(\d+)\).*JCC9200', line)
    if m:
        errors.setdefault(m.group(1), []).append(int(m.group(2)))

def find_method_range(lines, sig_re):
    for i, ln in enumerate(lines):
        if sig_re.search(ln):
            brace_start = None
            for j in range(i, len(lines)):
                if '{' in lines[j]:
                    brace_start = j
                    break
            if brace_start is None:
                continue
            depth = 0
            for j in range(brace_start, len(lines)):
                depth += lines[j].count('{') - lines[j].count('}')
                if depth == 0:
                    return i, j
            break
    return None, None

def is_in_try_catch(lines, idx, method_start, method_end):
    for i in range(method_start, idx):
        if re.search(r'\btry\b', lines[i]):
            for j in range(i + 1, idx):
                if re.search(r'\bcatch\b', lines[j]):
                    return True
    return False

def transform(lines, start, end):
    out = list(lines)
    changed = []
    skipped = []
    brace_end = end
    for i in range(start, brace_end):
        ln = out[i]
        indent = len(ln) - len(ln.lstrip())
        ind = ln[:indent]
        if re.match(r'\s*await\s+ValueTask\.CompletedTask.*;\s*$', ln):
            out[i] = ''
            changed.append(i + 1)
            continue
        m = re.match(r'\s*await\s+(.+?)\.ConfigureAwait\(false\);\s*$', ln)
        if not m:
            m = re.match(r'\s*await\s+(.+?);\s*$', ln)
        if m:
            expr = m.group(1).strip()
            if is_in_try_catch(out, i, start, brace_end):
                skipped.append(i + 1)
                continue
            out[i] = f'{ind}_ = {expr};'
            changed.append(i + 1)
    sig = out[start]
    if 'async ' in sig:
        out[start] = sig.replace('async ', '', 1)
        changed.append(start + 1)
    is_valuetask = 'ValueTask' in sig
    if is_valuetask:
        ret_re = re.compile(r'\s*return;\s*$')
        has_real_return = any(re.search(r'\breturn\b', out[i]) and not ret_re.match(out[i]) for i in range(start, brace_end))
        has_void_return = False
        for i in range(start, brace_end):
            if ret_re.match(out[i]):
                indent = len(out[i]) - len(out[i].lstrip())
                out[i] = out[i][:indent] + 'return ValueTask.CompletedTask;'
                changed.append(i + 1)
                has_void_return = True
        if not has_real_return and not has_void_return:
            indent_str = '        '
            insert_line = f'{indent_str}return ValueTask.CompletedTask;'
            out.insert(brace_end, insert_line)
            changed.append(brace_end + 1)
    return out, changed, skipped

total_changed = 0
total_skipped = 0
for fname in sorted(errors):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches:
        continue
    fpath = matches[0]
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    file_changed = False
    file_skipped = []
    for sig_re in [re.compile(r'(public|protected|internal|private)?\s*(override\s+)?(async\s+)?(ValueTask|Task|void)\s+Dispose(Async)?\s*\(')]:
        s, e = find_method_range(new, sig_re)
        if s is None:
            continue
        new, changed, skipped = transform(new, s, e)
        if changed:
            file_changed = True
        file_skipped.extend(skipped)
    if file_changed or file_skipped:
        if file_changed:
            diff = list(difflib.unified_diff(orig, new, fromfile=fname, tofile=fname, lineterm=''))
            if diff:
                print('\n'.join(diff))
                print()
                total_changed += 1
                if not DRY:
                    fpath.write_text('\n'.join(new) + '\n', encoding='utf-8')
        if file_skipped:
            print(f'  ⚠ {fname}: skipped try-catch await at lines {file_skipped} (needs manual fix)')
            total_skipped += len(file_skipped)

mode = 'DRY-RUN (preview)' if DRY else 'APPLIED'
print(f'=== {mode}: {total_changed} files changed, {total_skipped} try-catch awaits skipped ===')
