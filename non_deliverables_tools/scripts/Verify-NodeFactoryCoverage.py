#!/usr/bin/env python3
"""真机校验「节点声明 ↔ 运行期工厂来源」的分类一致性 — 启动 jcc 读结构化 dump。

背景（TASK100 S8 / §24）：节点工厂有两份来源，二者边界此前没有机器可判的标记：

  · **显式登记**（`DataDrivenPipelineComposition.BuildLocalNodeFactories()`）——
    恰好覆盖**实例型**节点（middleware / guard），它们要由运行期解析器造出实例。
  · **编译期 catalog**（`[NodeFactory]` 标注 → 生成物）——
    恰好覆盖**声明型**节点（provider / role / capability / command-group），
    它们由消费点自己读表判定，本来就不该有 DI 实例。

断裂点 F1：新节点若两处都漏登记，运行期 `ResolveMiddlewares` 过去只记一条 Warning 就
**静默跳过**该中间件 —— 在安全域等于「少一道守卫」。本门禁把这类漂移变成 CI 红。

本门禁只做结构校验，不做语义判断：
  1. 每个被 profile 声明的节点都必须有工厂来源（既无显式登记也无 catalog → FAIL）
  2. 实例型节点（middleware / guard）的来源必须是「显式登记」或「两处都有」
     —— 只有 catalog 兜底的实例型节点可运行，但必须被列出（提示其工厂是
     `GetRequiredService<T>()`，DI 未注册时会在运行期 fail-fast）
  3. 声明型节点（provider / role / capability / command-group）必须在 catalog，
     且**不得**出现在显式登记里 —— 出现即两份真相（FAIL）

用法: python non_deliverables_tools/scripts/Verify-NodeFactoryCoverage.py [jcc.exe 路径]
退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import json
import pathlib
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
JCC = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

# 契约 → 是否实例型（需要运行期解析器造实例）。
# 与 §24.1 的分类保持一致：middleware / guard = 实例型，其余 = 声明型。
INSTANCE_CONTRACTS = {"middleware", "guard"}
DECLARED_CONTRACTS = {"provider", "role", "capability", "command-group"}


def fail(message: str) -> None:
    """打印失败原因并以退出码 1 结束 — 与既有门禁脚本保持同一口径。"""
    print(f"[FAIL] {message}")
    sys.exit(1)


def main() -> int:
    if not JCC.exists():
        fail(f"找不到 jcc 可执行文件: {JCC}（先构建 modules/apps/cli/JoinCode.csproj）")

    proc = subprocess.run(
        [str(JCC), "--dump-nodes", "--json"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if proc.returncode != 0:
        fail(f"jcc --dump-nodes --json 退出码 {proc.returncode}\n{proc.stderr[-2000:]}")

    try:
        report = json.loads(proc.stdout)
    except json.JSONDecodeError as ex:
        fail(f"dump 输出不是合法 JSON: {ex}\n输出前 500 字: {proc.stdout[:500]}")

    sources = report.get("factorySources")
    if not isinstance(sources, dict) or not sources:
        fail("dump 输出里没有 factorySources —— jcc 版本太旧（该字段由 TASK100 S8 引入）")

    # 收集每个 (consumerId, nodeId) 的契约（跨 profile 去重）。
    contract_of: dict[tuple[str, str], str] = {}
    for consumer in report.get("consumers") or []:
        cid = consumer.get("consumerId", "")
        contract = consumer.get("contract", "")
        for node in consumer.get("nodes") or []:
            contract_of.setdefault((cid, node.get("id", "")), contract)

    problems: list[str] = []
    catalog_only_instances: list[str] = []
    stats: dict[str, dict[str, int]] = {}

    for key, contract in sorted(contract_of.items()):
        label = f"{key[0]}:{key[1]}"
        source = sources.get(label)
        if source is None:
            problems.append(f"{label} —— 被 profile 声明，但既无显式登记也无 catalog 工厂（运行期会静默跳过）")
            continue

        bucket = stats.setdefault(contract, {})
        bucket[source] = bucket.get(source, 0) + 1

        if contract in INSTANCE_CONTRACTS:
            if source == "catalog":
                # 运行期能解析（catalog 兜底），但工厂是 GetRequiredService<T>() 语义，
                # 与显式登记的参数化工厂不等价 —— 列出而不是放过。
                catalog_only_instances.append(label)
        elif contract in DECLARED_CONTRACTS:
            if source != "catalog":
                problems.append(
                    f"{label} —— 声明型节点（contract={contract}）却登记在显式清单里，"
                    "两份真相：声明型节点应由 catalog 提供元数据，不进 DI 工厂清单"
                )
        else:
            problems.append(f"{label} —— 未知契约 '{contract}'")

    print("═══ 节点工厂来源分类 ═══")
    for contract in sorted(stats):
        detail = ", ".join(f"{k}={v}" for k, v in sorted(stats[contract].items()))
        kind = "实例型" if contract in INSTANCE_CONTRACTS else "声明型"
        print(f"  {contract:14s}（{kind}）: {detail}")
    print(f"  合计: {len(contract_of)} 个节点 / {len(sources)} 个工厂键")

    if catalog_only_instances:
        print("── 仅靠 catalog 兜底的实例型节点（运行期可解析，但工厂语义为 GetRequiredService）──")
        for label in catalog_only_instances:
            print(f"  · {label}")

    if problems:
        print()
        for problem in problems:
            print(f"[FAIL] {problem}")
        return 1

    print("  [OK] 每个声明节点都有工厂来源，且实例型 / 声明型与来源一一对应")
    return 0


if __name__ == "__main__":
    sys.exit(main())
