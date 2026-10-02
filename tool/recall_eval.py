#!/usr/bin/env python3
"""
题库验证召回率 — 随机抽取50个.cs文件片段，提取符号名做关键字，调jcc.exe search_semantic验证召回率。

用法:
    python tool/recall_eval.py [--count 50] [--workspace D:/project/w2]

输出:
    - 每条测试的关键字、期望文件、实际命中文件、是否命中
    - 汇总召回率（命中数/总数）
"""

import argparse
import json
import os
import random
import re
import subprocess
import sys
from pathlib import Path

JCC_EXE = "D:/project/w2/artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
EXCLUDE_DIRS = {".git", "bin", "obj", ".xxx", ".jcc", "artifacts", "node_modules", ".vs", ".idea"}
EXCLUDE_SUFFIXES = (".g.cs", ".designer.cs", ".AssemblyInfo.cs", ".GlobalUsings.g.cs")


def collect_cs_files(workspace: str) -> list[Path]:
    root = Path(workspace)
    files = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for f in filenames:
            if not f.endswith(".cs"):
                continue
            if f.endswith(EXCLUDE_SUFFIXES):
                continue
            files.append(Path(dirpath) / f)
    return files


def extract_symbol_names(filepath: Path) -> list[str]:
    try:
        text = filepath.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return []

    symbols = []
    for m in re.finditer(r"\bclass\s+(\w+)", text):
        symbols.append(m.group(1))
    for m in re.finditer(r"\b(?:void|Task|async|public|private|internal|static|sealed|override)\s+(?:async\s+)?(\w+)\s*\(", text):
        name = m.group(1)
        if name not in ("Task", "void", "Get", "Set", "Add", "Remove", "True", "False", "Null"):
            symbols.append(name)

    seen = set()
    unique = []
    for s in symbols:
        if s not in seen:
            seen.add(s)
            unique.append(s)
    return unique


def search_semantic(query: str, top_k: int = 10) -> list[dict]:
    cmd = [
        JCC_EXE, "mcp_call", "code_index_search_semantic",
        json.dumps({"query": query, "top_k": top_k, "include_source_text": False})
    ]
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=30, encoding="utf-8")
    except subprocess.TimeoutExpired:
        return []
    if result.returncode != 0:
        return []

    lines = result.stdout.strip().split("\n")
    for line in lines:
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            data = json.loads(line)
        except json.JSONDecodeError:
            continue
        if not data.get("ok"):
            continue
        text_content = data.get("data", {}).get("content", [])
        if not text_content:
            continue
        result_text = text_content[0].get("text", "")
        hits = []
        for m in re.finditer(r"D:[/\\]project[/\\][^:\s]+\.cs", result_text):
            hit_path = m.group(0).replace("\\\\", "\\").replace("\\", "/")
            hits.append(hit_path)
        return hits
    return []


def main():
    parser = argparse.ArgumentParser(description="题库验证召回率")
    parser.add_argument("--count", type=int, default=50, help="测试用例数（默认50）")
    parser.add_argument("--workspace", type=str, default="D:/project/w2", help="工作区根目录")
    parser.add_argument("--seed", type=int, default=42, help="随机种子（默认42，可复现）")
    args = parser.parse_args()

    print(f"收集 .cs 文件...")
    files = collect_cs_files(args.workspace)
    print(f"找到 {len(files)} 个 .cs 文件")

    random.seed(args.seed)
    candidates = []
    for f in files:
        symbols = extract_symbol_names(f)
        if symbols:
            candidates.append((f, symbols))

    if len(candidates) < args.count:
        print(f"警告: 只有 {len(candidates)} 个有符号的文件，少于请求的 {args.count}")
        sample = candidates
    else:
        sample = random.sample(candidates, args.count)

    print(f"随机抽取 {len(sample)} 个测试用例\n")
    print(f"{'#':>3}  {'关键字':<40}  {'期望文件':<60}  {'命中':>4}  {'召回文件'}")
    print("-" * 150)

    hits = 0
    for i, (filepath, symbols) in enumerate(sample, 1):
        keyword = symbols[0]
        rel_path = str(filepath).replace("\\", "/")
        search_hits = search_semantic(keyword, top_k=10)

        matched = any(rel_path.replace("/", "\\") in h or rel_path in h.replace("\\", "/") for h in search_hits)
        if matched:
            hits += 1

        status = "✓" if matched else "✗"
        hit_file = search_hits[0] if search_hits else "(无结果)"
        short_keyword = keyword[:38] if len(keyword) > 38 else keyword
        short_path = rel_path[-58:] if len(rel_path) > 58 else rel_path
        print(f"{i:>3}  {short_keyword:<40}  {short_path:<60}  {status:>4}  {hit_file}")

    recall_rate = hits / len(sample) * 100
    print("-" * 150)
    print(f"\n召回率: {hits}/{len(sample)} = {recall_rate:.1f}%")

    if recall_rate >= 80:
        print("评价: 优秀 ✓")
    elif recall_rate >= 60:
        print("评价: 良好")
    elif recall_rate >= 40:
        print("评价: 一般")
    else:
        print("评价: 差 — 需要改进")


if __name__ == "__main__":
    main()
