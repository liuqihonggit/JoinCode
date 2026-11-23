#!/usr/bin/env python3
"""注册顺序表双向对账 — resources/orders.yaml 与源码实现必须完全一致。

这道门禁守的是「顺序必须显式设计」这条纪律：
  1. 每个接口的实现类都必须在 orders.yaml 对应列表里登记（漏登记 → 启动期直接抛异常）；
  2. orders.yaml 里登记的每个名字都必须有真实实现类（登记了不存在的名字 → 顺序表在骗人）；
  3. 实现类里的位次必须来自 OrderIndex.Default.PriorityOf / OrderOf，
     不允许再出现字面量数字（那等于把「先后」又藏回代码里）；
  4. 列表顺序必须与迁移前的执行顺序基线一致 —— 迁移只改表达方式，不改行为。

用法: python non_deliverables_tools/scripts/Verify-RegistrationOrder.py
退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import os
import pathlib
import re

REPO = pathlib.Path(__file__).resolve().parents[2]
ORDERS_YAML = REPO / "resources" / "orders.yaml"

# 列表名 → (接口全名, 实现类必须出现的源码目录)
LISTS: dict[str, tuple[str, tuple[str, ...]]] = {
    "command_guards": ("ICommandGuard", ("modules/guard/guard_hooks/execution/interception/guards",)),
    "command_interceptors": ("ICommandInterceptor", ()),
    "tool_fix_hooks": ("IToolFixHook", ("modules/guard/guard_hooks/execution/fix_hooks",)),
    "agent_tool_middlewares": ("IAgentToolMiddleware", ("modules/cognition/hands_core/tool_handlers/system_tools/agent",)),
    "query_stop_hooks": ("IQueryStopHook", ()),
}

# 迁移前的执行顺序基线 —— 来自旧 Priority / Order 数字。
# 迁移只改「顺序写在哪」，不改「顺序是什么」；基线不一致即行为漂移。
BASELINE: dict[str, list[str]] = {
    "command_guards": [
        "GitCommitGuard",      # 1000
        "DriveProtectionGuard",  # 900
        "CmdIndirectCallGuard",  # 800
        "RobocopyMirrorGuard",  # 500
        "HeredocGuard",       # 200
        "GhPrBodyGuard",      # 100
        "GhTimeoutGuard",     # 50
        "VpnRouteGuard",      # 30
    ],
    "tool_fix_hooks": [
        "GhPrBodyFixHook",    # 100
        "JsonFixHook",        # 80
        "GhTimeoutFixHook",   # 60
    ],
    "agent_tool_middlewares": [
        "AgentValidationMiddleware",       # Order 100
        "AgentForkMiddleware",             # 200
        "AgentBackgroundSpawnMiddleware",  # 300
        "AgentStreamExecutionMiddleware",  # 400
        "AgentHandoffMiddleware",          # 500
    ],
}

LITERAL_NUMBER = re.compile(r"public\s+int\s+(?:Priority|Order)\s*=>\s*\d+\s*;")
DERIVED = re.compile(r"OrderIndex\.Default\.(?:PriorityOf|OrderOf)\(\s*\"(?P<list>\w+)\"")


def parse_orders() -> dict[str, list[str]]:
    """解析 orders.yaml 的 lists 段（顺序即执行顺序）。"""
    lists: dict[str, list[str]] = {}
    current: str | None = None
    for raw in ORDERS_YAML.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("lists:"):
            current = None
            continue
        if line.startswith("- "):
            if current is None:
                continue
            name = line[2:].strip().strip('"')
            if name and name not in lists[current]:
                lists[current].append(name)
            continue
        if line.endswith("[]"):
            # 空列表写成流式序列：command_interceptors: []
            current = line[:-2].strip().rstrip(":").strip()
            lists.setdefault(current, [])
            continue
        if line.endswith(":"):
            current = line[:-1].strip()
            lists.setdefault(current, [])
    return lists


SKIP_DIRECTORIES = frozenset({".git", ".xxx", ".nuget", "artifacts", "bin", "obj", "node_modules", "test", "tests"})


def is_production_directory(name: str) -> bool:
    """在遍历前剪枝构建产物及测试目录，避免把测试替身登记为生产实现。"""
    lower = name.lower()
    return lower not in SKIP_DIRECTORIES and not lower.endswith((".tests", "_tests", "_test"))


def scan_sources(declared: dict[str, list[str]]) -> tuple[dict[str, dict[str, pathlib.Path]], list[str]]:
    """每个 C# 文件只读取一次，同时收集实现和位次违规。"""
    found: dict[str, dict[str, pathlib.Path]] = {}
    patterns: dict[str, re.Pattern[str]] = {}
    for list_name, (interface, _) in LISTS.items():
        if list_name in declared and interface not in patterns:
            patterns[interface] = re.compile(rf"class\s+(\w+)[^{{]*\b{re.escape(interface)}\b")
            found[interface] = {}

    scopes = {REPO / directory: [] for _, dirs in LISTS.values() for directory in dirs}
    for directory, children, files in os.walk(REPO):
        children[:] = [name for name in children if is_production_directory(name)]
        root = pathlib.Path(directory)
        scope = next((parent for parent in (root, *root.parents) if parent in scopes), None)
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = root / name
            try:
                text = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                if scope is not None:
                    scopes[scope].append(f"无法读取位次检查源码: {path.relative_to(REPO).as_posix()}")
                continue
            for interface, pattern in patterns.items():
                for match in pattern.finditer(text):
                    found[interface].setdefault(match.group(1), path)
            if scope is not None:
                scopes[scope].extend(order_diagnostics(path, text, declared))

    return found, [problem for problems in scopes.values() for problem in problems]


