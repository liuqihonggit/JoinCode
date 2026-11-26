#!/usr/bin/env python3
"""生成 jcc-tool-slash 独立 CLI 节点 — ADR 0157「mcp 和斜杠命令都拆成 exe」。

只读扫描现有 [ChatCommand] / [ChatCommandArg] 特性，生成：
  modules/tools/cli_nodes/groups/slash/help.txt            编译期内嵌的 -h 文本（按分类分组）
  modules/tools/cli_nodes/groups/slash/jcc_tool_slash.csproj

Program.cs 由人手工维护（第一行 HANDCRAFTED），本脚本绝不覆盖，也不修改任何源 .cs。
说明文本的单一数据源始终是现有特性 — 脚本只是把它按分类渲染成帮助文本。
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = REPO_ROOT / "modules/tools/cli_nodes/groups/slash"
ENUM_FILE = REPO_ROOT / "modules/abstractions/abstractions_primitives_utils/src/constants/cli_ansi/ChatCommandName.cs"

CAMEL_RE = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")


def unescape_csharp(raw: str) -> str:
    return raw.replace('\\"', '"').replace("\\n", " ").replace("\\\\", "\\").strip()


def split_top_level(text: str) -> list[str]:
    """按顶层逗号切分，尊重字符串与 () [] {} 嵌套。"""
    parts: list[str] = []
    buf: list[str] = []
    depth = 0
    in_str = False
    esc = False
    for ch in text:
        if in_str:
            buf.append(ch)
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
            buf.append(ch)
        elif ch in "([{":
            depth += 1
            buf.append(ch)
        elif ch in ")]}":
            depth -= 1
            buf.append(ch)
        elif ch == "," and depth == 0:
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(ch)
    if buf:
        parts.append("".join(buf))
    return parts


def find_attributes(text: str, name: str) -> list[tuple[int, int, str]]:
    """找 [name(...)] 特性块，返回 (start, end, body)，body 含方括号。"""
    results: list[tuple[int, int, str]] = []
    marker = "[" + name + "("
    idx = 0
    while True:
        start = text.find(marker, idx)
        if start < 0:
            break
        i = start + len(marker) - 1  # 指向 '('
        depth = 0
        in_str = False
        esc = False
        while i < len(text):
            ch = text[i]
            if in_str:
                if esc:
                    esc = False
                elif ch == "\\":
                    esc = True
                elif ch == '"':
                    in_str = False
            elif ch == '"':
                in_str = True
            elif ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        end = i + 1
        if end < len(text) and text[end] == "]":
            end += 1
        results.append((start, end, text[start:end]))
        idx = end
    return results


def parse_attr(body: str) -> tuple[list[str], dict[str, str]]:
    """拆出位置参数与命名参数。"""
    inner = body[body.find("(") + 1: body.rfind(")")]
    positional: list[str] = []
    named: dict[str, str] = {}
    for part in split_top_level(inner):
        part = part.strip()
        if not part:
            continue
        m = re.match(r"^(\w+)\s*=\s*(.+)$", part, re.S)
        if m and not part.startswith('"'):
            named[m.group(1)] = m.group(2).strip()
        else:
            positional.append(part)
    return positional, named


def as_str(raw: str) -> str:
    raw = (raw or "").strip()
    if len(raw) >= 2 and raw[0] == '"' and raw[-1] == '"':
        return unescape_csharp(raw[1:-1])
    return raw


def load_command_names() -> dict[str, str]:
    """枚举成员名 → EnumValue 真实命令名（如 RateLimitOptions → rate-limit-options）。"""
    if not ENUM_FILE.exists():
        raise SystemExit(f"命令名枚举文件缺失: {ENUM_FILE.as_posix()}（路径漂移会让生成结果退化成类名 kebab，禁止静默继续）")
    text = ENUM_FILE.read_text(encoding="utf-8", errors="replace")
    mapping: dict[str, str] = {}
    for m in re.finditer(r'\[EnumValue\("([^"]+)"\)\]\s*(\w+)', text):
        mapping[m.group(2)] = m.group(1)
    return mapping


def resolve_command_name(raw: str, name_map: dict[str, str]) -> str:
    raw = (raw or "").strip()
    if raw.startswith('"'):
        return as_str(raw)
    tail = raw.rsplit(".", 1)[-1]
    if tail in name_map:
        return name_map[tail]
    return CAMEL_RE.sub("-", tail).lower()


def resolve_category(raw: str | None) -> str:
    if not raw:
        return "Other"
    return raw.rsplit(".", 1)[-1].strip()


def parse_enum_values(raw: str) -> list[str]:
    literals = re.findall(r'"([^"]*)"', raw)
    if literals:
        return [unescape_csharp(v) for v in literals]
    constants = re.findall(r"[A-Za-z_][\w.]*", raw)
    return [c.rsplit(".", 1)[-1].lower() for c in constants if "." in c]


def parse_alias_values(raw: str) -> list[str]:
    literals = re.findall(r'"([^"]*)"', raw)
    return [unescape_csharp(v) for v in literals]


def parse_command(path: Path, text: str, name_map: dict[str, str]) -> list[dict]:
    commands: list[dict] = []
    for start, end, body in find_attributes(text, "ChatCommand"):
        positional, named = parse_attr(body)
        name = resolve_command_name(named.get("Name") or (positional[0] if positional else ""), name_map)
        if not name:
            continue

        # 该命令类的 [ChatCommandArg] 位于 ChatCommand 特性之后、class 声明之前
        class_match = re.search(r"\b(?:class|record)\s+\w+", text[end:])
        block_end = end + (class_match.start() if class_match else 0)
        args: list[dict] = []
        for _, _, arg_body in find_attributes(text[end:block_end], "ChatCommandArg"):
            arg_pos, arg_named = parse_attr(arg_body)
            if not arg_pos:
                continue
            args.append({
                "name": as_str(arg_pos[0]),
                "type": as_str(arg_named.get("Type", "string")),
                "description": as_str(arg_named.get("Description", "")),
                "required": arg_named.get("Required", "").strip().lower() == "true",
                "default": as_str(arg_named.get("Default", "")),
                "enum": parse_enum_values(arg_named.get("Enum", "")) if arg_named.get("Enum") else [],
            })

        commands.append({
            "name": name,
            "description": as_str(named.get("Description", "")),
            "usage": as_str(named.get("Usage", "")),
            "category": resolve_category(named.get("Category")),
            "argument_hint": as_str(named.get("ArgumentHint", "")),
            "aliases": parse_alias_values(named.get("Aliases", "")) if named.get("Aliases") else [],
            "is_hidden": named.get("IsHidden", "").strip().lower() == "true",
            "expose_to_mcp": named.get("ExposeToMcp", "").strip().lower() == "true",
            "args": args,
            "source": f"{path.as_posix()}:{text.count(chr(10), 0, start) + 1}",
        })
    return commands


def scan_modules(scan_root: Path, name_map: dict[str, str]) -> list[dict]:
    commands: list[dict] = []
    for path in sorted(scan_root.rglob("*.cs")):
        if ".g.cs" in path.name or "/obj/" in path.as_posix() or "/bin/" in path.as_posix():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "[ChatCommand(" not in text:
            continue
        commands.extend(parse_command(path, text, name_map))
    # 同名命令只保留首条（分部类/重复特性）
    seen: set[str] = set()
    unique: list[dict] = []
    for cmd in commands:
        if cmd["name"] in seen:
            continue
        seen.add(cmd["name"])
        unique.append(cmd)
    return sorted(unique, key=lambda c: (c["category"], c["name"]))


def render_help(commands: list[dict]) -> str:
    lines = [f"jcc-tool-slash — 斜杠命令节点（{len(commands)} 个命令）", "",
             "用法:", "  jcc-tool-slash.exe <命令名> [--参数 值 ...]", "  jcc-tool-slash.exe -h", "  jcc-tool-slash.exe <命令名> -h", "",
             "命令列表（说明复用自 [ChatCommand] 特性，按分类分组）:"]
    by_cat: dict[str, list[dict]] = {}
    for cmd in commands:
        by_cat.setdefault(cmd["category"], []).append(cmd)
    for category in sorted(by_cat):
        group = by_cat[category]
        lines.append("")
        lines.append(f"[{category}] ({len(group)} 个)")
        for cmd in group:
            hidden = " (隐藏)" if cmd["is_hidden"] else ""
            lines.append(f"  /{cmd['name']:<30} {cmd['description']}{hidden}")
    lines.append("")
    lines.append("单个命令的参数说明:")
    for cmd in commands:
        lines.append("")
        lines.append(f"  /{cmd['name']} — {cmd['description']}")
        if cmd["usage"]:
            lines.append(f"    用法: {cmd['usage']}")
        if cmd["aliases"]:
            lines.append(f"    别名: {', '.join('/' + a for a in cmd['aliases'])}")
        if not cmd["args"]:
            hint = f"  参数提示: {cmd['argument_hint']}" if cmd["argument_hint"] else ""
            lines.append(f"    (无结构化参数){hint}")
        for arg in cmd["args"]:
            flag = "必填" if arg["required"] else "可选"
            extra = ""
            if arg["enum"]:
                extra += f"  取值: {'/'.join(arg['enum'])}"
            if arg["default"]:
                extra += f"  默认={arg['default']}"
            lines.append(f"    --{arg['name']:<24} {arg['type']:<9} {flag:<4} {arg['description']}{extra}")
    lines.append("")
    lines.append("描述来源: 现有 [ChatCommand] / [ChatCommandArg] 特性，由本脚本在编译期提取。")
    return "\n".join(lines) + "\n"


def render_csproj() -> str:
    return """<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="../../cli_node.props" />

  <PropertyGroup>
    <AssemblyName>jcc-tool-slash</AssemblyName>
    <RootNamespace>JoinCode.CliNode.Slash</RootNamespace>
    <Description>斜杠命令独立 CLI.exe — -h 复用现有 [ChatCommand] 特性说明</Description>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="help.txt" LogicalName="JoinCode.CliNodes.Groups.slash.help.txt" />
    <EmbeddedResource Include="$(RepoRoot)resources/tools/policies/slash.yaml" LogicalName="JoinCode.CliNodes.Groups.slash.policy.yaml" />
    <AdditionalFiles Include="$(RepoRoot)resources/tools/policies/slash.yaml" />
    <ProjectReference Include="$(RepoRoot)modules/generators/cli_permission_analyzer/CliPermission.Analyzer.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
