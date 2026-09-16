import re, sys, difflib
from pathlib import Path

DRY = '--apply' not in sys.argv
LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build7.log')
ROOT = Path(r'D:\project\w1')

target_files = set()
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\(', line)
    if m and ('CS1997' in line or 'CS0127' in line or 'CS1501' in line or 'CS0126' in line):
        target_files.add(m.group(1))

total = 0
for fname in sorted(target_files):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches: continue
    fpath = matches[0]
    orig = fpath.read_text(encoding='utf-8').splitlines()
    new = list(orig)
    changed = False

    for i in range(len(new)):
        ln = new[i]
        if 'return ValueTask.CompletedTask;' in ln:
            indent = len(ln) - len(ln.lstrip())
            ind = ln[:indent]
            stripped = ln.strip()
            if stripped == 'return ValueTask.CompletedTask;':
                new[i] = f'{ind}return;'
                changed = True
            elif stripped.startswith('if') and 'return ValueTask.CompletedTask;' in stripped:
                new[i] = ln.replace('return ValueTask.CompletedTask;', 'return;')
                changed = True

        if 'DisposeAsync(CancellationToken.None)' in ln:
            new[i] = ln.replace('DisposeAsync(CancellationToken.None)', 'DisposeAsync()')
            changed = True

        m = re.match(r'\s*_\s*=\s*(\w+)\.(\w+)\(CancellationToken\.None\);\s*$', ln)
        if m and m.group(2) not in ('StopAsync', 'StopMonitoringAsync', 'DisconnectAsync', 'ShutdownAsync'):
            indent = len(ln) - len(ln.lstrip())
            new[i] = f'{indent*" "}_ = {m.group(1)}.{m.group(2)}();'
            changed = True

    if changed:
        diff = list(difflib.unified_diff(orig, new, fromfile=fname, tofile=fname, lineterm=''))
        if diff:
            print('\n'.join(diff)); print(); total += 1
            if not DRY: fpath.write_text('\n'.join(new) + '\n', encoding='utf-8')

mode = 'DRY-RUN' if DRY else 'APPLIED'
print(f'=== {mode}: {total} files ===')
