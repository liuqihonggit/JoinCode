"""
扫描裸 await xxx.Task.ConfigureAwait(false) — 未用 AskAwait 的 Ask 模式
预览需要推广 AskAwait 死锁检测的范围
"""
import os
import re

SCAN_DIRS = [
    r"D:\project\w1\lib",
    r"D:\project\w1\kit",
    r"D:\project\w1\llm",
    r"D:\project\w1\server",
    r"D:\project\w1\app",
]

SKIP_DIRS = {"obj", "bin", ".vs", ".git"}

ask_pattern = re.compile(r'await\s+(\w+)\.Task\b\.ConfigureAwait\(false\)')
askawait_pattern = re.compile(r'\bAskAwait\b')

def scan():
    results = []
    for scan_dir in SCAN_DIRS:
        if not os.path.isdir(scan_dir):
            continue
        for root, dirs, fnames in os.walk(scan_dir):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for f in fnames:
                if not f.endswith(".cs"):
                    continue
                path = os.path.join(root, f)
                try:
                    with open(path, "r", encoding="utf-8-sig") as fh:
                        lines = fh.readlines()
                except Exception:
                    continue
                for i, line in enumerate(lines, 1):
                    stripped = line.strip()
                    if stripped.startswith("//") or stripped.startswith("///"):
                        continue
                    if askawait_pattern.search(line):
                        continue
                    m = ask_pattern.search(line)
                    if m:
                        results.append((path, i, stripped[:120], m.group(1)))
    return results

def rel(p):
    return p.replace("D:\\project\\w1\\", "").replace("\\", "/")

def main():
    results = scan()
    print(f"裸 await xxx.Task.ConfigureAwait(false) (未用 AskAwait): {len(results)} 处")
    print()

    by_file = {}
    for path, line_no, line, var in results:
        by_file.setdefault(path, []).append((line_no, var, line))

    for path in sorted(by_file.keys()):
        hits = by_file[path]
        print(f"  {rel(path)} ({len(hits)}处)")
        for line_no, var, line in hits:
            print(f"    L{line_no} [{var}]: {line[:80]}")

    print(f"\n总计: {len(results)} 处, {len(by_file)} 个文件")

if __name__ == "__main__":
    main()
