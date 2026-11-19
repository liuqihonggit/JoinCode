"""工具组三方一致性门禁 + profile 工具面对账表。

存在理由（TASK100 S6/S7）：工具组是三处清单的交汇点，且三处各有各的失效方式。

  cli_groups.yaml             生成物（由 migrate_mcp_tools_to_cli_nodes.py 从 node.yaml 生成）
  ToolGroupDeclaration.cs     ID 常量与 AllNodeIds（提示词层与 CLI 层唯一可见的判定来源）
  base.yaml tool-groups 段    profile 消费点声明（决定默认启用哪些组）

三个方向都要查：
  · 声明里有、组不存在  → profile 声明了一个永远不会有工厂的节点（--dump-nodes 会报「声明无工厂」）
  · 组存在、声明里没有  → 那个组永远关不掉（白名单语义下它是「未启用」，但没人能启用它）
  · 代码常量与声明不一致 → 提示词按 yaml 渲染、CLI 按常量判定，两边会给出相反答案

另外两条 S7 新增的检查（都是把「口头注意事项」变成会失败的门禁）：

  · 组 ID 不得与其它消费点的节点 ID 同名。节点 ID 是全局命名空间，`excludes` 若不限定消费点
    就会同时命中两处 —— 这种耦合的表现为「某 profile 悄悄少一个组」，排查成本极高。
    （历史注：§22.7 曾断言 `web` 已经撞车，S7 实测为误判：ci 的 web-pipeline 少的是
    `crash-snapshot-mw`，`web` 只命中了工具组。误判本身说明这条需要检查，不能靠人读注释。）
  · 工具组只能用 `tool-groups:<组>` 的限定写法排除。裸写 `web` 在今天就等于 `tool-groups:web`，
    但明天多一个同名中间件就会连带改掉工具面 —— 让它在 CI 失败，而不是让下一个人去查。

用法：
  python non_deliverables_tools/scripts/Verify-ToolGroupNodes.py            # 门禁
  python non_deliverables_tools/scripts/Verify-ToolGroupNodes.py --report   # 打印对账表（markdown）
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GROUPS_YAML = ROOT / "resources/tools/cli_groups.yaml"
DECLARATION = ROOT / "modules/abstractions/composition/node/profile/ToolGroupDeclaration.cs"
BASE_YAML = ROOT / "resources/profiles/base.yaml"
PROFILES_DIR = ROOT / "resources/profiles"

CONSUMER_ID = "tool-groups"


def fail(message: str) -> None:
    print(f"[FAIL] {message}")
    sys.exit(1)


# ── 解析 ──────────────────────────────────────────────────────────────


def load_yaml_groups() -> list[str]:
    text = GROUPS_YAML.read_text(encoding="utf-8")
    # 组条目形如 `  - id: agent`；只取 groups: 段落，避免把注释里的示例算进来。
    groups_block = text.split("groups:", 1)
    if len(groups_block) != 2:
        fail(f"{GROUPS_YAML} 缺少 groups: 段")
    return re.findall(r"^\s{2}- id:\s*(\S+)\s*$", groups_block[1], re.MULTILINE)


def load_group_tool_counts() -> dict[str, int]:
    """组 ID → 该组工具数（cli_groups.yaml 的 tools 字段，生成物，已与 node.yaml 对齐）。"""
    text = GROUPS_YAML.read_text(encoding="utf-8").split("groups:", 1)[1]
    counts: dict[str, int] = {}
    for match in re.finditer(
        r"- id:\s*(\S+)\s*\n\s*exe:\s*(\S+)\s*\n\s*tools:\s*(\d+)", text
    ):
        counts[match.group(1)] = int(match.group(3))
    return counts


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


def load_consumer_nodes() -> dict[str, list[str]]:
    """base.yaml 的 consumers: → {消费者 ID: [节点 ID]}。

    只解析 `consumers:` 下的 2 空格键；节点行是 `      - id: xxx`（6 空格）。
    """
    lines = BASE_YAML.read_text(encoding="utf-8").splitlines()
    consumers: dict[str, list[str]] = {}
    in_consumers = False
    current: str | None = None

    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if not line.startswith(" ") and stripped == "consumers:":
            in_consumers = True
            continue
        if in_consumers:
            if not line.startswith(" "):
                break  # 顶层键 = consumers 段结束
            if line.startswith("  ") and not line.startswith("    ") and stripped.endswith(":"):
                current = stripped[:-1]
                consumers[current] = []
                continue
            if current is not None:
                match = re.match(r"^- id:\s*(\S+)$", stripped)
                if match:
                    consumers[current].append(match.group(1))
    return consumers


def parse_profile_file(path: Path) -> dict[str, object]:
    """解析一个 profile 文件里本脚本关心的字段（name / inherits / excludes / includes 的消费点）。"""
    lines = path.read_text(encoding="utf-8").splitlines()
    name = path.stem
    inherits: str | None = None
    excludes: list[str] = []
    includes: list[str] = []
    in_excludes = False
    in_includes = False

    for line in lines:
        stripped = line.strip()
        if not stripped:
            continue
        if stripped.startswith("#"):
            continue
        if not line.startswith(" "):
            in_excludes = stripped == "excludes:"
            in_includes = stripped == "includes:"
            if stripped.startswith("name:"):
                name = stripped.split(":", 1)[1].strip()
            elif stripped.startswith("inherits:"):
                inherits = stripped.split(":", 1)[1].strip() or None
            continue
        if in_excludes:
            match = re.match(r'^-\s*"?([^"#]+?)"?\s*(?:#.*)?$', stripped)
            if match:
                excludes.append(match.group(1).strip())
        elif in_includes:
            # 子 profile 可以 includes 一个父级没有的消费点：这种消费点同样能出现在
            # `consumer:node` 限定写法里，所以它必须被算进「已知消费点」。
            match = re.match(r"^([\w-]+):\s*$", stripped)
            if match:
                includes.append(match.group(1))
    return {"name": name, "inherits": inherits, "excludes": excludes, "includes": includes}


def load_profiles() -> dict[str, dict[str, object]]:
    profiles = {}
    for path in sorted(PROFILES_DIR.glob("*.yaml")):
        parsed = parse_profile_file(path)
        profiles[str(parsed["name"])] = parsed
    return profiles


def resolve_chain(profiles: dict[str, dict[str, object]], name: str) -> list[str]:
    """返回 [自身, 父, 祖父, ...]（用于按序应用 excludes）。"""
    chain = [name]
    seen = {name}
    current = profiles.get(name)
    while current and current["inherits"]:
        parent = str(current["inherits"])
        if parent in seen:
            fail(f"profile '{name}' 继承链出现环: {' → '.join(chain + [parent])}")
        chain.append(parent)
        seen.add(parent)
        current = profiles.get(parent)
    return chain


def split_exclude(entry: str, consumer_ids: set[str]) -> tuple[str | None, str]:
    """与 NodeProfileResolver.SplitExclude 同语义：前缀是已知消费点才当限定符。"""
    if ":" not in entry:
        return None, entry
    prefix, node = entry.split(":", 1)
    if prefix in consumer_ids:
        return prefix, node
    return None, entry


def enabled_groups(
    profiles: dict[str, dict[str, object]],
    name: str,
    all_groups: list[str],
    consumers: dict[str, list[str]],
) -> list[str]:
    chain = resolve_chain(profiles, name)
    consumer_ids = set(consumers.keys())
    removed: set[str] = set()
    for profile_name in chain:
        for entry in profiles[profile_name]["excludes"]:  # type: ignore[union-attr]
            scope, node = split_exclude(str(entry), consumer_ids)
            if scope is None or scope == CONSUMER_ID:
                removed.add(node)
    return [group for group in all_groups if group not in removed]


# ── 门禁 ──────────────────────────────────────────────────────────────


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


def check_no_id_collision(groups: list[str], consumers: dict[str, list[str]]) -> bool:
    """组 ID 不得同时是别的消费点里的节点 ID。"""
    owners: dict[str, list[str]] = {}
    for consumer_id, nodes in consumers.items():
        if consumer_id == CONSUMER_ID:
            continue
        for node_id in nodes:
            if node_id in groups:
                owners.setdefault(node_id, []).append(consumer_id)

    if not owners:
        print(f"  [OK] 组 ID 与其它消费点无同名节点（{len(groups)} 组）")
        return True
    for node_id, where in sorted(owners.items()):
        print(
            f"  [FAIL] 组 '{node_id}' 同时是消费点 {', '.join(where)} 的节点 ID —— "
            f"裸 excludes 会同时排掉两处，请改用 'tool-groups:{node_id}'"
        )
    return False


def check_scoped_excludes(
    profiles: dict[str, dict[str, object]], consumers: dict[str, list[str]]
) -> bool:
    """限定写法的前缀必须真是消费点，且该消费点里真有这个节点（拦住 `tool-group:` 这种笔误）。"""
    declared = set(consumers) | {
        str(consumer) for profile in profiles.values() for consumer in profile["includes"]  # type: ignore[union-attr]
    }
    ok = True
    scoped_count = 0
    for name, profile in sorted(profiles.items()):
        for entry in profile["excludes"]:  # type: ignore[union-attr]
            entry = str(entry)
            if ":" not in entry:
                continue
            prefix, node = entry.split(":", 1)
            if prefix not in declared:
                # 冒号不是限定符（按 resolver 语义会被当成节点 ID 的一部分），不在此处判错，
                # 但要提醒：这类写法几乎总是把消费点名写错了。
                print(f"  [FAIL] {name}.yaml: '{entry}' 的前缀 '{prefix}' 不是任何消费点 —— 疑似笔误")
                ok = False
                continue
            scoped_count += 1
            # includes 里新增的消费点在 base.yaml 里没有节点清单，只能核对前缀。
            if prefix in consumers and node not in consumers[prefix]:
                print(f"  [FAIL] {name}.yaml: 消费点 '{prefix}' 里没有节点 '{node}'")
                ok = False
    if ok:
        print(f"  [OK] 限定写法 excludes 均指向真实节点（{scoped_count} 条）")
    return ok


def check_unscoped_group_excludes(
    profiles: dict[str, dict[str, object]], groups: list[str]
) -> bool:
    """工具组只能用限定写法排除 —— 裸写今天就等于限定，明天可能不是你想要的。"""
    offenders: list[str] = []
    for name, profile in sorted(profiles.items()):
        for entry in profile["excludes"]:  # type: ignore[union-attr]
            entry = str(entry)
            if ":" in entry:
                continue
            if entry in groups:
                offenders.append(f"{name}.yaml: {entry}")
    if not offenders:
        print("  [OK] 工具组排除均使用 'tool-groups:<组>' 限定写法")
        return True
    for offender in offenders:
        print(f"  [FAIL] {offender} —— 请改成 'tool-groups:<组>'")
    return False


# ── 真机交叉核对（需要已编译的 jcc.exe）────────────────────────────────


def _run(exe: Path, args: list[str]) -> str:
    import subprocess

    completed = subprocess.run(  # noqa: S603 — 参数是常量，exe 路径来自 CI
        [str(exe), *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=180,
        check=False,
    )
    return completed.stdout + completed.stderr


def check_live(
    exe: Path,
    groups: list[str],
    counts: dict[str, int],
    profiles: dict[str, dict[str, object]],
    consumers: dict[str, list[str]],
) -> bool:
    """三个面必须说同一组数字：静态解析 / `tool tree` / 提示词的组目录。

    为什么值得单独一层：`tool tree` 走 CliProfileGate，提示词走 PromptToolGroupScope，
    两者取信于不同的 profile 来源。S7 实测过一次「tool tree 8 组、prompt 列出 13 组」——
    静态门禁全绿也发现不了，因为错的是「谁在读 profile」，不是「配置写了什么」。
    """
    ok = True
    for name in sorted(profiles):
        expected = enabled_groups(profiles, name, groups, consumers)
        expected_tools = sum(counts.get(group, 0) for group in expected)

        tree_out = _run(exe, ["--profile", name, "tool", "tree"])
        tree_match = re.search(r"共\s*(\d+)\s*组\s*/\s*(\d+)\s*个工具", tree_out)
        prompt_out = _run(exe, ["--profile", name, "prompt", "--section", "tools"])
        prompt_ids = set(re.findall(r"jcc-tool-([a-z]+)", prompt_out))
        prompt_header = re.search(r"分装在\s*(\d+)\s*个独立 exe 里（共\s*(\d+)\s*个工具", prompt_out)

        if tree_match is None:
            print(f"  [FAIL] {name}: `tool tree` 输出无法解析（命令失败或格式变了）")
            ok = False
            continue
        tree_groups, tree_tools = int(tree_match.group(1)), int(tree_match.group(2))

        problems = []
        if tree_groups != len(expected):
            problems.append(f"tool tree 报 {tree_groups} 组，解析结果 {len(expected)} 组")
        if tree_tools != expected_tools:
            problems.append(f"tool tree 报 {tree_tools} 个工具，解析结果 {expected_tools} 个")
        if prompt_ids != set(expected):
            missing = sorted(set(expected) - prompt_ids)
            extra = sorted(prompt_ids - set(expected))
            problems.append(f"提示词组清单不一致（少 {missing or '无'}，多 {extra or '无'}）")
        if prompt_header is None:
            problems.append("提示词里找不到「分装在 N 个独立 exe 里（共 M 个工具」这句")
        else:
            prompt_groups, prompt_tools = int(prompt_header.group(1)), int(prompt_header.group(2))
            if prompt_groups != len(expected) or prompt_tools != expected_tools:
                problems.append(
                    f"提示词头部报 {prompt_groups} 组 / {prompt_tools} 工具，解析结果 "
                    f"{len(expected)} 组 / {expected_tools} 工具"
                )

        if problems:
            for problem in problems:
                print(f"  [FAIL] {name}: {problem}")
            ok = False
        else:
            print(f"  [OK] {name}: {len(expected)} 组 / {expected_tools} 工具（静态解析 = tool tree = 提示词）")
    return ok


# ── 对账表 ────────────────────────────────────────────────────────────


def print_report(
    groups: list[str],
    counts: dict[str, int],
    profiles: dict[str, dict[str, object]],
    consumers: dict[str, list[str]],
) -> None:
    total_groups = len(groups)
    total_tools = sum(counts.values())
    print("| profile | 继承 | 启用组 | 排除的组（工具数） | 工具合计 |")
    print("|---|---|---|---|---|")
    print(f"| base | — | {total_groups} | — | {total_tools} |")
    for name in sorted(profiles):
        if name == "base":
            continue
        enabled = enabled_groups(profiles, name, groups, consumers)
        removed = [group for group in groups if group not in enabled]
        removed_text = "、".join(f"{group}({counts.get(group, 0)})" for group in removed) or "—"
        tool_total = sum(counts.get(group, 0) for group in enabled)
        print(
            f"| {name} | {profiles[name]['inherits'] or '—'} | {len(enabled)} | "
            f"{removed_text} | {tool_total} |"
        )


def main() -> int:
    yaml_groups = load_yaml_groups()
    declaration = load_declaration_ids()
    consumers = load_consumer_nodes()
    profiles = load_profiles()

    profile_ids = consumers.get(CONSUMER_ID)
    if profile_ids is None:
        fail(f"{BASE_YAML} 缺少 {CONSUMER_ID} 消费点")

    if "--report" in sys.argv:
        print_report(yaml_groups, load_group_tool_counts(), profiles, consumers)
        return 0

    print("═══ 工具组三方一致性 ═══")
    ok = compare("cli_groups.yaml", yaml_groups, "ToolGroupDeclaration", declaration)
    ok = compare("ToolGroupDeclaration", declaration, f"base.yaml {CONSUMER_ID}", profile_ids) and ok

    duplicates = [item for item in set(declaration) if declaration.count(item) > 1]
    if duplicates:
        print(f"  [FAIL] AllNodeIds 有重复项: {', '.join(sorted(duplicates))}")
        ok = False
    else:
        print(f"  [OK] AllNodeIds 无重复（{len(declaration)} 项）")

    print("═══ 节点 ID 全局命名空间 ═══")
    ok = check_no_id_collision(yaml_groups, consumers) and ok
    ok = check_unscoped_group_excludes(profiles, yaml_groups) and ok
    ok = check_scoped_excludes(profiles, consumers) and ok

    live_arg = next((arg for arg in sys.argv if arg.startswith("--live=")), None)
    if live_arg is not None:
        raw = Path(live_arg.split("=", 1)[1])
        # 允许只给目录或省略 .exe：Windows 上是 jcc.exe，其它平台是无后缀的 jcc。
        candidates = [raw, raw.with_name(raw.name + ".exe")] if raw.suffix != ".exe" else [raw, raw.with_suffix("")]
        if raw.is_dir():
            candidates = [raw / "jcc.exe", raw / "jcc"]
        exe = next((path for path in candidates if path.exists()), None)
        if exe is None:
            print(f"  [FAIL] --live 指定的 jcc 不存在: {raw}")
            ok = False
        else:
            print("═══ 真机交叉核对（静态解析 = tool tree = 提示词）═══")
            ok = check_live(exe, yaml_groups, load_group_tool_counts(), profiles, consumers) and ok

    if ok:
        print(f"结果: [OK] 三处清单一致（{len(yaml_groups)} 组），排除写法均无跨消费点耦合")
        return 0
    print("结果: [FAIL] 工具组清单/排除写法有问题 —— 加组、删组、裁剪 profile 时必须同时改这里列出的位置")
    return 1


if __name__ == "__main__":
    sys.exit(main())
