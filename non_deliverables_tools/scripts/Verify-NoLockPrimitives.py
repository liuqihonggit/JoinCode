#!/usr/bin/env python3
"""JCC4008 门禁：显式锁原语基线只减不增。

扫描所有生产/测试 C# 源文件（排除生成器自身、工具脚本与构建产物），统计
lock/Monitor/信号量/互斥量/读写锁/项目锁生态类型的出现次数，与
non_deliverables_tools/lock_gate/lock-primitive-baseline.txt 逐文件对账。

- 新增文件或次数增加 -> 失败（禁止把锁带回来）
- 次数减少但基线未同步 -> 失败（提示运行 --write-baseline，保持基线诚实）
- 基线条目已归零 -> 失败（必须从基线删除，避免僵尸豁免）

用法:
    python non_deliverables_tools/scripts/Verify-NoLockPrimitives.py
    python non_deliverables_tools/scripts/Verify-NoLockPrimitives.py --write-baseline
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

SKIP_DIRS = {
    ".git",
    ".nuget",
    ".qoder",
    ".vs",
    ".xxx",
    "artifacts",
    "bin",
    "node_modules",
    "obj",
    "modules/generators",
    "non_deliverables_tools",
}

LOCK_PATTERNS = (
    re.compile(r"\block\s*\("),
    re.compile(r"\bMonitor\.(?:Enter|TryEnter|Exit|Pulse|PulseAll|Wait)\s*\("),
    re.compile(
        r"\b(?:SemaphoreSlim|Semaphore|Mutex|ReaderWriterLockSlim|SpinLock|"
        r"ManualResetEventSlim|ManualResetEvent|AutoResetEvent|CountdownEvent|Barrier|"
        r"AsyncLock|AsyncLockedDictionary|LockRef|LockRegistry|LockTimeoutPolicy|"
        r"TimeoutLock|OrderedLockManager)\b"
    ),
    re.compile(r"\bSystem\.Threading\.Lock\b"),
    re.compile(
        r"^\s*(?:(?:private|protected|internal|public|static|readonly|volatile|required|new)\s+)+"
        r"Lock\s+[A-Za-z_]",
        re.MULTILINE,
    ),
)


def strip_comments_and_strings(source: str) -> str:
    """去掉注释与字符串内容，避免文档/日志里提到锁名造成误报。"""
    out: list[str] = []
    i = 0
    length = len(source)
    state = "normal"

    while i < length:
        ch = source[i]
        nxt = source[i + 1] if i + 1 < length else ""

        if state == "normal":
            if ch == "/" and nxt == "/":
                state = "line_comment"
                out.append("  ")
                i += 2
                continue
            if ch == "/" and nxt == "*":
                state = "block_comment"
                out.append("  ")
                i += 2
                continue
            if ch == "@" and nxt == '"':
                state = "verbatim_string"
                out.append("  ")
                i += 2
                continue
            if ch == '"':
                state = "string"
                out.append(" ")
                i += 1
                continue
            if ch == "'":
                state = "char"
                out.append(" ")
                i += 1
                continue
            out.append(ch)
            i += 1
            continue

        if state == "line_comment":
            if ch == "\n":
                state = "normal"
                out.append("\n")
            else:
                out.append(" ")
            i += 1
            continue

        if state == "block_comment":
            if ch == "*" and nxt == "/":
                state = "normal"
                out.append("  ")
                i += 2
                continue
            out.append("\n" if ch == "\n" else " ")
            i += 1
            continue

        if state == "verbatim_string":
            if ch == '"' and nxt == '"':
                out.append("  ")
                i += 2
                continue
            if ch == '"':
                state = "normal"
                out.append(" ")
                i += 1
                continue
            out.append("\n" if ch == "\n" else " ")
            i += 1
            continue

        if state in {"string", "char"}:
            if ch == "\\":
                out.append("  ")
                i += 2
                continue
            closing = '"' if state == "string" else "'"
            if ch == closing:
                state = "normal"
                out.append(" ")
                i += 1
                continue
            out.append("\n" if ch == "\n" else " ")
            i += 1
            continue

    return "".join(out)


def count_locks(source: str) -> int:
    cleaned = strip_comments_and_strings(source)
    return sum(len(pattern.findall(cleaned)) for pattern in LOCK_PATTERNS)


def is_skipped(path: Path, repo_root: Path) -> bool:
    rel = path.relative_to(repo_root).as_posix()
    return any(rel == skip or rel.startswith(skip + "/") for skip in SKIP_DIRS)


def scan(repo_root: Path) -> dict[str, int]:
    counts: dict[str, int] = {}
    for path in repo_root.rglob("*.cs"):
        if is_skipped(path, repo_root):
            continue
        count = count_locks(path.read_text(encoding="utf-8", errors="replace"))
        if count:
            counts[path.relative_to(repo_root).as_posix()] = count
    return counts


def read_baseline(path: Path) -> dict[str, int]:
    baseline: dict[str, int] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "|" not in line:
            raise ValueError(f"基线行缺少 |count: {line}")
        file_path, raw_count = line.rsplit("|", 1)
        baseline[file_path.strip().replace("\\", "/")] = int(raw_count.strip())
    return baseline


def write_baseline(path: Path, counts: dict[str, int], repo_root: Path) -> None:
    # |0 是分析器专用豁免（间接消费锁类型，文本扫描不可见），重写时必须保留；
    # 文件已删除或重新出现文本级锁原语时以当前扫描结果为准。
    preserved = {
        file_path: 0
        for file_path, allowed in (read_baseline(path).items() if path.exists() else [])
        if allowed == 0 and file_path not in counts and (repo_root / file_path).exists()
    }
    merged = {**counts, **preserved}
    lines = [
        "# JCC4008 锁原语存量基线（只减不增，B6 归档锁生态时清零）",
        "# 格式: 仓库相对路径|允许出现的最大次数（|0 = 仅分析器拦截的间接消费豁免）",
        "# 更新: python non_deliverables_tools/scripts/Verify-NoLockPrimitives.py --write-baseline",
        "",
    ]
    lines.extend(f"{file_path}|{count}" for file_path, count in sorted(merged.items()))
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="JCC4008 锁原语基线门禁")
    parser.add_argument("--write-baseline", action="store_true", help="按当前源码重写基线")
    parser.add_argument(
        "--baseline",
        default=str(Path("non_deliverables_tools/lock_gate/lock-primitive-baseline.txt")),
        help="基线文件路径",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[2]
    baseline_path = (repo_root / args.baseline).resolve()
    actual = scan(repo_root)

    if args.write_baseline:
        write_baseline(baseline_path, actual, repo_root)
        print(f"已写入基线: {baseline_path} ({len(actual)} 个文件, {sum(actual.values())} 处)")
        return 0

    baseline = read_baseline(baseline_path)
    failures: list[str] = []

    for file_path, count in sorted(actual.items()):
        allowed = baseline.get(file_path)
        if allowed is None:
            failures.append(f"新增锁原语: {file_path} ({count} 处)，不允许新增；请改用 Actor/Channel/CAS/原子文件租约")
        elif count > allowed:
            failures.append(f"锁原语增加: {file_path} 基线 {allowed} -> 实际 {count}")

    for file_path, allowed in sorted(baseline.items()):
        # |0 是分析器专用豁免（如间接消费 AsyncLock 属性，文本扫描看不见），B6 统一清零
        if allowed == 0:
            continue
        count = actual.get(file_path, 0)
        if count == 0:
            failures.append(f"基线僵尸条目: {file_path} 已无锁原语，请运行 --write-baseline 移除")
        elif count < allowed:
            failures.append(f"锁原语减少: {file_path} 基线 {allowed} -> 实际 {count}，请运行 --write-baseline 收紧基线")

    if failures:
        print("JCC4008 锁原语门禁失败:", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    print(f"JCC4008 锁原语门禁通过: {len(actual)} 个文件, {sum(actual.values())} 处存量")
    return 0


if __name__ == "__main__":
    sys.exit(main())