def order_diagnostics(path: pathlib.Path, text: str, declared: dict[str, list[str]]) -> list[str]:
    """从已读取的源码校验位次与引用列表。"""
    relative = path.relative_to(REPO).as_posix()
    problems = []
    if LITERAL_NUMBER.search(text):
        problems.append(f"{relative} 仍写死了位次数字")
    for match in DERIVED.finditer(text):
        used = match.group("list")
        if used not in declared:
            problems.append(f"{relative} 引用了不存在的列表 '{used}'")
    return problems


def main() -> int:
    if not ORDERS_YAML.is_file():
        print(f"缺少注册顺序表: {ORDERS_YAML}")
        return 1

    declared = parse_orders()
    implementations, order_problems = scan_sources(declared)
    problems: list[str] = []

    for list_name, (interface, dirs) in LISTS.items():
        if list_name not in declared:
            problems.append(f"orders.yaml 缺少列表 '{list_name}'")
            continue
        registered = declared[list_name]
        impls = implementations[interface]
        # 只认源码目录内的实现（测试替身不在生产顺序表里）
        # 未声明目录范围的列表（如 query_stop_hooks）用全仓实现做对账 —— 否则 scoped 恒为空，
        # 「登记了不存在的实现」必然误报，等于这类列表永远填不进去（TASK126 实测踩到）。
        scoped = {name: path for name, path in impls.items()
                  if any(d in path.as_posix() for d in dirs)} if dirs else impls

        missing = sorted(set(scoped) - set(registered))
        if missing:
            problems.append(f"列表 '{list_name}' 漏登记实现: {missing}")
        unknown = sorted(set(registered) - set(scoped))
        if unknown:
            problems.append(f"列表 '{list_name}' 登记了不存在的实现: {unknown}")

        # 基线：顺序必须与迁移前一致
        baseline = BASELINE.get(list_name)
        if baseline is not None and registered != baseline:
            problems.append(
                f"列表 '{list_name}' 顺序与迁移前基线不一致:\n"
                f"      期望 {baseline}\n      实际 {registered}")

    problems.extend(order_problems)

    total = sum(len(v) for v in declared.values())
    print(f"注册顺序表 {len(declared)} 个列表 / {total} 项")
    if problems:
        for problem in problems:
            print("  -", problem)
        return 1
    print("orders.yaml 与源码实现双向一致，位次全部由表派生，无字面量数字。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
