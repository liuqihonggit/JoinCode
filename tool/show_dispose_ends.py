import re
from pathlib import Path

LOG = Path(r'C:\Users\54076\AppData\Local\Temp\jcc_build4.log')
ROOT = Path(r'D:\project\w1')

target_files = set()
for line in LOG.read_text(encoding='utf-8', errors='replace').splitlines():
    if 'CS0161' in line and 'DisposeAsync' in line:
        m = re.search(r'([^\\]+\.cs)\(', line)
        if m:
            target_files.add(m.group(1))

sig_re = re.compile(r'(public|protected|internal|private)?\s*(override\s+|new\s+)?(async\s+)?ValueTask\s+Dispose(Async)?\s*\(')

for fname in sorted(target_files):
    matches = [p for p in ROOT.rglob(fname) if '/obj/' not in str(p) and '/bin/' not in str(p)]
    if not matches:
        continue
    fpath = matches[0]
    lines = fpath.read_text(encoding='utf-8').splitlines()
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
                    print(f'=== {fname} (method at line {i+1}, ends at line {j+1}) ===')
                    for k in range(max(brace_start, j - 8), j + 1):
                        print(f'  {k+1:5d}: {lines[k]}')
                    print()
                    break
            break
