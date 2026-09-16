import re
from pathlib import Path

LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build11.log')
ROOT = Path(r'D:\project\w1')

target_files = set()
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    m = re.search(r'([^\\]+\.cs)\((\d+),', line)
    if m and ('JCC9200' in line or 'CS4032' in line):
        target_files.add(m.group(1))

for fname in sorted(target_files):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches: continue
    fpath = matches[0]
    lines = fpath.read_text(encoding='utf-8').splitlines()
    print(f'=== {fname} ===')
    for i, ln in enumerate(lines):
        if 'DisposeAsync' in ln and ('public' in ln or 'override' in ln or 'async' in ln or 'ValueTask' in ln or 'Task' in ln):
            print(f'  sig {i+1}: {ln.strip()}')
        if 'await' in ln and i < len(lines):
            for line2 in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
                if fname in line2 and f'({i+1},' in line2 and 'JCC9200' in line2:
                    print(f'  await {i+1}: {ln.strip()}')
                    break
    print()
