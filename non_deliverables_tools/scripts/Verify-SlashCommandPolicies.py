#!/usr/bin/env python3
"""斜杠命令源码与生成物/权限策略对账 — ADR 0168「有参无权/有权无参编译期拦截」。

只读检查，不写任何文件：
  1. [ChatCommand]/[ChatCommandArg] 解析结果与内嵌 help.txt / node.yaml 完全一致（陈旧即红灯）；
  2. slash.yaml 的工具集合与命令集合一致；
  3. 每个工具的策略参数集合等于 [ChatCommandArg] 声明集合（双向，避免有参无权与幽灵权限）；
  4. slash.yaml executables 同时包含 jcc-tool-slash.exe 与宿主 jcc.exe。

修复方式：先运行 scripts/generate_slash_cli_node.py 刷新 help/node，
再补 resources/tools/policies/slash.yaml 参数并 Generate-CliToolPolicies.py --trust-only。
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts"))

import generate_slash_cli_node as gen  # noqa: E402

SLASH_DIR = gen.OUT_DIR
POLICY_PATH = REPO_ROOT / "resources/tools/policies/slash.yaml"
REQUIRED_EXES = {"jcc-tool-slash.exe", "jcc.exe"}


def unquote(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
        return value[1:-1].replace('\\"', '"').replace("\\\\", "\\")
    return value


def parse_policy(path: Path) -> tuple[dict[str, list[str]], list[str]]:
    tools: dict[str, list[str]] = {}
    executables: list[str] = []
    current: str | None = None
    in_parameters = False
    section = ""
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.rstrip()
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if not line.startswith(" "):
            section = stripped.rstrip(":")
            current = None
            in_parameters = False
            continue
        if section == "executables" and stripped.startswith("- "):
            executables.append(stripped[2:].strip())
            continue
        if section != "tools":
            continue
        if line.startswith("  ") and not line.startswith("    ") and stripped.endswith(":"):
            current = unquote(stripped[:-1])
            tools[current] = []
            in_parameters = False
            continue
        if current is None:
            continue
        if stripped == "parameters: []":
            in_parameters = False
            continue
        if stripped == "parameters:":
            in_parameters = True
            continue
        if in_parameters and line.startswith("      ") and stripped.endswith(":"):
            tools[current].append(unquote(stripped[:-1]))
            continue
        if not line.startswith("      "):
            in_parameters = False
    return tools, executables


def main() -> int:
    commands = gen.scan_modules((REPO_ROOT / "modules"), gen.load_command_names())
    problems: list[str] = []

    help_path = SLASH_DIR / "help.txt"
    if not help_path.exists():
        problems.append(f"缺少生成物: {help_path.relative_to(REPO_ROOT).as_posix()}")
    elif help_path.read_text(encoding="utf-8") != gen.render_help(commands):
        problems.append("help.txt 与 [ChatCommand]/[ChatCommandArg] 不一致（运行 scripts/generate_slash_cli_node.py 刷新）")

    node_path = SLASH_DIR / "node.yaml"
    if not node_path.exists():
        problems.append(f"缺少生成物: {node_path.relative_to(REPO_ROOT).as_posix()}")
    elif node_path.read_text(encoding="utf-8") != gen.render_node_yaml(commands, gen.collect_local_ids(SLASH_DIR)):
        problems.append("node.yaml 与 [ChatCommand] 不一致（运行 scripts/generate_slash_cli_node.py 刷新）")

    policy_path = POLICY_PATH
    if not policy_path.exists():
        problems.append(f"缺少策略: {policy_path.relative_to(REPO_ROOT).as_posix()}")
        policy_tools, executables = {}, []
    else:
        policy_tools, executables = parse_policy(policy_path)

    if not REQUIRED_EXES.issubset({exe for exe in executables}):
        problems.append(f"slash.yaml executables 缺少宿主镜像（需包含 {sorted(REQUIRED_EXES)}，实际 {executables}）")

    declared = {f"/{cmd['name']}": {arg["name"] for arg in cmd["args"]} for cmd in commands}
    missing_tools = sorted(set(declared) - set(policy_tools))
    extra_tools = sorted(set(policy_tools) - set(declared))
    if missing_tools:
        problems.append(f"策略缺少命令: {missing_tools}")
    if extra_tools:
        problems.append(f"策略存在源码未声明的命令: {extra_tools}")

    for name in sorted(set(declared) & set(policy_tools)):
        declared_args = declared[name]
        policy_args = set(policy_tools[name])
        if declared_args - policy_args:
            problems.append(f"{name} 策略缺少参数（有参无权）: {sorted(declared_args - policy_args)}")
        if policy_args - declared_args:
            problems.append(f"{name} 策略多出参数（有权无参）: {sorted(policy_args - declared_args)}")

    if problems:
        print(f"[FAIL] 斜杠命令对账失败（{len(problems)} 项）:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print(f"[OK] 斜杠命令对账通过: {len(declared)} 个命令 / "
          f"{sum(len(args) for args in declared.values())} 个参数，help/node/策略与源码一致")
    return 0


if __name__ == "__main__":
    sys.exit(main())
