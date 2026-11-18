#!/usr/bin/env python3
"""真机校验「节点表 ↔ 工厂 catalog ↔ 契约」三方一致 — 启动 jcc 读结构化 dump。

背景（TASK100）：节点体系与真实装配是两套事实，此前只能人工比对。
`tool-dispatch` 4+1 条声明既无工厂也无消费方，长期无人发现，就是漏网标本。

本门禁只做「结构一致」校验，不做语义判断：
  1. `jcc --dump-nodes --json` 的 verification 三个数组必须全空
     （声明无工厂 / 工厂无声明 / 互斥契约内重复）
  2. 每个消费点的 contract 必须是合法值
  3. resources/profiles/*.yaml 每个文件都必须出现在 dump 里
     （漏加 EmbeddedResource 会导致 profile 静默不生效 — 这是最容易犯的错）
  4. nodeCount / consumerCount 非零

用法: python non_deliverables_tools/scripts/Verify-NodeConsistency.py [jcc.exe 路径]
退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import json
import pathlib
import re
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
JCC = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

VALID_CONTRACTS = {"middleware", "guard", "provider", "role", "capability", "command-group"}
PROFILE_DIR = REPO / "resources/profiles"


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

    # 1. 三方比对结果
    verification = report.get("verification") or {}
    for key, label in (
        ("declaredWithoutFactory", "profile 声明了但没有工厂（死声明）"),
        ("factoryWithoutDeclaration", "有工厂但所有 profile 都不声明（孤儿工厂）"),
        ("duplicateInExclusiveConsumer", "互斥契约消费点内重复声明"),
    ):
        items = verification.get(key) or []
        if items:
            fail(f"{label}: {', '.join(items)}\n  处置: 死声明要接上工厂或删除；孤儿工厂要登记到 profile。")

    # 2. 契约合法性
    consumers = report.get("consumers") or []
    bad = sorted({c["contract"] for c in consumers if c.get("contract") not in VALID_CONTRACTS})
    if bad:
        fail(f"出现非法 contract: {', '.join(bad)}；合法值: {', '.join(sorted(VALID_CONTRACTS))}")

    # 3. 磁盘 profile 必须全部进入 dump（防漏加 EmbeddedResource）
    on_disk = {p.stem for p in PROFILE_DIR.glob("*.yaml")}
    in_dump = {c["profile"] for c in consumers}
    missing = sorted(on_disk - in_dump)
    if missing:
        fail(
            f"这些 profile 文件没有出现在 dump 里: {', '.join(missing)}\n"
            f"  多半是 modules/composition/composition/Composition.csproj 漏了 EmbeddedResource 登记。"
        )

    # 4. 规模非零（防止 dump 路径被改坏后「空报告也算通过」）
    if int(report.get("nodeCount") or 0) <= 0 or not consumers:
        fail(f"dump 规模异常: nodeCount={report.get('nodeCount')}, consumerCount={report.get('consumerCount')}")

    # 5. profile 源码里的 contract 字面量必须是合法值（解析器会抛错，这里做静态兜底）
    for path in sorted(PROFILE_DIR.glob("*.yaml")):
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = re.match(r"^\s*contract:\s*(\S+)\s*$", line)
            if match and match.group(1) not in VALID_CONTRACTS:
                fail(f"{path.name}:{lineno} 的 contract '{match.group(1)}' 非法；合法值: {', '.join(sorted(VALID_CONTRACTS))}")

    distribution: dict[str, int] = {}
    for consumer in consumers:
        distribution[consumer["contract"]] = distribution.get(consumer["contract"], 0) + 1
    summary = ", ".join(f"{k}={v}" for k, v in sorted(distribution.items()))
    print(f"[OK] 节点一致性通过: {report['nodeCount']} 节点 / {len(consumers)} 消费点（契约分布: {summary}）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
