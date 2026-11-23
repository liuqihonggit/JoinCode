#!/usr/bin/env python3
"""TASK097 §15 — 锁住插件节点桥接与能力路由的架构约定。

背景：G1 可逆注册（RegistrationHandle）与 G4 metadata 长期空转 —— 机制有、生产不用。
本轮让插件加载/卸载走 INodeRegistry.Register/Dispose、让 meta.required-capabilities
参与路由。本门禁防止后续改动把这两处重新架空：

  1. 插件加载/卸载必须调用 INodeRegistry.Register + RegistrationHandle.Dispose，
     不许绕过注册表自建平行装卸表
  2. 两个节点解析入口（GetNodesForConsumerAsync / GetServicesForConsumerAsync）
     都必须经过 RequiredCapabilitiesSatisfied 过滤
  3. 声明数校验（Brain 的 preprocess 链）必须与 registry 同源调用
     RequiredCapabilitiesSatisfied —— 否则「能力关闭」会被误判成「装配失败」
  4. 桥接接线必须挂在 PluginLoaded/PluginUnloading 事件上（真生命周期）

退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]

BRIDGE = "modules/composition/composition/node_registry/core/PluginNodeBridge.cs"
REGISTRY = "modules/composition/composition/node_registry/core/NodeRegistry.cs"
WIRE = "modules/composition/composition/dependency_injection/infrastructure/ServiceRegistration.Skills.cs"
BRAIN = "modules/composition/composition/dependency_injection/infrastructure/ServiceRegistration.Brain.cs"
TEST = "modules/composition/composition_tests/node_registry/NodeBridgeAndCapabilityTest.cs"


def read(relative: str) -> str | None:
    path = REPO / relative
    return path.read_text(encoding="utf-8-sig") if path.exists() else None


def main() -> int:
    problems: list[str] = []
    print("═══ 插件节点桥 + 能力路由约定 ═══")

    bridge = read(BRIDGE)
    if bridge is None:
        problems.append(f"桥接文件缺失: {BRIDGE}")
    else:
        # 1. Register + Dispose，且不能自建平行表（必须持有 RegistrationHandle）。
        if not re.search(r"_registry\.Register\s*\(", bridge):
            problems.append("桥接未调用 INodeRegistry.Register —— G1 可逆注册被架空")
        if not re.search(r"RegistrationHandle", bridge):
            problems.append("桥接未持有 RegistrationHandle —— 无法保证 LIFO 回滚")
        if not re.search(r"\.Dispose\s*\(\s*\)", bridge):
            problems.append("桥接卸载时未 Dispose 句柄 —— 插件卸载不会回滚节点")
        if "lock " in bridge:
            problems.append("桥接用了 lock —— 项目要求不可变快照 + CAS（JCC4008）")
        print("  [OK] 桥接走 Register/Dispose 且无 lock")

    wire = read(WIRE)
    if wire is None:
        problems.append(f"接线文件缺失: {WIRE}")
    else:
        # 4. 挂真生命周期事件。
        if "PluginLoaded +=" not in wire or "PluginUnloading +=" not in wire:
            problems.append("桥接未挂 PluginLoaded/PluginUnloading 事件 —— 不是真生命周期")
        if "nodeBridge.OnPluginLoaded" not in wire or "nodeBridge.OnPluginUnloading" not in wire:
            problems.append("桥接事件未调用 PluginNodeBridge 的装卸方法")
        print("  [OK] 桥接挂在插件加载/卸载真事件上")

    registry = read(REGISTRY)
    if registry is None:
        problems.append(f"注册表文件缺失: {REGISTRY}")
    else:
        # 2. 两个解析入口都要过滤（出现 >= 3 次：方法定义 + 两处调用）。
        hits = len(re.findall(r"RequiredCapabilitiesSatisfied\s*\(", registry))
        if hits < 3:
            problems.append(
                f"RequiredCapabilitiesSatisfied 在注册表中只出现 {hits} 次（定义 + 两入口调用应 >= 3）"
                " —— 有解析入口漏了能力过滤"
            )
        if not re.search(r"internal static bool RequiredCapabilitiesSatisfied", registry):
            problems.append("RequiredCapabilitiesSatisfied 非 internal —— 声明数校验无法与之一致")
        print(f"  [OK] 两个解析入口均过滤（出现 {hits} 次）")

    brain = read(BRAIN)
    if brain is None:
        problems.append(f"Brain 注册文件缺失: {BRAIN}")
    else:
        # 3. 声明数校验必须同源调用过滤方法。
        if "RequiredCapabilitiesSatisfied(" not in brain:
            problems.append(
                "preprocess 声明数校验未调用 RequiredCapabilitiesSatisfied —— "
                "能力关闭会被误判成装配失败（两套过滤真相）"
            )
        print("  [OK] 声明数校验与 registry 过滤同源")

    if read(TEST) is None:
        problems.append(f"回归测试缺失: {TEST}")
    else:
        print("  [OK] 回归测试存在")

    if problems:
        print()
        for problem in problems:
            print(f"[FAIL] {problem}")
        return 1

    print("  [OK] 桥接可逆注册 + 能力路由约定全部成立")
    return 0


if __name__ == "__main__":
    sys.exit(main())
