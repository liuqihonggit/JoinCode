#!/usr/bin/env python3
"""G2 节点继承的生产落地门禁（TASK097 §14）：node.inherit.yaml 叠加文件必须真的生效。

存在理由：G2 机制有 8 条单测 + 夹具，但生产 15 个 node.yaml 零 inherits——
`ResolveAll` 对生产数据是 no-op。"节点皆可继承"当时只是能力，不是事实。
叠加文件（node.inherit.yaml，生成器承诺但此前未实现）让人工差异与生成物共存：
生成器全量重写 node.yaml，绝不碰叠加文件；加载器把差异叠加后再进继承链。

本门禁把三件事变成会失败的 CI 检查：

  1. 每份叠加文件必须有同目录 node.yaml，且 id 一致（叠到别的组上会显示错 ID）
  2. 叠加文件必须至少改一个字段（空叠加 = 无意义文件，通常是写错名字）
  3. 生成器不得把 inherits 写进 node.yaml（那会在下次重跑时静默丢失）
  4. --live 真机对账：每份叠加文件改过的 tags，必须出现在 `jcc tool help <id>` 里
     —— 证明"文件存在"不等于"生效"（G4/G6 都栽过"打印出来≠消费端吃到"）

用法:
  python non_deliverables_tools/scripts/Verify-CliNodeInheritance.py
  python non_deliverables_tools/scripts/Verify-CliNodeInheritance.py --live=artifacts/bin/JoinCode/Debug/net10.0
退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GROUPS = ROOT / "modules/tools/cli_nodes/groups"
OVERLAY_NAME = "node.inherit.yaml"
BASE_NAME = "node.yaml"


def scalar(text: str, key: str) -> str:
    match = re.search(rf"^{re.escape(key)}:\s*(.+)$", text, re.MULTILINE)
    return match.group(1).strip().strip('"') if match else ""


def inline_list(text: str, key: str) -> list[str]:
    value = scalar(text, key)
    if not (value.startswith("[") and value.endswith("]")):
        return []
    return [item.strip() for item in value[1:-1].split(",") if item.strip()]


def fail(message: str) -> None:
    print(f"[FAIL] {message}")
    sys.exit(1)


def main() -> int:
    jcc_arg = next((a for a in sys.argv[1:] if a.startswith("--live=")), None)
    jcc = (ROOT / jcc_arg[len("--live="):] / "jcc.exe") if jcc_arg else None

    overlays = sorted(GROUPS.glob(f"*/{OVERLAY_NAME}")) if GROUPS.exists() else []
    if not overlays:
        fail("没有找到任何 node.inherit.yaml —— G2 继承机制在生产仍是 no-op（TASK097 §14）")

    problems: list[str] = []
    print("═══ CLI 节点叠加继承 ═══")
    for overlay_path in overlays:
        group = overlay_path.parent.name
        baseline_path = overlay_path.parent / BASE_NAME
        overlay_text = overlay_path.read_text(encoding="utf-8-sig")
        overlay_id = scalar(overlay_text, "id")

        if not baseline_path.exists():
            problems.append(f"{group}: 有叠加文件但没有 {BASE_NAME} —— 叠加没有基准")
            continue
        if overlay_id != group:
            problems.append(
                f"{group}: 叠加文件 id='{overlay_id}' 与组名不一致 —— 叠加描述的是同一节点的差异"
            )
            continue

        baseline_text = baseline_path.read_text(encoding="utf-8-sig")
        if re.search(r"^\s*inherits\s*:", baseline_text, re.MULTILINE):
            problems.append(
                f"{group}: {BASE_NAME} 含手写 inherits —— 生成器重跑会静默丢失（应放叠加文件）"
            )

        overlay_keys = set(re.findall(r"^([a-z_]+):", overlay_text, re.MULTILINE)) - {"id"}
        if not overlay_keys:
            problems.append(f"{group}: 叠加文件除 id 外没有声明任何差异字段 —— 空叠加无意义")
            continue

        overlay_tags = inline_list(overlay_text, "tags")
        baseline_tags = inline_list(baseline_text, "tags")
        changed_tags = [tag for tag in overlay_tags if tag not in baseline_tags]
        print(f"  {group}: id={overlay_id} 差异字段={sorted(overlay_keys)} 新增标签={changed_tags}")

        if jcc is not None and changed_tags:
            if not jcc.exists():
                fail(f"--live 指定的 jcc 不存在: {jcc}")
            proc = subprocess.run(
                [str(jcc), "tool", "help", group],
                capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=ROOT,
            )
            if proc.returncode != 0:
                problems.append(f"{group}: `jcc tool help {group}` 退出码 {proc.returncode}")
                continue
            output = proc.stdout + proc.stderr
            missing = [tag for tag in changed_tags if tag not in output]
            if missing:
                problems.append(
                    f"{group}: 叠加新增标签 {missing} 没出现在 `jcc tool help {group}` —— "
                    "文件存在但没生效（加载层漏读叠加？）"
                )

    if jcc is not None:
        print("  [--live] 已对账叠加新增标签是否进入 tool help 输出")
    else:
        print("  [--live 未指定] 仅静态校验；加 --live=<jcc 目录> 可验证真机生效")

    if problems:
        print()
        for problem in problems:
            print(f"[FAIL] {problem}")
        return 1
    verdict = "结构正确且真机生效" if jcc is not None else "结构正确（未做真机对账）"
    print(f"  [OK] {len(overlays)} 份叠加文件{verdict}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
