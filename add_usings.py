import subprocess, re, sys
from pathlib import Path

USING = 'global using System.Diagnostics.CodeAnalysis;'

r = subprocess.run(["dotnet", "build", "build/sln/JoinCode.slnx", "-c", "Debug", "--nologo"],
                   capture_output=True, text=True, timeout=600)
output = r.stdout + r.stderr

dirs = set()
for line in output.split('\n'):
    if 'CS0246' in line and 'SuppressMessage' in line:
        m = re.search(r'\[D:\\project\\w1\\(.+?)\.csproj\]', line)
        if m:
            dirs.add(m.group(1).replace('\\', '/'))

added = 0
for d in sorted(dirs):
    p = Path(d).parent / 'GlobalUsings.cs'
    if not p.exists():
        p.write_text(USING + '\n', encoding='utf-8')
        added += 1
        print(f'CREATED: {d}')
        continue
    text = p.read_text(encoding='utf-8-sig')
    if USING in text:
        continue
    lines = text.split('\n')
    lines.insert(0, USING)
    p.write_text('\n'.join(lines), encoding='utf-8')
    added += 1
    print(f'ADDED: {d}')

print(f'\nTotal: {added} usings added across {len(dirs)} projects')
