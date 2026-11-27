#!/usr/bin/env python3
"""ADR 0157 v2 — 按 ToolCategory 分组，从现有 [McpTool] 特性生成分组 CLI.exe 的帮助文本与索引。

设计边界（重要）:
  * 只读扫描源 .cs，绝不修改、移动或删除任何现有文件
  * 不改动任何 [McpTool] / [McpToolParameter] 特性 —— 描述直接复用现有 MCP 说明
  * 一个分组 = 一个 CLI.exe；参数描述从特性提取后预渲染为 help.txt 内嵌资源
  * 生成的 node.yaml 是分组级索引，供 jcc tool find / tree 下钻定位到分组 exe
  * 不生成假的可执行实现；工具调用由各分组 exe 按批次接入

用法:
  python scripts/migrate_mcp_tools_to_cli_nodes.py --scan <dir> --out <dir> [--category git] [--limit N]
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
GROUP_MAP_PATH = REPO_ROOT / "resources/tools/group_map.yaml"
WELL_KNOWN_PARAM_PATH = REPO_ROOT / "modules/abstractions/abstractions_primitives_utils/src/constants/params/WellKnownParam.cs"

DISPATCH_RE = re.compile(r"\[McpToolDispatch\(ToolCategory\.(?P<cat>\w+)")
TOOL_RE = re.compile(
    r"\[McpTool\(\s*(?P<name>\"(?:\\.|[^\"\\])*\"|[A-Za-z0-9_.]+)\s*,\s*"
    r"(?P<summary>\"(?:\\.|[^\"\\])*\"|[A-Za-z0-9_.]+)\s*"
    r"(?:,\s*\"(?P<category>(?:\\.|[^\"\\])*)\")?[^\[\]]*\)\]",
    re.S,
)
PARAM_RE = re.compile(
    r"\[McpToolParameter\(\s*(?:\"(?P<desc>(?:\\.|[^\"\\])*)\"|WellKnownParam\.(?P<well_known>[A-Za-z_][A-Za-z0-9_]*))\s*(?P<rest>(?:[^\[\]]|\[[^\[\]]*\])*)\)\]\s*"
    r"(?P<type>[A-Za-z0-9_<>?\[\],. ]+?)\s+(?P<pname>@?[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?:=\s*(?P<default>[^,)\n]+))?",
    re.S,
)
CAMEL_RE = re.compile(r"(?<!^)(?=[A-Z])")

# [McpToolOptions] 记录型参数：10 个 search 工具把参数收在 GrepSearchOptions 之类的 record 里，
# 只扫方法签名会得到「(无参数)」的假帮助，LLM 照此调用必然缺参。
MCPTOOL_OPTIONS_RE = re.compile(r"\[McpToolOptions\]\s*(?P<type>[A-Za-z_][A-Za-z0-9_.]*)")
RECORD_RE = re.compile(r"(?:public|internal)\s+(?:sealed\s+|partial\s+)*record\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)")
PROPERTY_PARAM_RE = re.compile(
    r"\[McpToolParameter\(\s*(?:\"(?P<desc>(?:\\.|[^\"\\])*)\"|WellKnownParam\.(?P<well_known>[A-Za-z_][A-Za-z0-9_]*))\s*(?P<rest>(?:[^\[\]]|\[[^\[\]]*\])*)\)\]\s*"
    r"public\s+(?P<req>required\s+)?(?P<type>[A-Za-z0-9_<>?\[\],. ]+?)\s+(?P<pname>@?[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;",
    re.S,
)
ALIASES_RE = re.compile(r"Aliases\s*=\s*\[(?P<items>[^\]]*)\]")


def is_source_file(path: Path) -> bool:
    """生产源文件 — 测试项目里的 [McpTool] 桩（如 test_tool）不能进 CLI 目录。"""
    if ".g.cs" in path.name:
        return False
    return not any(part.endswith(".tests") or part.endswith("_tests") or part in {"test", "tests"} for part in path.parts)


def load_group_map() -> tuple[dict[str, str], str]:
    """读取 group_map.yaml — 极简解析，不引入 PyYAML 依赖。"""
    if not GROUP_MAP_PATH.is_file():
        raise FileNotFoundError(f"分组映射表不存在: {GROUP_MAP_PATH}")
    mapping: dict[str, str] = {}
    default = "system"
    group: str | None = None
    section = "groups"
    for line in GROUP_MAP_PATH.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = len(line) - len(line.lstrip())
        if indent == 0:
            if stripped.startswith("default:"):
                default = stripped.split(":", 1)[1].strip()
            elif stripped.startswith("delegates:"):
                section = "delegates"
            else:
                section = "groups"
            group = None
            continue
        if section == "delegates":
            continue
        if indent == 2 and stripped.endswith(":") and not stripped.startswith("-"):
            group = stripped[:-1].strip()
            continue
        if stripped.startswith("- ") and group:
            mapping[stripped[2:].strip().lower()] = group
    if not mapping:
        raise ValueError(f"分组映射表 '{GROUP_MAP_PATH}' 的 groups 段为空")
    return mapping, default


def load_delegates() -> dict[str, dict[str, str]]:
    """读取 group_map.yaml 的 delegates 段: 组名 → {exe, verb, reason}。

    段格式固定为三级（组名 / 键值），与 groups 段的列表格式互不干扰。
    """
    if not GROUP_MAP_PATH.is_file():
        return {}
    delegates: dict[str, dict[str, str]] = {}
    section = "groups"
    current: str | None = None
    for line in GROUP_MAP_PATH.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = len(line) - len(line.lstrip())
        if indent == 0:
            section = "delegates" if stripped.startswith("delegates:") else "groups"
            current = None
            continue
        if section != "delegates":
            continue
        if indent == 2 and stripped.endswith(":"):
            current = stripped[:-1].strip()
            delegates[current] = {}
            continue
        if indent >= 4 and current and ":" in stripped:
            key, value = stripped.split(":", 1)
            delegates[current][key.strip()] = value.strip()
    return delegates


def load_internal_tools() -> frozenset[str]:
    """读取 group_map.yaml 的 internal_tools 段 — jcc 内部基础工具 id。

    这些工具由宿主内核直接注册并进入首轮 tools 字段，不再作为独立 exe 的子命令出现。
    不在这里登记、却想在 node.yaml 里隐藏工具的做法会造成「声明 vs 现实」漂移。
    """
    if not GROUP_MAP_PATH.is_file():
        return frozenset()
    tools: set[str] = set()
    section = "groups"
    for line in GROUP_MAP_PATH.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if len(line) - len(line.lstrip()) == 0:
            section = "internal_tools" if stripped.startswith("internal_tools:") else "groups"
            continue
        if section == "internal_tools" and stripped.startswith("- "):
            tools.add(stripped[2:].strip().lower())
    return frozenset(tools)


def canonical_group(raw: str, mapping: dict[str, str], default: str) -> str:
    return mapping.get(raw.strip().lower(), default)


def unescape_csharp(raw: str) -> str:
    """还原 C# 字符串字面量里的转义，避免把 \\\" 当成字符串结尾。"""
    return (raw.replace(r'\"', '"').replace(r"\\", "\\")
            .replace(r"\n", " ").replace(r"\t", " ").replace(r"\r", " "))


