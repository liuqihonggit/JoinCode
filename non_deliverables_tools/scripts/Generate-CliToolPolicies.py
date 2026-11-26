"""从各分组 help.txt 生成参数权限策略骨架（ADR 0168）。

存在理由：node.yaml 只登记工具与 mode，不登记参数；参数真相在 help.txt（由 [McpToolParameter]
在编译期生成）。安全默认要求「每个 CLI 参数都必须有权限条目」，而 540 个工具手写会漏。
本脚本把 help.txt 的参数表转成 resources/tools/policies/<group>.yaml：

  · 所有参数默认 effect: allow（当前硬边界是「参数白名单」，危险能力收紧再按组叠加 deny/ask）
  · 无参数工具显式写 parameters: []
  · 已存在的策略文件默认跳过，避免覆盖人工收紧的 effect/capabilities

用法：
  python non_deliverables_tools/scripts/Generate-CliToolPolicies.py            # 生成缺失组
  python non_deliverables_tools/scripts/Generate-CliToolPolicies.py git web    # 指定组
  python non_deliverables_tools/scripts/Generate-CliToolPolicies.py --force    # 重写已有文件
"""

from __future__ import annotations

import hashlib
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GROUPS_ROOT = ROOT / "modules/tools/cli_nodes/groups"
POLICY_ROOT = ROOT / "resources/tools/policies"
TRUST_PATH = ROOT / "resources/tools/policy_trust.yaml"

LIST_LINE = re.compile(r"^  (\S+)\s{2,}\S")
DETAIL_LINE = re.compile(r"^  (\S+) — ")
PARAM_LINE = re.compile(r"^    --(\S+)\s")
DETAIL_MARKER = "参数说明"

# 组 exe 之外额外内嵌同一份策略的执行镜像：delegate 实现（jcc-gh）与宿主直调路径（jcc slash_call）。
EXTRA_POLICY_EXES = {"github": ["jcc-gh.exe"], "slash": ["jcc.exe"]}


def parse_help(help_text: str) -> tuple[list[str], dict[str, list[str]]]:
    tools: list[str] = []
    params: dict[str, list[str]] = {}
    current: str | None = None
    in_details = False

    for line in help_text.splitlines():
        if line.strip().startswith("单个") and DETAIL_MARKER in line:
            in_details = True
            current = None
            continue
        if not in_details:
            match = LIST_LINE.match(line)
            if match:
                tools.append(match.group(1))
            continue

        detail = DETAIL_LINE.match(line)
        if detail:
            current = detail.group(1)
            params.setdefault(current, [])
            if current not in tools:
                tools.append(current)
            continue

        param = PARAM_LINE.match(line)
        if param and current:
            name = param.group(1)
            if name not in params[current]:
                params[current].append(name)

    return tools, params


def yaml_key(name: str) -> str:
    if re.fullmatch(r"[A-Za-z0-9_-]+", name):
        return name
    return '"' + name.replace("\\", "\\\\").replace('"', '\\"') + '"'


def render(group: str, exe: str, tools: list[str], params: dict[str, list[str]]) -> str:
    lines = [
        f"# {group} 组工具参数权限 — ADR 0168（由 Generate-CliToolPolicies.py 从 help.txt 生成）",
        "#",
        "# 参数白名单是硬边界：调用方只能传这里登记的参数；收紧危险能力时按需补 effect: deny/ask。",
        "schema_version: 1",
        f"policy_id: cli.{group}",
        "policy_version: 1",
        "executables:",
        f"  - {exe}",
        *[f"  - {extra}" for extra in EXTRA_POLICY_EXES.get(group, [])],
        "",
        "tools:",
    ]
    for tool in tools:
        tool_params = params.get(tool, [])
        lines.append(f"  {yaml_key(tool)}:")
        lines.append("    effect: allow")
        if not tool_params:
            lines.append("    parameters: []")
            continue
        lines.append("    parameters:")
        for name in tool_params:
            lines.append(f"      {yaml_key(name)}:")
            lines.append("        effect: allow")
    return "\n".join(lines) + "\n"


def write_trust_catalog() -> int:
    """生成策略信任目录 — jcc 只认这里锚定的 policy id + 内容哈希（ADR 0168）。

    信任目录必须与策略文件同一提交：宿主内嵌它，broker 加载目标 exe 的策略后
    比对哈希；改策略不刷新目录会被启动期/执行期拒绝，而不是静默放行。
    """
    entries: list[tuple[str, str, list[str]]] = []
    for policy_path in sorted(POLICY_ROOT.glob("*.yaml")):
        raw = policy_path.read_bytes()
        policy_id = re.search(rb"(?m)^policy_id:\s*(\S+)", raw)
        if not policy_id:
            print(f"[trust-skip] {policy_path.name}: 缺少 policy_id")
            continue
        exec_section = re.search(rb"(?ms)^executables:\s*$(.*?)(?=^\S)", raw)
        executables = re.findall(rb"(?m)^\s+-\s*(\S+)", exec_section.group(1)) if exec_section else []
        entries.append((
            policy_id.group(1).decode(),
            hashlib.sha256(raw).hexdigest(),
            [item.decode() for item in executables],
        ))

    lines = [
        "# 由 Generate-CliToolPolicies.py 从 resources/tools/policies/*.yaml 生成 —— 不要手改",
        "#",
        "# jcc broker 用它校验目标 exe 内嵌策略的 id 与内容哈希（ADR 0168 信任目录）。",
        "# 重新生成: python non_deliverables_tools/scripts/Generate-CliToolPolicies.py --trust-only",
        "version: 1",
        "policies:",
    ]
    for policy_id, digest, executables in entries:
        lines.append(f"  - id: {policy_id}")
        lines.append(f"    hash: {digest}")
        lines.append("    executables:")
        for exe in executables:
            lines.append(f"      - {exe}")
    TRUST_PATH.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"[trust] {TRUST_PATH.relative_to(ROOT)} policies={len(entries)}")
    return len(entries)


def main(argv: list[str]) -> int:
    force = "--force" in argv
    trust_only = "--trust-only" in argv
    requested = [arg for arg in argv if not arg.startswith("--")]
    groups = [] if trust_only else (requested or sorted(
        path.parent.name for path in GROUPS_ROOT.glob("*/help.txt")))

    POLICY_ROOT.mkdir(parents=True, exist_ok=True)
    written = 0
    skipped = 0
    for group in groups:
        help_path = GROUPS_ROOT / group / "help.txt"
        if not help_path.exists():
            print(f"[skip] {group}: 缺少 help.txt")
            skipped += 1
            continue

        node_yaml = (GROUPS_ROOT / group / "node.yaml").read_text(encoding="utf-8")
        exe_match = re.search(r"(?m)^exe:\s*(\S+)", node_yaml)
        if not exe_match:
            print(f"[skip] {group}: node.yaml 缺少 exe")
            skipped += 1
            continue

        target = POLICY_ROOT / f"{group}.yaml"
        if target.exists() and not force:
            print(f"[keep] {target.relative_to(ROOT)} 已存在")
            skipped += 1
            continue

        tools, params = parse_help(help_path.read_text(encoding="utf-8"))
        target.write_text(render(group, exe_match.group(1), tools, params),
                          encoding="utf-8", newline="\n")
        written += 1
        print(f"[write] {target.relative_to(ROOT)} tools={len(tools)}")

    print(f"完成: 生成 {written} 份，跳过 {skipped} 份")
    write_trust_catalog()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
