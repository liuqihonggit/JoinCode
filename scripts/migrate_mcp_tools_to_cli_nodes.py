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

DISPATCH_RE = re.compile(r"\[McpToolDispatch\(ToolCategory\.(?P<cat>\w+)")
TOOL_RE = re.compile(
    r"\[McpTool\(\s*(?P<name>\"(?:\\.|[^\"\\])*\"|[A-Za-z0-9_.]+)\s*,\s*\"(?P<summary>(?:\\.|[^\"\\])*)\"\s*"
    r"(?:,\s*\"(?P<category>(?:\\.|[^\"\\])*)\")?[^\[\]]*\)\]",
    re.S,
)
PARAM_RE = re.compile(
    r"\[McpToolParameter\(\s*\"(?P<desc>(?:\\.|[^\"\\])*)\"\s*(?P<rest>[^]]*)\)\]\s*"
    r"(?P<type>[A-Za-z0-9_<>?\[\],. ]+?)\s+(?P<pname>[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?:=\s*(?P<default>[^,)\n]+))?",
    re.S,
)
CAMEL_RE = re.compile(r"(?<!^)(?=[A-Z])")


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


def parse_file(path: Path) -> list[dict]:
    text = path.read_text(encoding="utf-8", errors="replace")
    dispatch = DISPATCH_RE.search(text)
    dispatch_category = dispatch.group("cat").lower() if dispatch else "general"
    matches = list(TOOL_RE.finditer(text))
    tools: list[dict] = []
    for index, match in enumerate(matches):
        block_end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[match.start():block_end]
        raw_name = match.group("name")
        params = []
        for p in PARAM_RE.finditer(block):
            rest = p.group("rest") or ""
            params.append({
                "name": p.group("pname"),
                "type": normalize_type(p.group("type")),
                "description": unescape_csharp(p.group("desc").strip()),
                "required": "Required = false" not in rest and "Required=false" not in rest,
                "default": clean_default(p.group("default")),
            })
        tools.append({
            "raw_name": raw_name,
            "id": to_snake(raw_name),
            "summary": unescape_csharp(match.group("summary").strip()),
            "category": (match.group("category") or dispatch_category).lower(),
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
            lines.append(f"    --{p['name']:<26} {p['type']:<9} {flag:<4} {p['description']}{default}")
    lines.append("")
    lines.append("描述来源: 现有 [McpTool] / [McpToolParameter] 特性，由本脚本在编译期提取。")
    return "\n".join(lines) + "\n"


def render_group_index(category: str, exe: str, tools: list[dict]) -> str:
    summaries = "; ".join(yaml_escape(t["summary"]) for t in tools[:3])
    tags = ", ".join(sorted({t["id"].split("_")[0] for t in tools})[:8])
    return (
        f"# 由 migrate_mcp_tools_to_cli_nodes.py 从现有 [McpTool] 特性按分组生成\n"
        f"id: {category}\n"
        f"name: jcc-tool-{category}\n"
        f"summary: \"{summaries}\"\n"
        f"category: {category}\n"
        f"exe: {exe}\n"
        f"tags: [{tags}]\n"
        f"tool_count: {len(tools)}\n"
        f"parameters: []\n"
    )


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


def prune_stale_groups(out_root: Path, keep: set[str]) -> list[str]:
    """本次未生成的旧分组目录移到项目根 .xxx/ 归档，不删除任何文件（幂等、可追溯）。"""
    archive = REPO_ROOT / ".xxx"
    moved: list[str] = []
    if not out_root.is_dir():
        return moved
    for child in sorted(out_root.iterdir()):
        if not child.is_dir() or child.name in keep or not (child / "node.yaml").exists():
            continue
        archive.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%d%H%M%S")
        dest = archive / f"cli_nodes_group_{child.name}_{stamp}.del"
        shutil.move(str(child), str(dest))
        moved.append(dest.as_posix())
    return moved


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--scan", required=True, help="要扫描的源目录")
    ap.add_argument("--out", required=True, help="分组输出根目录")
    ap.add_argument("--category", default="", help="只生成指定分组（如 git）")
    ap.add_argument("--limit", type=int, default=0, help="最多生成多少个分组（0 表示不限）")
    args = ap.parse_args()

    scan_root, out_root = Path(args.scan), Path(args.out)
    if not scan_root.is_dir():
        print(f"scan dir not found: {scan_root}", file=sys.stderr)
        return 2

    candidates = sorted(p for p in scan_root.rglob("*.cs") if ".g.cs" not in p.name)
    found: list[dict] = []
    for path in candidates:
        found.extend(parse_file(path))

    groups: dict[str, list[dict]] = {}
    for tool in found:
        groups.setdefault(tool["category"], []).append(tool)
    if args.category:
        groups = {k: v for k, v in groups.items() if k == args.category.lower()}

    report = {"scanned_files": len(candidates), "tools_found": len(found),
              "groups_found": len(groups), "groups_written": 0, "outputs": []}
    for category, tools in sorted(groups.items()):
        if args.limit and report["groups_written"] >= args.limit:
            break
        unique = dedupe_tools(tools)
        exe = f"jcc-tool-{category}.exe"
        target_dir = out_root / category
        target_dir.mkdir(parents=True, exist_ok=True)
        help_path = target_dir / "help.txt"
        index_path = target_dir / "node.yaml"
        help_path.write_text(render_group_help(category, exe, unique), encoding="utf-8")
        index_path.write_text(render_group_index(category, exe, unique), encoding="utf-8")
        report["outputs"].extend([help_path.as_posix(), index_path.as_posix()])
        report["groups_written"] += 1
    # 仅在全量生成时清理陈旧产物；指定 --category 的试跑不得动其他分组。
    report["pruned"] = [] if args.category else prune_stale_groups(out_root, set(groups.keys()))
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