def yaml_escape(value: str) -> str:
    """YAML 双引号标量转义 — 引号、反斜杠、换行都必须处理。"""
    return value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ").replace("\r", " ").strip()


def to_snake(raw: str) -> str:
    cleaned = raw.strip().strip('"')
    tail = cleaned.rsplit(".", 1)[-1]
    return re.sub(r"[^a-z0-9_]+", "_", CAMEL_RE.sub("_", tail).lower()).strip("_")


# [EnumValue("read")] FileRead —— 运行时的工具名是特性里的字符串，不是枚举成员的驼峰。
# 只按成员名转驼峰会造出「目录里有、注册表里没有」的假 id（实测 file_read / code_index_stats /
# mcp_auth_o_auth2 共 33 个），jcc tool call 一律报「分组内没有工具」。
ENUM_DECL_RE = re.compile(r"\benum\s+(?P<name>[A-Za-z_]\w*)")
ENUM_MEMBER_RE = re.compile(
    r"\[EnumValue\(\s*\"(?P<value>(?:\\.|[^\"\\])*)\"\s*\)\]"
    r"(?:\s*\[[^\]]*\])*"          # 中间常夹着 [SecurityClass(...)] 等特性，不能只允许空白
    r"\s*(?P<member>[A-Za-z_]\w*)",
    re.S,
)


def collect_enum_values(root: Path) -> tuple[dict[str, dict[str, str]], dict[str, str]]:
    """扫描全仓枚举 — 返回 (枚举类型 → {成员: 值}, 成员 → 值 的全局兜底表)。

    <para>兜底表只收「同名成员只有一个候选值」的项：不同枚举里会有同名成员（如
    <c>AllowedPromptTool.Bash="Bash"</c> 与 <c>ShellToolName.Bash="bash"</c>），
    取第一个会把工具名解析成 "Bash"。真出现歧义就交给调用方退回驼峰转蛇形并留 warn。</para>
    """
    by_type: dict[str, dict[str, str]] = {}
    candidates: dict[str, set[str]] = {}
    for path in sorted(root.rglob("*.cs")):
        if not is_source_file(path):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "[EnumValue(" not in text:
            continue
        for decl in ENUM_DECL_RE.finditer(text):
            block = extract_braced_block(text, decl.end())
            if not block or "[EnumValue(" not in block:
                continue
            members = {m.group("member"): unescape_csharp(m.group("value")) for m in ENUM_MEMBER_RE.finditer(block)}
            if not members:
                continue
            by_type.setdefault(decl.group("name"), {}).update(members)
            for member, value in members.items():
                candidates.setdefault(member, set()).add(value)
    return by_type, {member: next(iter(values)) for member, values in candidates.items() if len(values) == 1}


