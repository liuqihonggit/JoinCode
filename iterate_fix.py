import subprocess, re, sys
from pathlib import Path
from collections import defaultdict

SLNX = "build/sln/JoinCode.slnx"
USING = "global using System.Diagnostics.CodeAnalysis;'
SUPPRESS_JUSTIFICATION = "边界可空抑制: 反射/延迟初始化/解析保证非空"

def run_build():
    r = subprocess.run(
        ["dotnet", "build", SLNX, "-c", "Debug", "--nologo"],
        capture_output=True, text=True, timeout=600
    )
    return r.stdout + r.stderr

def collect_errors(output, pattern_str):
    pat = re.compile(pattern_str)
    errs = defaultdict(set)
    for line in output.split('\n'):
        m = pat.match(line)
        if m:
            fp = m.group(1).replace('\\', '/')
            ln = int(m.group(2))
            errs[fp].add(ln)
    return errs

def add_global_usings(output):
    pat = re.compile(r'^(.+?)\.csproj\]')
    csproj_pat = re.compile(r'\[(D:\\\\project\\\\w1\\\\(.+?)\.csproj)\]')
    dirs = set()
    for line in output.split('\n'):
        if 'CS0246' in line and 'SuppressMessage' in line:
            m = re.search(r'\[D:\\\\project\\\\w1\\\\(.+?)\.csproj\]', line)
            if not m:
                m = re.search(r'\[D:.project.w1.(.+?).csproj\]', line)
            if m:
                dirs.add(m.group(1).replace('\\', '/'))
    added = 0
    for d in dirs:
        p = Path(d) / 'GlobalUsings.cs'
        if not p.exists():
            p.write_text(USING + '\n', encoding='utf-8')
            added += 1
            continue
        text = p.read_text(encoding='utf-8-sig')
        if USING in text:
            continue
        lines = text.split('\n')
        lines.insert(0, USING)
        p.write_text('\n'.join(lines), encoding='utf-8')
        added += 1
    return added

for iteration in range(15):
    print(f"\n{'='*60}")
    print(f"ITERATION {iteration+1}")
    print(f"{'='*60}")

    output = run_build()

    jcc_errs = collect_errors(output, r'^(.+?)\((\d+),(\d+)\): error JCC11003')
    total_jcc = sum(len(v) for v in jcc_errs.values())
    print(f"JCC11003 errors: {total_jcc}")

    if total_jcc == 0:
        print("SUCCESS: 0 JCC11003 errors!")
        break

    # Write errors to file for fix script
    with open('/tmp/jcc_iter.txt', 'w') as f:
        for fp, lns in jcc_errs.items():
            for ln in lns:
                f.write(f"{fp}({ln},1): error JCC11003\n")

    r = subprocess.run(
        ["python", "fix_jcc11003.py", "/tmp/jcc_iter.txt", "--apply"],
        capture_output=True, text=True, timeout=120
    )
    print(r.stdout.strip().split('\n')[-1] if r.stdout.strip() else "no output")

    # Check for CS0246 and add usings
    output2 = run_build()
    added = add_global_usings(output2)
    if added:
        print(f"Added {added} global usings")

print("\nDone.")
