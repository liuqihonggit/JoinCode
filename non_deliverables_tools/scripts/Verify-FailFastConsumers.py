#!/usr/bin/env python3
"""TASK100 S10 / §25.7 D6 — 锁住「实例型消费点必须显式开启 failFastOnMissing」。

背景：NodeRegistry.GetServicesForConsumerAsync<T> 在节点解析不到时，历史上只记一条
Warning 就丢掉。在 command-dispatch / prompt-assembly 这类**实例型**消费点上，
丢一个节点 = 整类斜杠命令消失 / 提示词整组段落不见 —— 症状与原因相隔很远。

修法给方法加了可选参数 failFastOnMissing（默认 false 保持兼容），由调用方按消费点性质
决定。但「由调用方决定」最怕的就是新增消费点时忘了传 —— 于是本门禁把这条约定固化：

  1. 生产代码里每一个 `.GetServicesForConsumerAsync<...>` 调用点都必须登记在
     REGISTERED_CALL_SITES 中（新增调用点 → 门禁红，要求先判定是否实例型）
  2. 登记为 True 的调用点，每次调用都必须显式写 `failFastOnMissing: true`
  3. 接口 + 实现都必须保留 `bool failFastOnMissing = false`（防默认值被改成 true
     而悄悄改变所有非实例型调用方的行为）
  4. 实现里必须真的有分支抛错（防开关被架空成空实现）

退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]

# 已登记的生产调用点 → 是否为实例型（必须 failFastOnMissing: true）。
# 判据：该消费点漏解析一个节点，会不会导致「整批功能静默消失」？
# 新增调用点时必须先回答这个问题，再把结论写进这张表。
REGISTERED_CALL_SITES: dict[str, bool] = {
    # S9：preprocess 两条链 —— 少一个中间件等于那条链静默少一环。
    "modules/composition/composition/dependency_injection/infrastructure/ServiceRegistration.Brain.cs": True,
    # S10 / F6：整类斜杠命令消失。
    "modules/apps/engine/commands/slash/SlashCommandRunner.cs": True,
    # S10 / F6：提示词整组段落消失（系统提示词悄悄变短）。
    "modules/cognition/brain.context_services/preprocess/SystemPromptMiddleware.cs": True,
}

INTERFACE_FILE = "modules/abstractions/composition/node/interfaces/INodeRegistry.cs"
IMPLEMENTATION_FILE = "modules/composition/composition/node_registry/core/NodeRegistry.cs"
REGRESSION_TEST = "modules/composition/composition.tests/node_registry/NodeRegistryFailFastTest.cs"

# 只匹配「实例方法调用」（前面有点号）；接口声明 / 类实现的方法定义不带点号，天然排除。
CALL_RE = re.compile(r"\.\s*GetServicesForConsumerAsync\s*<")
OPTIONAL_PARAM_RE = re.compile(r"bool\s+failFastOnMissing\s*=\s*false")
EXPLICIT_TRUE_RE = re.compile(r"failFastOnMissing\s*:\s*true")
THROW_RE = re.compile(r"if\s*\(\s*failFastOnMissing\s*\)[\s\S]{0,400}?throw\s+new\s+InvalidOperationException")

# 扫描窗口：一个调用的参数写太长也不能越过下一个调用，避免把别人的 true 算到自己头上。
WINDOW = 800


def read(relative: str) -> str | None:
    path = REPO / relative
    if not path.exists():
        return None
    return path.read_text(encoding="utf-8-sig")


def is_production(path: pathlib.Path) -> bool:
    """排除测试替身与归档目录 —— 它们的签名只需能编译，不参与生产约定。"""
    parts = {part.lower() for part in path.parts}
    if any(p in parts for p in {"tests", "test", ".xxx", "bin", "obj"}):
        return False
    if ".tests" in str(path).lower() or "test/" in str(path).lower().replace("\\", "/"):
        return False
    return True


def main() -> int:
    problems: list[str] = []

    # ── 1. 找出生产代码里所有调用点 ────────────────────────────────────────
    found: dict[str, int] = {}
    for path in (REPO / "modules").rglob("*.cs"):
        if not is_production(path):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        count = len(CALL_RE.findall(text))
        if count:
            found[path.relative_to(REPO).as_posix()] = count

    registered = set(REGISTERED_CALL_SITES)
    actual = set(found)
    for extra in sorted(actual - registered):
        problems.append(
            f"未登记的生产调用点: {extra} —— 新增 GetServicesForConsumerAsync 调用必须先在 "
            "Verify-FailFastConsumers.py 的 REGISTERED_CALL_SITES 判定是否实例型"
        )
    for stale in sorted(registered - actual):
        problems.append(f"登记表中的调用点已不存在: {stale}（文件改名或调用删除？请同步登记表）")

    print("═══ 实例型消费点 fail-fast 约定 ═══")
    for relative in sorted(actual | registered):
        required = REGISTERED_CALL_SITES.get(relative)
        status = "实例型/必须 true" if required else ("已登记/兼容默认" if required is False else "未登记")
        print(f"  {relative}: 调用 {found.get(relative, 0)} 处 — {status}")

    # ── 2. 每个实例型调用都要显式传 true ──────────────────────────────────
    for relative, required in REGISTERED_CALL_SITES.items():
        if not required:
            continue
        text = read(relative)
        if text is None:
            problems.append(f"实例型调用点文件缺失: {relative}")
            continue
        matches = list(CALL_RE.finditer(text))
        for index, match in enumerate(matches):
            next_start = matches[index + 1].start() if index + 1 < len(matches) else len(text)
            window = text[match.end(): min(match.end() + WINDOW, next_start)]
            if not EXPLICIT_TRUE_RE.search(window):
                problems.append(
                    f"{relative} 第 {index + 1} 处调用未显式传 failFastOnMissing: true —— "
                    "实例型消费点漏一个节点会静默丢掉整批功能"
                )

    # ── 3. 接口 / 实现签名与开关实现 ──────────────────────────────────────
    for relative in (INTERFACE_FILE, IMPLEMENTATION_FILE):
        text = read(relative)
        if text is None:
            problems.append(f"签名文件缺失: {relative}")
        elif not OPTIONAL_PARAM_RE.search(text):
            problems.append(
                f"{relative} 缺少 `bool failFastOnMissing = false` —— "
                "默认值必须是 false，否则所有非实例型调用方的历史行为会被悄悄改变"
            )

    impl = read(IMPLEMENTATION_FILE)
    if impl is not None and not THROW_RE.search(impl):
        problems.append(f"{IMPLEMENTATION_FILE} 里找不到 failFastOnMissing 的抛错分支（开关被架空？）")

    if read(REGRESSION_TEST) is None:
        problems.append(f"回归测试缺失: {REGRESSION_TEST}")
    else:
        print(f"  [OK] 回归测试存在: {pathlib.Path(REGRESSION_TEST).name}")

    if problems:
        print()
        for problem in problems:
            print(f"[FAIL] {problem}")
        return 1

    print(f"  [OK] {len(actual)} 个生产调用点全部登记，实例型均显式 fail-fast，接口/实现签名一致")
    return 0


if __name__ == "__main__":
    sys.exit(main())