# 少数 [McpTool] 的描述不是字符串字面量，而是本地化键（StringKey.ListPeersDesc）。
# 只认字面量会让这些工具从目录里整个消失（实测 list_peers / subscribe_pr）。
STRING_KEY_RE = re.compile(
    r'defaultEntries\[StringKey\.(?P<key>\w+)\]\s*=\s*"(?P<value>(?:\\.|[^"\\])*)"',
    re.S,
)


def collect_string_keys(root: Path) -> dict[str, str]:
    """扫描本地化初始化器 — StringKey 成员 → 英文默认文案。"""
    result: dict[str, str] = {}
    for path in sorted(root.rglob("*.cs")):
        if not is_source_file(path):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "defaultEntries[StringKey." not in text:
            continue
        for m in STRING_KEY_RE.finditer(text):
            result.setdefault(m.group("key"), unescape_csharp(m.group("value")))
    return result


def resolve_tool_id(raw_name: str, enums: tuple[dict[str, dict[str, str]], dict[str, str]]) -> str:
    """把 [McpTool] 的名字参数还原成运行时注册名。

    <para>三种写法：字符串字面量（"gh_api"）、枚举成员（CodeToolNameEnumConstants.CodeIndexStats）、
    裸成员名。枚举写法必须查 [EnumValue]；查不到就 fail-fast —— 退回驼峰转蛇形正是当年造出
    33 个假 id（file_read / code_index_stats / mcp_auth_o_auth2）的根因，静默生成比不生成更糟。</para>
    """
    by_type, flat = enums
    cleaned = raw_name.strip()
    if cleaned.startswith('"'):
        return to_snake(cleaned)
    if "." in cleaned:
        type_name, member = cleaned.rsplit(".", 1)
        # 常见写法是 XxxToolNameEnumConstants（包装类）指向 enum XxxToolName —— 先剥后缀再按类型查
        for candidate_type in (type_name, type_name.removesuffix("EnumConstants")):
            if (value := by_type.get(candidate_type, {}).get(member)) is not None:
                return value
        if (value := flat.get(member)) is not None:
            return value
    elif (value := flat.get(cleaned)) is not None:
        return value
    raise SystemExit(
        f"无法解析工具名 {cleaned} 的 [EnumValue]：会让 node.yaml 出现运行时不存在的假 id。"
        f"请补 [EnumValue(\"...\")] 或确认扫描目录包含该枚举定义。"
    )


def resolve_summary(raw: str, string_keys: dict[str, str]) -> str:
    """摘要可能是字符串字面量，也可能是本地化键（StringKey.X）。"""
    if raw.startswith('"'):
        return unescape_csharp(raw.strip('"')).strip()
    key = raw.rsplit(".", 1)[-1]
    if key not in string_keys:
        raise SystemExit(f"工具描述 {raw!r} 未能解析为本地化文案；请检查 LocalizerInitializer 的 defaultEntries")
    return string_keys[key].strip()


def normalize_type(raw_type: str) -> str:
    t = raw_type.replace(" ", "").lower()
    if "string" in t:
        return "string"
    if "bool" in t:
        return "boolean"
    if "int" in t:
        return "integer"
    if "double" in t or "float" in t or "decimal" in t:
        return "number"
    if "[]" in t or "list<" in t or "array" in t or "ienumerable" in t:
        return "array"
    return "string"


def clean_default(raw: str | None) -> str:
    """方法签名里的 = null / = default 不是真实默认值，不应写进帮助文本。"""
    value = (raw or "").strip().strip('"')
    return "" if value in {"null", "default", "null!", "", "0"} else value


def extract_aliases(rest: str) -> str:
    """从特性剩余参数里取 Aliases（如 -i / -A），写成帮助里的「别名」提示。"""
    match = ALIASES_RE.search(rest)
    if not match:
        return ""
    items = [item.strip().strip('"') for item in match.group("items").split(",")]
    kept = [item for item in items if item]
    return " / ".join(kept)


def collect_options_records(root: Path) -> dict[str, list[dict]]:
    """收集全仓 [McpToolOptions] 记录类型的属性作为工具参数。

    这些 record 的属性才是真实入参（GrepSearchOptions.Pattern 等），
    只扫方法签名会把 8 个 search 工具渲染成「(无参数)」——那是假帮助，不是省略。
    """
    registry: dict[str, list[dict]] = {}
    for path in sorted(root.rglob("*.cs")):
        if not is_source_file(path):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "[McpToolParameter" not in text:
            continue
        for record in RECORD_RE.finditer(text):
            block = extract_braced_block(text, record.end())
            if not block or "[McpToolParameter" not in block:
                continue
            params = [parameter_from_match(prop, property_parameter=True)
                      for prop in PROPERTY_PARAM_RE.finditer(block)]
            if params:
                registry[record.group("name")] = params
    return registry


def _default_value(rest: str) -> str | None:
    match = re.search(r"DefaultValue\s*=\s*\"(?P<value>(?:\\.|[^\"\\])*)\"", rest)
    return match.group("value") if match else None


