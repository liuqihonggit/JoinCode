"""
批量推广 AskAwait 死锁检测 — 预览模式
1. 检查每个文件是否继承 ActorBase
2. 对 ActorBase 子类,把裸 await xxx.Task.ConfigureAwait(false) 改为 await AskAwait(xxx, ct)
3. 非 ActorBase 子类报告(需其他方案)
"""
import os
import re
import sys
import hashlib

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
actorbase_pattern = re.compile(r':\s*ActorBase\b')

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

def rel(p):
    return p.replace("D:\\project\\w1\\", "").replace("\\", "/")

def main():
    execute = "--execute" in sys.argv
    files = scan_files()

    actor_files = []      # ActorBase 子类,有裸 Ask
    non_actor_files = []  # 非 ActorBase 子类,有裸 Ask

    for path in files:
        try:
            with open(path, "r", encoding="utf-8-sig") as fh:
                content = fh.read()
        except Exception:
            continue

        is_actor = bool(actorbase_pattern.search(content))

        lines = content.splitlines(keepends=True)
        hits = []
        for i, line in enumerate(lines, 1):
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("///"):
                continue
            if askawait_pattern.search(line):
                continue
            m = ask_pattern.search(line)
            if m:
                hits.append((i, m.group(1), stripped[:120]))

        if not hits:
            continue

        if is_actor:
            actor_files.append((path, hits))
        else:
            non_actor_files.append((path, hits))

    print(f"=== ActorBase 子类 (可用 AskAwait): {len(actor_files)} 文件 ===")
    total_actor = 0
    for path, hits in actor_files:
        print(f"\n  {rel(path)} ({len(hits)}处)")
        for line_no, var, line in hits:
            print(f"    L{line_no} [{var}]: {line[:80]}")
        total_actor += len(hits)
    print(f"\n  ActorBase 子类总计: {total_actor} 处")

    print(f"\n=== 非 ActorBase 子类 (需其他方案): {len(non_actor_files)} 文件 ===")
    total_non = 0
    for path, hits in non_actor_files:
        print(f"\n  {rel(path)} ({len(hits)}处)")
        for line_no, var, line in hits:
            print(f"    L{line_no} [{var}]: {line[:80]}")
        total_non += len(hits)
    print(f"\n  非 ActorBase 子类总计: {total_non} 处")

    if not execute:
        print(f"\n[预览模式] 加 --execute 对 ActorBase 子类执行替换")
        return

    # 执行替换:只对 ActorBase 子类,变量名 tcs → AskAwait(tcs, ct)
    # 特殊变量名 remainingTcs/registerTcs 也处理
    total_replaced = 0
    for path, hits in actor_files:
        with open(path, "r", encoding="utf-8-sig") as fh:
            content = fh.read()
        original_hash = hashlib.md5(content.encode("utf-8")).hexdigest()

        for line_no, var, _ in hits:
            old = f"await {var}.Task.ConfigureAwait(false)"
            new = f"await AskAwait({var}, ct)"
            content = content.replace(old, new)

        new_hash = hashlib.md5(content.encode("utf-8")).hexdigest()
        if original_hash != new_hash:
            with open(path, "w", encoding="utf-8-sig") as fh:
                fh.write(content)
            total_replaced += len(hits)
            print(f"  [已替换] {rel(path)}: {len(hits)}处  hash {original_hash[:8]}→{new_hash[:8]}")
        else:
            print(f"  [未变化] {rel(path)}")

    print(f"\n[执行完成] 替换 {total_replaced} 处")

if __name__ == "__main__":
    main()
