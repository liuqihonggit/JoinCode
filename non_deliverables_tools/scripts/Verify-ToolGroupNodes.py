"""工具组三方一致性门禁 — cli_groups.yaml ↔ ToolGroupDeclaration ↔ base.yaml。

存在理由（TASK100 S6 实测）：节点 ID 是**全局命名空间**，`ci.yaml`/`sdk.yaml` 里的
`excludes: [web]` 会同时排掉 web-pipeline 的中间件与 tool-groups 的 web 组。
这意味着组名一旦与别的消费点撞车，就会产生「某个 profile 悄悄少一个组」的效果 ——
所以三处清单必须逐一相等，且只能有一处是「手写的真相」：

  cli_groups.yaml             生成物（由 migrate_mcp_tools_to_cli_nodes.py 从 node.yaml 生成）
  ToolGroupDeclaration.cs     ID 常量与 AllNodeIds（提示词层与 CLI 层唯一可见的判定来源）
  base.yaml tool-groups 段    profile 消费点声明（决定默认启用哪些组）

三个方向都要查：
  · 声明里有、组不存在  → profile 声明了一个永远不会有工厂的节点（--dump-nodes 会报「声明无工厂」）
  · 组存在、声明里没有  → 那个组永远关不掉（白名单语义下它是「未启用」，但没人能启用它）
  · 代码常量与声明不一致 → 提示词按 yaml 渲染、CLI 按常量判定，两边会给出相反答案
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GROUPS_YAML = ROOT / "resources/tools/cli_groups.yaml"
DECLARATION = ROOT / "modules/abstractions/composition/node/profile/ToolGroupDeclaration.cs"
BASE_YAML = ROOT / "resources/profiles/base.yaml"


def fail(message: str) -> None:
    print(f"[FAIL] {message}")
    sys.exit(1)


def load_yaml_groups() -> list[str]:
    text = GROUPS_YAML.read_text(encoding="utf-8")
    # 组条目形如 `  - id: agent`；只取 groups: 段落，避免把注释里的示例算进来。
    groups_block = text.split("groups:", 1)
    if len(groups_block) != 2:
        fail(f"{GROUPS_YAML} 缺少 groups: 段")
    return re.findall(r"^\s{2}- id:\s*(\S+)\s*$", groups_block[1], re.MULTILINE)


def load_declaration_ids() -> list[str]:
    text = DECLARATION.read_text(encoding="utf-8")
    constants = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]+)"\s*;', text))
    if "ConsumerId" not in constants:
        fail(f"{DECLARATION} 未找到 ConsumerId 常量")

    match = re.search(r"AllNodeIds\s*\{[^}]*\}\s*=\s*\[(.*?)\]", text, re.DOTALL)
    if match is None:
        fail(f"{DECLARATION} 未找到 AllNodeIds 数组初始化")

    ordered = []
    for token in match.group(1).replace("\n", " ").split(","):
        name = token.strip()
        if not name:
            continue
        if name not in constants:
            fail(f"AllNodeIds 引用了未定义的常量 '{name}'（{DECLARATION.name}）")
        ordered.append(constants[name])
    return ordered


def load_profile_ids() -> list[str]:
    text = BASE_YAML.read_text(encoding="utf-8")
    lines = text.splitlines()
    start = None
    for index, line in enumerate(lines):
        if line.strip() == "tool-groups:" and line.startswith("  "):
            start = index
            break
    if start is None:
        fail(f"{BASE_YAML} 缺少 tool-groups 消费点")

    ids: list[str] = []
    for line in lines[start + 1:]:
        stripped = line.strip()
        # 下一个缩进 2 的顶层键 = 本段结束
        if stripped and not line.startswith("    "):
            break
        match = re.match(r"^- id:\s*(\S+)$", stripped)
        if match:
            ids.append(match.group(1))
    return ids


def compare(name_a: str, a: list[str], name_b: str, b: list[str]) -> bool:
    only_a = [item for item in a if item not in b]
    only_b = [item for item in b if item not in a]
    if not only_a and not only_b:
        print(f"  [OK] {name_a} ↔ {name_b}: {len(a)} 组")
        return True
    if only_a:
        print(f"  [FAIL] 只在 {name_a}: {', '.join(only_a)}")
    if only_b:
        print(f"  [FAIL] 只在 {name_b}: {', '.join(only_b)}")
    return False


def main() -> int:
    yaml_groups = load_yaml_groups()
    declaration = load_declaration_ids()
    profile = load_profile_ids()

    print("═══ 工具组三方一致性 ═══")
    ok = compare("cli_groups.yaml", yaml_groups, "ToolGroupDeclaration", declaration)
    ok = compare("ToolGroupDeclaration", declaration, "base.yaml tool-groups", profile) and ok

    duplicates = [item for item in set(declaration) if declaration.count(item) > 1]
    if duplicates:
        print(f"  [FAIL] AllNodeIds 有重复项: {', '.join(sorted(duplicates))}")
        ok = False
    else:
        print(f"  [OK] AllNodeIds 无重复（{len(declaration)} 项）")

    if ok:
        print(f"结果: [OK] 三处清单一致（{len(yaml_groups)} 组）")
        return 0
    print("结果: [FAIL] 工具组清单不一致 —— 加组/删组时必须同时改这三处")
    return 1


if __name__ == "__main__":
    sys.exit(main())