def load_well_known_params(path: Path = WELL_KNOWN_PARAM_PATH) -> dict[str, dict[str, object]]:
    text = path.read_text(encoding="utf-8")
    pattern = re.compile(
        r"\[ParamMeta\(\"(?P<desc>(?:\\.|[^\"\\])*)\"(?P<rest>[^)]*)\)\]\s*"
        r"(?P<member>[A-Za-z_][A-Za-z0-9_]*)\s*,",
        re.S,
    )
    metadata: dict[str, dict[str, object]] = {}
    for match in pattern.finditer(text):
        rest = match.group("rest")
        metadata[match.group("member")] = {
            "description": unescape_csharp(match.group("desc")),
            "required": "Required = false" not in rest and "Required=false" not in rest,
            "default": clean_default(_default_value(rest)),
        }
    return metadata


WELL_KNOWN_PARAMS = load_well_known_params()


def parameter_from_match(match: re.Match[str], *, property_parameter: bool = False) -> dict[str, object]:
    rest = match.group("rest") or ""
    name = match.group("pname").lstrip("@")
    if property_parameter:
        name = to_snake(name)

    well_known = match.group("well_known")
    if well_known:
        metadata = WELL_KNOWN_PARAMS[well_known]
        required = metadata["required"]
        if "Required = false" in rest or "Required=false" in rest:
            required = False
        elif "Required = true" in rest or "Required=true" in rest:
            required = True
        return {
            "name": name,
            "type": normalize_type(match.group("type")),
            "description": metadata["description"],
            "required": required,
            "default": clean_default(_default_value(rest)) or metadata["default"],
            "aliases": extract_aliases(rest),
        }

    if property_parameter:
        required = bool(match.group("req")) or "Required = true" in rest
        if "Required = false" in rest or "Required=false" in rest:
            required = False
    else:
        required = "Required = false" not in rest and "Required=false" not in rest
    return {
        "name": name,
        "type": normalize_type(match.group("type")),
        "description": unescape_csharp(match.group("desc").strip()),
        "required": required,
        "default": clean_default(_default_value(rest) if property_parameter else match.group("default")),
        "aliases": extract_aliases(rest),
    }