"""


def collect_local_ids(out_dir: Path) -> set[str]:
    """从 private/*.cs 的 FrozenDictionary 注册项反推本地命令 id。

    斜杠命令在 exe 内部带前导 '/'（Resolve 收到的是规范名 /version），
    而 node.yaml 的命令 id 不带斜杠，故反推时去掉前导 '/'。
    """
    private_dir = out_dir / "private"
    if not private_dir.exists():
        return set()
    ids: set[str] = set()
    pattern = re.compile(r'["\']/?([A-Za-z_][A-Za-z0-9_-]*)["\']\]?\s*=\s*\w+')
    for cs in sorted(private_dir.glob("*.cs")):
        body = cs.read_text(encoding="utf-8", errors="replace")
        for m in pattern.finditer(body):
            ids.add(m.group(1))
    return ids


def render_node_yaml(commands: list[dict], local_ids: set[str] | None = None) -> str:
    """节点自描述 — 让 jcc tool tree/find/help 能发现斜杠命令 exe，并记录执行方式。

    默认全部 host（会话绑定命令）；private/*.cs 里注册了本地实现的命令标 local。
    本地实现表以 private/*.cs 的字典 key 为准（反推），不在此硬编码。
    """
    local_ids = local_ids or set()
    categories = sorted({c["category"] for c in commands})
    summaries = "; ".join(yaml_scalar(c["description"]) for c in commands[:3])
    header = (
        f"# 由 generate_slash_cli_node.py 从现有 [ChatCommand] / [ChatCommandArg] 特性生成\n"
        f"id: slash\n"
        f"name: jcc-tool-slash\n"
        f"summary: \"斜杠命令节点 — {len(commands)} 个命令，{len(categories)} 个分类；{summaries}\"\n"
        f"category: slash\n"
        f"exe: jcc-tool-slash.exe\n"
        f"tags: [{', '.join(categories[:8])}]\n"
        f"tool_count: {len(commands)}\n"
        f"local_tool_count: {len(local_ids)}\n"
        f"parameters: []\n"
        f"tools:\n"
    )
    known_names = {c["name"] for c in commands}
    unknown = local_ids - known_names
    if unknown:
        raise SystemExit(f"slash 私有实现注册了未声明的命令 id: {sorted(unknown)}")
    body = "".join(
        f"  - id: {yaml_id(c['name'])}\n"
        f"    mode: {'local' if c['name'] in local_ids else 'host'}\n"
        + (f"    aliases: [{', '.join(yaml_id(a) for a in c['aliases'])}]\n" if c.get("aliases") else "")
        for c in sorted(commands, key=lambda item: item["name"])
    )
    return header + body


SAFE_YAML_ID = re.compile(r"^[A-Za-z_][A-Za-z0-9_-]*$")


def yaml_id(value: str) -> str:
    """安全标量 — 命令名可能含 ? 等 YAML 指示符（如 `??`），必须加引号否则解析成映射键。"""
    return value if SAFE_YAML_ID.match(value) else '"' + value.replace('"', '\\"') + '"'


def yaml_scalar(text: str) -> str:
    """压成单行并去掉会破坏 YAML 双引号标量的字符。"""
    return (text or "").replace('"', "'").replace("\n", " ").strip()


def main() -> int:
    parser = argparse.ArgumentParser(description="生成 jcc-tool-slash 独立 CLI 节点")
    parser.add_argument("--scan", default="modules", help="扫描根目录（默认 modules）")
    parser.add_argument("--out", default=str(OUT_DIR), help="输出目录")
    parser.add_argument("--dump", action="store_true", help="把解析结果以 JSON 打到 stdout")
    args = parser.parse_args()

    scan_root = (REPO_ROOT / args.scan).resolve()
    out_dir = Path(args.out)
    name_map = load_command_names()
    commands = scan_modules(scan_root, name_map)

    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "help.txt").write_text(render_help(commands), encoding="utf-8")
    local_ids = collect_local_ids(out_dir)
    (out_dir / "node.yaml").write_text(render_node_yaml(commands, local_ids), encoding="utf-8")
    (out_dir / "jcc_tool_slash.csproj").write_text(render_csproj(), encoding="utf-8")

    local_ids = local_ids or set()
    categories = sorted({c["category"] for c in commands})
    report = {
        "commands": len(commands),
        "with_args": sum(1 for c in commands if c["args"]),
        "categories": len(categories),
        "hidden": sum(1 for c in commands if c["is_hidden"]),
        "outputs": [
            (out_dir / "help.txt").as_posix(),
            (out_dir / "node.yaml").as_posix(),
            (out_dir / "jcc_tool_slash.csproj").as_posix(),
        ],
    }
    if args.dump:
        report["items"] = commands
    print(json.dumps(report, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
