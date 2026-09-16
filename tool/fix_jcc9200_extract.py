import re
import os
from pathlib import Path

errors = {}
for line in open('/tmp/jcc_build2.log', encoding='utf-8', errors='replace'):
    m = re.search(r'([^\\]+\.cs)\((\d+),(\d+)\).*JCC9200', line)
    if m:
        fname = m.group(1)
        lineno = int(m.group(2))
        errors.setdefault(fname, []).append(lineno)

root = Path('D:/project/w1')
report = []
for fname, lines in sorted(errors.items()):
    matches = list(root.rglob(fname))
    matches = [m for m in matches if '/obj/' not in str(m) and '/bin/' not in str(m) and '/artifacts/' not in str(m)]
    if not matches:
        report.append(f'### {fname} — NOT FOUND')
        continue
    fpath = matches[0]
    code = fpath.read_text(encoding='utf-8').splitlines()
    lines_sorted = sorted(set(lines))
    report.append(f'### {fname}  ({fpath})')
    report.append(f'   errors at lines: {lines_sorted}')
    for ln in lines_sorted:
        start = max(0, ln - 15)
        end = min(len(code), ln + 20)
        report.append(f'  --- context around line {ln} ---')
        for i in range(start, end):
            marker = ' >>>' if (i + 1) == ln else '    '
            report.append(f'{marker}{i+1:5d}: {code[i]}')
        report.append('')
    report.append('')

out = Path('D:/project/w1/tool/jcc9200_report.txt')
out.write_text('\n'.join(report), encoding='utf-8')
print(f'Report written to {out}, {len(errors)} files, {sum(len(v) for v in errors.values())} errors')
