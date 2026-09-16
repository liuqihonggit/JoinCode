"""
Ask/Tell 模式扫描脚本 — 预览模式
扫描 ActorBase.SendAsync/TrySend 引用 + Ask 模式(await tcs.Task)位置
"""
import os
import re
import hashlib
from collections import defaultdict

SCAN_DIRS = [
    r"D:\project\w1\lib",
    r"D:\project\w1\kit",
    r"D:\project\w1\llm",
    r"D:\project\w1\server",
    r"D:\project\w1\app",
]

SKIP_DIRS = {"obj", "bin", ".vs", ".git"}

def scan_files():
    files = []
    for scan_dir in SCAN_DIRS:
        if not os.path.isdir(scan_dir):
            continue
        for root, dirs, fnames in os.walk(scan_dir):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for f in fnames:
                if f.endswith(".cs"):
                    files.append(os.path.join(root, f))
    return files

def scan_symbols(files):
    tell_refs = defaultdict(list)   # SendAsync / TrySend 引用 (Tell 语义)
    ask_refs = defaultdict(list)    # await tcs.Task / await xxx.Task.ConfigureAwait (Ask 语义)
    send_def = []                   # SendAsync/TrySend 方法定义

    tell_pattern = re.compile(r'\b(SendAsync|TrySend)\b')
    ask_pattern = re.compile(r'await\s+\w+\.Task\b')
    def_pattern = re.compile(r'(public|protected|internal|private).*\b(SendAsync|TrySend)\s*\(')

    for path in files:
        try:
            with open(path, "r", encoding="utf-8-sig") as fh:
                lines = fh.readlines()
        except Exception:
            continue
        for i, line in enumerate(lines, 1):
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("///"):
                continue
            if def_pattern.search(line):
                send_def.append((path, i, stripped[:120]))
            for m in tell_pattern.finditer(line):
                sym = m.group(1)
                if def_pattern.search(line):
                    continue
                tell_refs[sym].append((path, i, stripped[:120]))
            if ask_pattern.search(line):
                ask_refs["await xxx.Task"].append((path, i, stripped[:120]))

    return tell_refs, ask_refs, send_def

def rel(p):
    return p.replace("D:\\project\\w1\\", "").replace("\\", "/")

def main():
    files = scan_files()
    print(f"扫描 .cs 文件数: {len(files)}")

    tell_refs, ask_refs, send_def = scan_symbols(files)

    print(f"\n{'='*70}")
    print(f"Tell 语义方法定义 (SendAsync/TrySend 声明): {len(send_def)} 处")
    print(f"{'='*70}")
    for path, line_no, line in send_def:
        print(f"  {rel(path)}:{line_no}")
        print(f"    {line}")

    print(f"\n{'='*70}")
    print("Tell 语义引用 (SendAsync/TrySend 调用方)")
    print(f"{'='*70}")
    for sym, hits in tell_refs.items():
        print(f"\n  {sym}: {len(hits)} 处")
        for path, line_no, line in hits[:15]:
            print(f"    {rel(path)}:{line_no}: {line[:90]}")
        if len(hits) > 15:
            print(f"    ... 还有 {len(hits)-15} 处")

    print(f"\n{'='*70}")
    print("Ask 语义引用 (await xxx.Task — 发消息等回复)")
    print(f"{'='*70}")
    for sym, hits in ask_refs.items():
        print(f"\n  {sym}: {len(hits)} 处")
        for path, line_no, line in hits[:20]:
            print(f"    {rel(path)}:{line_no}: {line[:90]}")
        if len(hits) > 20:
            print(f"    ... 还有 {len(hits)-20} 处")

    total_tell = sum(len(v) for v in tell_refs.values())
    total_ask = sum(len(v) for v in ask_refs.values())
    print(f"\n{'='*70}")
    print(f"汇总: Tell引用={total_tell}  Ask引用={total_ask}  方法定义={len(send_def)}")
    print(f"{'='*70}")

if __name__ == "__main__":
    main()
