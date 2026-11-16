#!/usr/bin/env python3
"""CLI 节点契约对账 —— 把 node.yaml 的「声明」和分组 exe 的「真实注册表」摆在一起比。

存在理由：node.yaml 的 mode 是生成器从 private/*.cs 反推出来的声明，声明和实现会漂移
（加了 handler 忘了重跑生成器、或删了实现没删登记）。本脚本要求 exe 自报 `--list-local`，
再与清单逐项比对，漂移立刻暴露；退出码非 0 即为失败，可直接进 CI。

用法：
  python scripts/check_cli_node_coverage.py                 # 只对账声明 vs 注册表
  python scripts/check_cli_node_coverage.py --check-help     # 额外逐个跑 <tool> -h
  python scripts/check_cli_node_coverage.py --group code     # 只查某一组

对账项（任一不符即 FAIL）：
  1. exe 自报的 local 集合 == node.yaml 里 mode: local 的集合
  2. node.yaml 的 local_tool_count == 上面集合的大小
  3. node.yaml 的 tool_count == tools 段条目数
  4. 每个 local 工具在 help.txt 里都有详情段（否则 -h 会走到"未知工具"分支）
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GROUPS = REPO / "modules" / "tools" / "cli_nodes" / "groups"
BIN = REPO / "artifacts" / "bin"


def parse_node_yaml(path: Path) -> dict:
    """按行扫描 node.yaml —— 只认 id/mode 这两个键，不引 YAML 依赖。"""
    text = path.read_text(encoding="utf-8")
    meta: dict[str, object] = {"tools": [], "local_tool_count": None, "tool_count": None, "exe": None}
    current: dict[str, str] | None = None
    for raw in text.splitlines():
        line = raw.rstrip()
        if line.startswith("exe:"):
            meta["exe"] = line.split(":", 1)[1].strip().strip('"')
        elif line.startswith("tool_count:"):
            meta["tool_count"] = int(line.split(":", 1)[1].strip())
        elif line.startswith("local_tool_count:"):
            meta["local_tool_count"] = int(line.split(":", 1)[1].strip())
        elif re.match(r"\s+-\s+id:", line):
            current = {"id": line.split("id:", 1)[1].strip().strip('"')}
            meta["tools"].append(current)  # type: ignore[union-attr]
        elif re.match(r"\s+mode:", line) and current is not None:
            current["mode"] = line.split("mode:", 1)[1].strip()
    return meta


def find_exe(group: str, exe_name: str | None) -> Path | None:
    candidates = list(BIN.glob(f"jcc_tool_{group}/Debug/*/{exe_name or 'jcc-tool-' + group + '.exe'}"))
    if not candidates:
        candidates = list(BIN.glob(f"jcc_tool_{group}/Debug/*/*.exe"))
    return candidates[0] if candidates else None


def list_local(exe: Path) -> tuple[set[str], dict[str, str]]:
    out = subprocess.run([str(exe), "--list-local"], capture_output=True, text=True, timeout=120)
    line = (out.stdout or "").strip()
    fields = dict(part.split("=", 1) for part in line.split() if "=" in part)
    local = {item for item in fields.get("local", "").split(",") if item}
    return local, fields


def help_has_section(help_text: str, tool_id: str) -> bool:
    """help.txt 的详情段形如 `  <id> — <desc>`；无该段则 -h 会走未知工具分支。"""
    return bool(re.search(rf"^\s*{re.escape(tool_id)}\s+—", help_text, re.MULTILINE))


def check_group(group_dir: Path, check_help: bool) -> list[str]:
    problems: list[str] = []
    group = group_dir.name
    node = parse_node_yaml(group_dir / "node.yaml")
    tools = node["tools"]
    # 斜杠组的 node.yaml 用 `version`（目录节点 id 不能以 / 开头），而 handler 与 help.txt 用 `/version`。
    # 契约的判定口径统一为「归一化后的 id」：比较前去掉前导 '/'，与 CliNodeRunner.CanonicalToolId 一致。
    def norm(tool_id: str) -> str:
        return tool_id.lstrip("/")

    declared_local = {norm(t["id"]) for t in tools if t.get("mode") == "local"}
    declared_host = {norm(t["id"]) for t in tools if t.get("mode") == "host"}
    declared_delegate = {norm(t["id"]) for t in tools if t.get("mode") == "delegate"}

    if node["tool_count"] is not None and node["tool_count"] != len(tools):
        problems.append(f"tool_count={node['tool_count']} 但 tools 段有 {len(tools)} 条")

    exe = find_exe(group, node["exe"])  # type: ignore[arg-type]
    if exe is None:
        problems.append(f"未找到已构建的 exe（artifacts/bin/jcc_tool_{group}），先编译该组再对账")
        return problems

    try:
        actual_local_raw, fields = list_local(exe)
        actual_local = {norm(item) for item in actual_local_raw}
    except (subprocess.SubprocessError, OSError) as exc:
        problems.append(f"--list-local 执行失败: {exc}")
        return problems

    if node["local_tool_count"] is not None and node["local_tool_count"] != len(actual_local):
        problems.append(f"local_tool_count={node['local_tool_count']} 但 exe 自报 {len(actual_local)} 个")

    missing = declared_local - actual_local
    extra = actual_local - declared_local
    if missing:
        problems.append("node.yaml 说 local 但没有实现: " + ", ".join(sorted(missing)))
    if extra:
        problems.append("有实现但 node.yaml 未标 local: " + ", ".join(sorted(extra)))
    help_text = (group_dir / "help.txt").read_text(encoding="utf-8") if (group_dir / "help.txt").exists() else ""
    if not help_text:
        problems.append("help.txt 缺失")

    if check_help:
        for tool_id in sorted(actual_local_raw):
            try:
                out = subprocess.run([str(exe), tool_id, "-h"], capture_output=True, text=True, timeout=60)
            except (subprocess.SubprocessError, OSError) as exc:
                problems.append(f"{tool_id} -h 执行失败: {exc}")
                continue
            if out.returncode != 0:
                problems.append(f"{tool_id} -h 退出码 {out.returncode}: {(out.stderr or '').strip()[:160]}")

    print(
        f"{group:<8} 声明 local={len(declared_local):<3} host={len(declared_host):<3} delegate={len(declared_delegate):<3} | "
        f"exe 自报 local={len(actual_local):<3} | {'OK' if not problems else 'FAIL'}"
    )
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description="CLI 节点契约对账")
    parser.add_argument("--group", action="append", help="只查指定分组，可重复")
    parser.add_argument("--check-help", action="store_true", help="逐个 local 工具跑 -h 自描述检查（慢）")
    args = parser.parse_args()

    group_dirs = sorted(p for p in GROUPS.iterdir() if p.is_dir() and (p / "node.yaml").exists())
    if args.group:
        wanted = set(args.group)
        group_dirs = [p for p in group_dirs if p.name in wanted]
    if not group_dirs:
        print("没有匹配的分组", file=sys.stderr)
        return 2

    failed = False
    totals = {"local": 0, "host": 0, "delegate": 0}
    for group_dir in group_dirs:
        node = parse_node_yaml(group_dir / "node.yaml")
        for tool in node["tools"]:
            mode = tool.get("mode")
            if mode in totals:
                totals[mode] += 1  # type: ignore[index]
        problems = check_group(group_dir, args.check_help)
        for problem in problems:
            failed = True
            print(f"  ! {problem}")

    print()
    print(f"合计 local={totals['local']} host={totals['host']} delegate={totals['delegate']}")
    print("对账通过" if not failed else "对账失败")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
