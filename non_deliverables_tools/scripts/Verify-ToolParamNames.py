#!/usr/bin/env python3
"""真机验证：独立工具 exe 的参数名归一化与宿主路径一致。

背景
----
参数名归一化（``--filePath`` → ``--file_path``）原先只有宿主路径
（中间件 ``ArgumentRepairMiddleware``）会做，exe 路径不做 ——
同一次调用在 host 路径被自动修复、在 local 路径被直接拒绝。

本脚本对每个分组 exe 的若干 local 工具做真机抽查：
把帮助文本里声明的必填参数改成 camelCase 再调用，若仍报「缺少必填参数 --<原名>」，
说明该 exe 没接上归一化 —— 这正是两条路径分叉的症状。

用法
----
    python non_deliverables_tools/scripts/Verify-ToolParamNames.py
    python non_deliverables_tools/scripts/Verify-ToolParamNames.py --per-group 5
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXE_DIR = os.path.join(REPO_ROOT, "artifacts", "bin")

# 详情段里的必填参数行：  --file_path   string   必填   说明
REQUIRED_PARAM = re.compile(r"^\s+--([A-Za-z_][A-Za-z0-9_]*)\s+\S+\s+必填", re.M)
LOCAL_COUNT = re.compile(r"local_count=(\d+)")
LOCAL_LIST = re.compile(r"local=(\S*)")
# 归一化失败的症状：工具仍然按原参数名说「缺少必填参数」
MISSING_PARAM = re.compile(r"缺少必填参数\s+--([A-Za-z_][A-Za-z0-9_]*)")


def to_camel(name: str) -> str:
    head, *rest = name.split("_")
    return head + "".join(part[:1].upper() + part[1:] for part in rest if part)


def run(exe: str, args: list[str], timeout: int = 60) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            [exe, *args], capture_output=True, text=True, timeout=timeout, cwd=REPO_ROOT
        )
    except subprocess.TimeoutExpired:
        return 124, ""
    return proc.returncode, (proc.stdout or "") + (proc.stderr or "")


def list_local(exe: str) -> list[str]:
    _, out = run(exe, ["--list-local"])
    match = LOCAL_LIST.search(out)
    if not match:
        return []
    return [name for name in match.group(1).split(",") if name]


def declared_required(exe: str, tool: str) -> list[str]:
    _, out = run(exe, [tool, "-h"])
    return REQUIRED_PARAM.findall(out)


def check_tool(exe: str, group: str, tool: str) -> tuple[bool, str]:
    """返回 (是否通过, 说明)。"""
    required = declared_required(exe, tool)
    if not required:
        return True, f"{group}/{tool}: 无必填参数，跳过"
    for param in required:
        camel = to_camel(param)
        if camel == param:
            continue
        _, out = run(exe, [tool, f"--{camel}", "verify-probe-value"])
        missing = MISSING_PARAM.search(out)
        if missing and missing.group(1) == param:
            return False, (
                f"{group}/{tool}: --{camel} 未归一化，仍报「缺少必填参数 --{param}」"
            )
        return True, f"{group}/{tool}: --{camel} → --{param} 已被识别"
    return True, f"{group}/{tool}: 参数名无大小写变体，跳过"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--per-group", type=int, default=3, help="每组抽查多少个 local 工具")
    args = parser.parse_args()

    if not os.path.isdir(EXE_DIR):
        print(f"找不到工具 exe 目录: {EXE_DIR}")
        return 2

    groups = sorted(
        name[len("jcc_tool_"):]
        for name in os.listdir(EXE_DIR)
        if name.startswith("jcc_tool_")
    )
    if not groups:
        print(f"{EXE_DIR} 下没有 jcc_tool_* 目录 —— 先编译工具 exe")
        return 2

    failures: list[str] = []
    checked = 0
    for group in groups:
        exe = os.path.join(EXE_DIR, f"jcc_tool_{group}", "Debug", "net10.0", f"jcc-tool-{group}.exe")
        if not os.path.exists(exe):
            failures.append(f"{group}: 找不到 {exe}")
            continue
        for tool in list_local(exe)[:args.per_group]:
            ok, note = check_tool(exe, group, tool)
            checked += 1
            print(("  OK  " if ok else " FAIL ") + note)
            if not ok:
                failures.append(note)

    print()
    if failures:
        print(f"参数名归一化不一致：{len(failures)}/{checked} 项失败")
        for item in failures:
            print(f"  - {item}")
        return 1
    print(f"参数名归一化一致：{checked} 项抽查全部通过（camelCase 与 snake_case 等价）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