def extract_braced_block(text: str, start: int) -> str:
    """从 start 起找到第一个 '{' 并返回配平的块内容（不处理字符串内花括号，够用即可）。"""
    open_index = text.find("{", start)
    if open_index < 0:
        return ""
    depth = 0
    for index in range(open_index, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[open_index + 1:index]
    return ""


def parse_file(path: Path, group_map: dict[str, str], default_group: str,
               options_records: dict[str, list[dict]] | None = None,
               enums: tuple[dict[str, dict[str, str]], dict[str, str]] | None = None,
               string_keys: dict[str, str] | None = None) -> list[dict]:
    text = path.read_text(encoding="utf-8", errors="replace")
    # 注释里的示例（/// [McpTool("gh_run_view", ...)]）不是声明，抹掉整行注释。
    # 不能用正则删 /* */ 块：字符串里的 glob（"**/*.cs"）会被当成注释起点，吞掉整段真代码。
    text = "\n".join("" if line.lstrip().startswith("//") else line for line in text.splitlines())
    dispatch = DISPATCH_RE.search(text)
    dispatch_category = dispatch.group("cat").lower() if dispatch else "general"
    matches = list(TOOL_RE.finditer(text))
    tools: list[dict] = []
    for index, match in enumerate(matches):
        block_end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[match.start():block_end]
        raw_name = match.group("name")
        params = [parameter_from_match(parameter) for parameter in PARAM_RE.finditer(block)]
        # 参数收在 [McpToolOptions] record 里的工具（grep / search_* 等），把 record 的属性并进来。
        parameter_names = {parameter["name"] for parameter in params}
        for option in MCPTOOL_OPTIONS_RE.finditer(block):
            params.extend(parameter for parameter in (options_records or {}).get(option.group("type"), [])
                          if parameter["name"] not in parameter_names)
        tools.append({
            "raw_name": raw_name,
            "id": resolve_tool_id(raw_name, enums or ({}, {})),
            "summary": resolve_summary(match.group("summary").strip(), string_keys or {}),
            "category": canonical_group(match.group("category") or dispatch_category, group_map, default_group),
            "source": f"{path.as_posix()}:{text.count(chr(10), 0, match.start()) + 1}",
            "parameters": params,
        })
    return tools


def render_group_help(category: str, exe: str, tools: list[dict]) -> str:
    lines = [f"jcc-tool-{category} — {category} 工具分组（{len(tools)} 个工具）", "",
             "用法:", f"  {exe} <工具名> [参数...]", f"  {exe} -h", f"  {exe} <工具名> -h", "",
             "工具列表（说明复用自 [McpTool] 特性）:"]
    for tool in tools:
        lines.append(f"  {tool['id']:<34} {tool['summary']}")
    lines.append("")
    lines.append("单个工具的参数说明:")
    for tool in tools:
        lines.append("")
        lines.append(f"  {tool['id']} — {tool['summary']}")
        if not tool["parameters"]:
            lines.append("    (无参数)")
        for p in tool["parameters"]:
            flag = "必填" if p["required"] else "可选"
            default = f"  默认={p['default']}" if p["default"] else ""
            aliases = f"  别名={p['aliases']}" if p.get("aliases") else ""
            lines.append(f"    --{p['name']:<26} {p['type']:<9} {flag:<4} {p['description']}{default}{aliases}")
    lines.append("")
    lines.append("描述来源: 现有 [McpTool] / [McpToolParameter] 特性，由本脚本在编译期提取。")
    return "\n".join(lines) + "\n"


def render_group_program(category: str, delegate: dict[str, str] | None = None) -> str:
    """通用分组 exe 入口 —— 只声明身份，行为统一在契约层 CliNodeRunner。

    delegate 非空时生成「委托独立 exe」的声明：该组工具由另一个已存在的独立可执行文件实现，
    分组 exe 只负责转发，不经过宿主。
    """
    ns = "".join(part.capitalize() for part in category.split("_"))
    delegate_lines = ""
    if delegate:
        exe = delegate.get("exe", "")
        verb = delegate.get("verb", "--call-tool")
        reason = delegate.get("reason", "").replace('"', "'")
        delegate_lines = (
            f'        DelegateExecutable = "{exe}",\n'
            f'        DelegateVerb = "{verb}",\n'
            f'        DelegateReason = "{reason}",\n'
        )
    host_reason = (
        "该组工具由委托的独立 exe 执行，未配置该 exe 时无法运行。" if delegate
        else f"{category} 组暂无本地实现，工具依赖宿主持有的引擎服务与共享状态。"
    )
    mode_note = (
        "该组工具委托独立 exe 执行（不经宿主）；" if delegate
        else "Resolve 返回 null 表示该工具走宿主转发；逐组做实时把它换成私有实现表的 Find。"
    )
    return f'''// GENERATED BY migrate_mcp_tools_to_cli_nodes.py — DO NOT EDIT
// 本文件由扫描器幂等覆盖；如需分组本地实现，请删除本文件并手写（参考 groups/search/）。
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.{ns};

/// <summary>
/// {category} 分组独立 CLI.exe — 由 migrate_mcp_tools_to_cli_nodes.py 生成。
/// <para>入口行为（-h / --list-local / 参数解析 / 本地分发 / 转发 / 退出码）统一在
/// <see cref="CliNodeRunner"/>，本文件只声明分组身份与本地实现表。</para>
/// <para>{mode_note}</para>
/// -h 文本来自现有 [McpTool] 特性，不在生成物里重复维护说明。
/// </summary>
internal static class Program {{
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {{
        GroupId = "{category}",
        Title = "{category} 分组",
        HelpResource = "JoinCode.CliNodes.Groups.{category}.help.txt",
        Resolve = static _ => null,
        LocalToolIds = static () => [],
{delegate_lines}        HostReason = "{host_reason}"
    }}, args);
}}
'''


def render_group_csproj(category: str) -> str:
    """分组 exe 项目文件 — 统一 Import cli_node.props，内嵌 help.txt。"""
    return f'''<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="../../cli_node.props" />

  <PropertyGroup>
    <AssemblyName>jcc-tool-{category}</AssemblyName>
    <RootNamespace>JoinCode.CliNode.{''.join(p.capitalize() for p in category.split('_'))}</RootNamespace>
    <Description>{category} 工具分组独立 CLI.exe — -h 复用现有 MCP 特性说明</Description>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="help.txt" LogicalName="JoinCode.CliNodes.Groups.{category}.help.txt" />
  </ItemGroup>

</Project>
'''


def render_group_index(category: str, exe: str, tools: list[dict], local_ids: set[str],
                       delegate: dict[str, str] | None = None) -> str:
    summaries = "; ".join(yaml_escape(t["summary"]) for t in tools[:3])
    tags = ", ".join(sorted({t["id"].split("_")[0] for t in tools})[:8])
    header = (
        f"# 由 migrate_mcp_tools_to_cli_nodes.py 从现有 [McpTool] 特性按分组生成\n"
        f"# 子工具的 mode 由各组 private/*.cs 的实际注册表反推：local=本 exe 内实现，\n"
        f"# delegate=委托另一个独立 exe（不经宿主），host=转发宿主执行。\n"
        f"id: {category}\n"
        f"name: jcc-tool-{category}\n"
        f"summary: \"{summaries}\"\n"
        f"category: {category}\n"
        f"exe: {exe}\n"
        f"tags: [{tags}]\n"
        f"tool_count: {len(tools)}\n"
        f"local_tool_count: {len(local_ids)}\n"
        f"parameters: []\n"
        f"tools:\n"
    )
    def mode_of(tool_id: str) -> str:
        if tool_id in local_ids:
            return "local"
        return "delegate" if delegate else "host"

    lines = [f"  - id: {t['id']}\n    mode: {mode_of(t['id'])}\n" for t in tools]
    return header + "".join(lines)


REGISTRY_DECL_RE = re.compile(
    r"new\s+Dictionary<string,\s*.+?>\s*\([^)]*\)\s*\{",
)
REGISTRY_ENTRY_RE = re.compile(
    r'\["(?P<id>[A-Za-z_][A-Za-z_0-9]*)"\]\s*=\s*(?P<handler>[A-Za-z_][A-Za-z0-9_.]*)\s*(?=[,}\s])',
)


def collect_local_ids(target_dir: Path) -> set[str]:
    """从分组私有实现里反推本地已实现的工具 id。

    只认「注册表字典字面量」里的键：`new Dictionary<string, X>(...) { ["id"] = Handler, ... }`。
    这是「代码里真实现了什么」，而不是另写一份清单自称实现了什么；手工组才有 private/。

    不能只按 `["k"] = 标识符` 全文件扫：notebook 实现里的 `new JsonObject { ["cell_type"] = ... }`
    会被误判成工具 id，直接把生成流程卡死。
    """
    private_dir = target_dir / "private"
    if not private_dir.exists():
        return set()
    ids: set[str] = set()
    for cs in sorted(private_dir.glob("*.cs")):
        body = cs.read_text(encoding="utf-8", errors="replace")
        for decl in REGISTRY_DECL_RE.finditer(body):
            block = extract_braced_block(body, decl.end() - 1)
            if not block:
                continue
            ids.update(m.group("id") for m in REGISTRY_ENTRY_RE.finditer(block))
    return ids


def dedupe_tools(tools: list[dict]) -> list[dict]:
    """按工具 id 去重，保留首次出现的声明。"""
    seen: set[str] = set()
    result: list[dict] = []
    for tool in tools:
        if not tool["id"] or tool["id"] in seen:
            continue
        seen.add(tool["id"])
        result.append(tool)
    return result


GENERATED_MARK = "// GENERATED BY migrate_mcp_tools_to_cli_nodes.py"
HANDCRAFTED_MARK = "// HANDCRAFTED"


def is_generated_or_missing(path: Path) -> bool:
    """生成物可幂等覆盖；手工实现（git / file 组）绝不覆盖。"""
    if not path.exists():
        return True
    head = path.read_text(encoding="utf-8", errors="replace")[:2000]
    if HANDCRAFTED_MARK in head:
        return False
    # 旧版生成物没有标记，但含转发模板独有的 ForwardAsync；手工实现需显式标 HANDCRAFTED。
    return GENERATED_MARK in head or "ForwardAsync" in head


def prune_stale_groups(out_root: Path, keep: set[str]) -> list[str]:
    """本次未生成的旧分组目录移到项目根 .xxx/ 归档，不删除任何文件（幂等、可追溯）。

    只归档「本脚本自己生成过」的目录：别的生成器（generate_slash_cli_node.py 的 slash 组）
    产物不属于本脚本的清理范围，误清会把整个斜杠命令目录搬走。
    """
    archive = REPO_ROOT / ".xxx"
    moved: list[str] = []
    if not out_root.is_dir():
        return moved
    for child in sorted(out_root.iterdir()):
        if not child.is_dir() or child.name in keep or not (child / "node.yaml").exists():
            continue
        head = (child / "node.yaml").read_text(encoding="utf-8", errors="replace")[:200]
        if GENERATED_MARK.replace("// ", "# ") not in head and "migrate_mcp_tools_to_cli_nodes" not in head:
            continue
        archive.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%d%H%M%S")
        dest = archive / f"cli_nodes_group_{child.name}_{stamp}.del"
        shutil.move(str(child), str(dest))
        moved.append(dest.as_posix())
    return moved


# 生产代码里确实实现了、但没有 [McpTool] 特性的诊断工具 —— 供 CLI 链路自测的确定性回显。
# 必须显式登记，否则「私有实现 id 未在声明里」的守卫会拒绝生成，或把它当拼写漂移。
LOCAL_ONLY_TOOLS: tuple[dict, ...] = ({
    "raw_name": "test_tool", "id": "test_tool",
    "summary": "Deterministic echo for CLI plumbing self-test (--arg)",
    "category": "tools", "source": "cli_nodes/groups/tools/private/ToolCommands.cs",
    "parameters": [{
        "name": "arg", "type": "string", "description": "Value to echo back",
        "required": True, "default": "", "aliases": ""
    }],
},)


PROMPT_INDEX_PATH = REPO_ROOT / "resources/tools/cli_groups.yaml"

# 首轮提示词里每个分组的一句话定位。这里是**唯一**的人工维护点：
# 分组增删由 node.yaml 自动反映，但「这个 exe 是干什么的」没法从特性里猜出人话。
# 漏登记的组会让生成直接失败，而不是静默写一个空简介进提示词。
GROUP_BLURBS: dict[str, str] = {
    "agent": "plan 模式与步骤编排、子代理控制、worktree 管理",
    "code": "notebook 单元格读写/移动/执行、AI 代码工作流",
    "file": "读写删文件、行级与正则编辑、apply_patch、目录列举、发送文件给用户",
    "git": "status/add/commit/push/pull/log/diff/branch/clone/submodule",
    "github": "PR、issue、review、workflow 等 GitHub 全量操作与 REST 直调",
    "mcp": "MCP 连接与认证状态、配置读写、工具列举",
    "media": "鼠标键盘与桌面自动化、截图、图像处理",
    "search": "glob、grep、全文与符号搜索、代码库索引查询",
    "shell": "bash、powershell、C# 代码沙箱、构建队列",
    "slash": "斜杠命令（会话、代理、配置、助手模式）",
    "system": "配置读写、代理消息与生命周期、诊断、cron 与遥测",
    "tools": "技能搜索与加载、工具创建、代码简评与验证",
    "web": "网页抓取与转 Markdown、文件下载、浏览器、网页搜索",
}


def render_prompt_index(out_root: Path) -> tuple[str, list[str], list[str]]:
    """从各分组 node.yaml 生成首轮提示词用的分组索引。

    数据源只有 node.yaml：exe 名、工具数、隔离数全部现算，不在这里写第二份。
    返回 (yaml 文本, 缺简介的分组, 参与生成的分组)。
    """
    blocks: list[str] = [
        "# 由 migrate_mcp_tools_to_cli_nodes.py 从各分组 node.yaml 生成 —— 不要手改",
        "#",
        "# 消费方: ToolsSection（首轮系统提示词）用它告诉模型「有哪些独立 exe、各自干什么」，",
        "#         模型再用 `<exe> -h` 按需读说明书。工具数与隔离数现算，避免与 node.yaml 漂移。",
        "# 重新生成: python scripts/migrate_mcp_tools_to_cli_nodes.py --out modules/tools/cli_nodes/groups --index-only",
        "version: 1",
        "groups:",
    ]
    missing: list[str] = []
    seen: list[str] = []

    for child in sorted(p for p in out_root.iterdir() if p.is_dir() and (p / "node.yaml").is_file()):
        text = (child / "node.yaml").read_text(encoding="utf-8", errors="replace")

        def scalar(key: str) -> str:
            found = re.search(rf"^{key}:\s*(.+)$", text, re.M)
            return found.group(1).strip().strip('"') if found else ""

        gid = scalar("id") or child.name
        exe = scalar("exe") or f"jcc-tool-{gid}.exe"
        blurb = GROUP_BLURBS.get(gid)
        if not blurb:
            missing.append(gid)
            blurb = ""
        tool_ids = [
            m.group(1).strip().strip('"').strip("'")
            for m in re.finditer(r"^  - id:\s*(.+)$", text, re.M)
        ]
        tool_id_list = ", ".join('"' + item.replace(chr(92), chr(92) * 2).replace(chr(34), chr(92) + chr(34)) + '"'
                                for item in tool_ids)
        blocks.extend([
            f"  - id: {gid}",
            f"    exe: {exe.removesuffix('.exe')}",
            f"    tools: {scalar('tool_count') or 0}",
            f"    local: {len(re.findall(r'^    mode: local$', text, re.M))}",
            f"    delegate: {len(re.findall(r'^    mode: delegate$', text, re.M))}",
            f"    blurb: \"{blurb.replace(chr(34), chr(92) + chr(34))}\"",
            f"    tool_ids: [{tool_id_list}]",
        ])
        seen.append(gid)

    return "\n".join(blocks) + "\n", missing, seen


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--scan", default="", help="要扫描的源目录（--index-only 时可省略）")
    ap.add_argument("--out", required=True, help="分组输出根目录")
    ap.add_argument("--category", default="", help="只生成指定分组（如 git）")
    ap.add_argument("--limit", type=int, default=0, help="最多生成多少个分组（0 表示不限）")
    ap.add_argument("--emit-exe", action="store_true",
                    help="为每个分组生成通用 exe 骨架 csproj + Program.cs（已有手工实现的分组跳过）")
    ap.add_argument("--index-only", action="store_true",
                    help="不扫描源码，只从已有 node.yaml 重新生成 resources/tools/cli_groups.yaml")
    args = ap.parse_args()

    if args.index_only:
        index_text, missing, groups = render_prompt_index(Path(args.out))
        if missing:
            raise SystemExit(f"以下分组缺少首轮提示词简介，请补 GROUP_BLURBS: {missing}")
        PROMPT_INDEX_PATH.write_text(index_text, encoding="utf-8")
        print(json.dumps({"index": PROMPT_INDEX_PATH.as_posix(), "groups": groups}, ensure_ascii=False, indent=2))
        return 0

    if not args.scan:
        ap.error("--scan 是必填参数（除非用 --index-only）")

    scan_root, out_root = Path(args.scan), Path(args.out)
    if not scan_root.is_dir():
        print(f"scan dir not found: {scan_root}", file=sys.stderr)
        return 2

    candidates = sorted(p for p in scan_root.rglob("*.cs") if is_source_file(p))
    group_map, default_group = load_group_map()
    delegates = load_delegates()
    internal_tools = load_internal_tools()
    options_records = collect_options_records(scan_root)
    enums = collect_enum_values(scan_root)
    string_keys = collect_string_keys(scan_root)
    found: list[dict] = [dict(tool) for tool in LOCAL_ONLY_TOOLS]
    for path in candidates:
        found.extend(parse_file(path, group_map, default_group, options_records, enums, string_keys))

    groups: dict[str, list[dict]] = {}
    for tool in found:
        groups.setdefault(tool["category"], []).append(tool)
    if args.category:
        groups = {k: v for k, v in groups.items() if k == args.category.lower()}

    report = {"scanned_files": len(candidates), "tools_found": len(found),
              "groups_found": len(groups), "groups_written": 0,
              "internal_tools_excluded": sorted(internal_tools), "outputs": []}
    for category, tools in sorted(groups.items()):
        if args.limit and report["groups_written"] >= args.limit:
            break
        # 内部基础工具只由 jcc 内核注册（base.yaml + 首轮 tools），不作为 exe 子命令出现。
        unique = [tool for tool in dedupe_tools(tools)
                  if tool["id"].lower() not in internal_tools]
        exe = f"jcc-tool-{category}.exe"
        target_dir = out_root / category
        target_dir.mkdir(parents=True, exist_ok=True)
        help_path = target_dir / "help.txt"
        index_path = target_dir / "node.yaml"
        # G2 节点继承护栏（TASK097 §14）：node.yaml 是本脚本的生成物，全量重写；
        # 人工差异必须放同目录 node.inherit.yaml —— 本脚本绝不写它，NodeManifestLoader 会叠加。
        # 手写 inherits 到 node.yaml 必拒，否则下次生成静默丢失继承关系。
        if index_path.exists() and re.search(r"^\s*inherits\s*:", index_path.read_text(encoding="utf-8"), re.MULTILINE):
            raise SystemExit(
                f"分组 '{category}' 的 node.yaml 含手写 inherits 声明，生成器拒绝覆写"
                f"（否则继承关系会静默丢失）。\n"
                f"处理: 把继承声明迁到同目录 node.inherit.yaml —— 生成器不碰该文件，"
                f"NodeManifestLoader 会把它的差异叠加到 node.yaml 上。"
            )
        # 叠加文件校验：它描述的是同一个节点的差异，id 必须与所在组一致；
        # 否则下钻树会显示错 ID / 叠加到别的组上。生成器只校验、不改写。
        overlay_path = target_dir / "node.inherit.yaml"
        if overlay_path.exists():
            overlay_text = overlay_path.read_text(encoding="utf-8", errors="replace")
            overlay_id = re.search(r"^id:\s*(.+)$", overlay_text, re.MULTILINE)
            found_id = overlay_id.group(1).strip().strip('"') if overlay_id else ""
            if found_id != category:
                raise SystemExit(
                    f"分组 '{category}' 的 node.inherit.yaml id='{found_id}' 与组名不一致。\n"
                    f"处理: 把叠加文件的 id 改成 '{category}'（叠加描述的是同一节点的差异，不是新节点）；"
                    f"要指向别的父节点请用 inherits 字段。"
                )
        help_path.write_text(render_group_help(category, exe, unique), encoding="utf-8")
        local_ids = collect_local_ids(target_dir)
        unknown = local_ids - {t["id"] for t in unique}
        if unknown:
            raise SystemExit(
                f"分组 '{category}' 的私有实现注册了未在 [McpTool] 中声明的工具 id: {sorted(unknown)}。\n"
                f"这通常意味着 id 拼写漂移（改名后忘了改实现），必须先对齐再生成——"
                f"否则 node.yaml 的执行方式元数据会与真实实现不一致。"
            )
        index_path.write_text(render_group_index(category, exe, unique, local_ids, delegates.get(category)), encoding="utf-8")
        report["outputs"].extend([help_path.as_posix(), index_path.as_posix()])
        if args.emit_exe and is_generated_or_missing(target_dir / "Program.cs"):
            program_path = target_dir / "Program.cs"
            project_path = target_dir / f"jcc_tool_{category}.csproj"
            program_path.write_text(render_group_program(category, delegates.get(category)), encoding="utf-8")
            project_path.write_text(render_group_csproj(category), encoding="utf-8")
            report["outputs"].extend([program_path.as_posix(), project_path.as_posix()])
            report["exes_written"] = report.get("exes_written", 0) + 1
        report["groups_written"] += 1
    # 仅在全量生成时清理陈旧产物；指定 --category 的试跑不得动其他分组。
    report["pruned"] = [] if args.category else prune_stale_groups(out_root, set(groups.keys()))
    # 首轮提示词索引：全量生成时同步刷新，保证「提示词里列出的 exe」永远等于「真实存在的 exe」。
    if not args.category and not args.limit:
        index_text, missing, indexed = render_prompt_index(out_root)
        if missing:
            raise SystemExit(f"以下分组缺少首轮提示词简介，请补 GROUP_BLURBS: {missing}")
        PROMPT_INDEX_PATH.write_text(index_text, encoding="utf-8")
        report["prompt_index"] = PROMPT_INDEX_PATH.as_posix()
        report["prompt_index_groups"] = indexed
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
