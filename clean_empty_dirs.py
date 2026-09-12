#!/usr/bin/env python3
"""
清理空文件夹脚本。

安全策略：
  - 默认 dry-run，仅展示将删除的目录，不实际删除
  - 需显式 --execute 才真正执行删除
  - 后序递归（自底向上）：先处理子目录，子目录变空后父目录也可能被清理
  - dry-run 同样模拟级联删除效果，预览结果与 execute 一致
  - 排除列表中的目录及其子树不会被检查、不会被删除
  - 根目录本身永远不会被删除

用法:
  python clean_empty_dirs.py <target_dir>                # 预览（dry-run）
  python clean_empty_dirs.py <target_dir> --execute      # 实际删除
  python clean_empty_dirs.py <target_dir> --exclude .git --exclude .xxx
  python clean_empty_dirs.py <target_dir> --execute --verbose

退出码:
  0 = 正常结束
  1 = 参数错误 / 目标不存在
  2 = 执行删除时发生错误
"""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass, field


DEFAULT_EXCLUDES: tuple[str, ...] = (".git", ".xxx", ".svn", ".hg", "node_modules")


@dataclass
class CleanResult:
    scanned: int = 0
    empty_found: int = 0
    deleted: int = 0
    skipped_excluded: int = 0
    errors: list[str] = field(default_factory=list)


def clean_dir(
    path: str,
    excludes: tuple[str, ...],
    result: CleanResult,
    execute: bool,
    verbose: bool,
) -> bool:
    """
    后序递归清理。返回 True 表示此目录在清理后为空（已被删或将被删）。
    根目录调用方不应根据返回值删除根目录本身。
    """
    if not os.path.isdir(path):
        return False

    result.scanned += 1

    try:
        names = os.listdir(path)
    except OSError as e:
        result.errors.append(f"{path}: {e}")
        return False

    any_surviving = False
    for name in names:
        child = os.path.join(path, name)
        # 跳过符号链接（不递归进入，视为存活内容）
        if os.path.islink(child):
            any_surviving = True
            continue
        if os.path.isdir(child):
            if name in excludes:
                result.skipped_excluded += 1
                any_surviving = True
                continue
            child_became_empty = clean_dir(child, excludes, result, execute, verbose)
            if not child_became_empty:
                any_surviving = True
        else:
            any_surviving = True

    if any_surviving:
        return False

    # 此目录清理后为空
    result.empty_found += 1
    if execute:
        try:
            os.rmdir(path)
            result.deleted += 1
            if verbose:
                print(f"[DELETED] {path}")
        except OSError as e:
            result.errors.append(f"{path}: {e}")
            return False
    else:
        if verbose:
            print(f"[EMPTY] {path}")
    return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="clean_empty_dirs",
        description="清理空文件夹（默认 dry-run，需 --execute 才真删）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("target", help="要清理的目标目录")
    parser.add_argument(
        "--execute",
        action="store_true",
        help="实际执行删除（默认仅预览）",
    )
    parser.add_argument(
        "--exclude",
        action="append",
        default=[],
        help=f"排除的目录名（可多次指定，默认: {list(DEFAULT_EXCLUDES)}）",
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="详细输出每个空目录",
    )
    args = parser.parse_args(argv)

    target = os.path.abspath(args.target)
    if not os.path.isdir(target):
        print(f"错误: 目标目录不存在: {target}", file=sys.stderr)
        return 1

    excludes = tuple(args.exclude) if args.exclude else DEFAULT_EXCLUDES

    result = CleanResult()
    # 对根目录的每个子项递归处理，根目录本身不删除
    try:
        root_names = os.listdir(target)
    except OSError as e:
        print(f"错误: 无法读取目标目录: {e}", file=sys.stderr)
        return 1

    for name in root_names:
        child = os.path.join(target, name)
        if os.path.islink(child):
            continue
        if os.path.isdir(child):
            if name in excludes:
                result.skipped_excluded += 1
                continue
            clean_dir(child, excludes, result, args.execute, args.verbose)

    mode = "EXECUTE" if args.execute else "DRY-RUN"
    print(f"== 清理空文件夹 [{mode}] ==")
    print(f"目标: {target}")
    print(f"排除: {list(excludes)}")
    print(f"扫描目录数: {result.scanned}")
    print(f"跳过(排除): {result.skipped_excluded}")
    print(f"发现空目录: {result.empty_found}")
    if args.execute:
        print(f"已删除: {result.deleted}")
    else:
        print(f"将删除: {result.empty_found}（加 --execute 实际执行）")

    if result.errors:
        print(f"错误数: {len(result.errors)}", file=sys.stderr)
        for e in result.errors:
            print(f"  - {e}", file=sys.stderr)
        return 2

    if result.empty_found == 0:
        print("没有空目录需要处理。")

    return 0


if __name__ == "__main__":
    sys.exit(main())
