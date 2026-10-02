#!/usr/bin/env python3
"""
题库验证召回率 — 随机抽取N个.cs文件片段，提取符号名做关键字，通过jcc mcp_serve stdio管道验证召回率。

用法:
    python tool/recall_eval.py [--count 50] [--workspace D:/project/w2]

性能: 通过mcp_serve stdio管道复用已加载索引，避免每次搜索重启进程。
"""

import argparse
import json
import os
import random
import re
import subprocess
import sys
import time
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


class McpStdioClient:
    """MCP stdio 客户端 — 通过 stdin/stdout 管道与 jcc mcp_serve 通信。"""

    def __init__(self):
        self._proc = None
        self._next_id = 1

    def start(self):
        self._proc = subprocess.Popen(
            [JCC_EXE, "mcp_serve"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            encoding="utf-8",
            bufsize=1,
        )
        self._call("initialize", {
            "protocolVersion": "2025-11-25",
            "capabilities": {},
            "clientInfo": {"name": "recall_eval", "version": "1.0"},
        })
        self._notify("notifications/initialized")

    def stop(self):
        if self._proc is not None:
            self._proc.stdin.close()
            self._proc.terminate()
            try:
                self._proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._proc.kill()
            self._proc = None

    def _call(self, method: str, params: dict) -> dict:
        req_id = self._next_id
        self._next_id += 1
        req = {"jsonrpc": "2.0", "method": method, "params": params, "id": req_id}
        self._proc.stdin.write(json.dumps(req) + "\n")
        self._proc.stdin.flush()
        while True:
            resp_line = self._proc.stdout.readline()
            if not resp_line:
                raise RuntimeError("mcp_serve 进程意外退出")
            resp_line = resp_line.strip()
            if not resp_line.startswith("{"):
                continue
            data = json.loads(resp_line)
            if data.get("id") == req_id:
                return data.get("result", {})

    def _notify(self, method: str):
        req = {"jsonrpc": "2.0", "method": method}
        self._proc.stdin.write(json.dumps(req) + "\n")
        self._proc.stdin.flush()

    def search_semantic(self, query: str, top_k: int = 10, mode: str = "hybrid") -> list[str]:
        result = self._call("tools/call", {
            "name": "code_query",
            "arguments": {"query": query, "mode": mode, "top_k": top_k, "include_source_text": False},
        })
        content = result.get("content", [])
        if not content:
            return []
        text = content[0].get("text", "")
        hits = []
        for m in re.finditer(r"D:[/\\]project[/\\][^:\s]+\.cs", text):
            hit_path = m.group(0).replace("\\\\", "\\").replace("\\", "/")
            hits.append(hit_path)
        return hits


def main():
    parser = argparse.ArgumentParser(description="题库验证召回率")
    parser.add_argument("--count", type=int, default=50, help="测试用例数（默认50）")
    parser.add_argument("--workspace", type=str, default="D:/project/w2", help="工作区根目录")
    parser.add_argument("--seed", type=int, default=42, help="随机种子（默认42，可复现）")
    parser.add_argument("--mode", type=str, default="hybrid", help="检索模式（默认hybrid）")
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

    print(f"随机抽取 {len(sample)} 个测试用例")
    print(f"启动 mcp_serve 长驻进程...")
    client = McpStdioClient()
    client.start()
    print(f"mcp_serve 已就绪\n")

    print(f"{'#':>3}  {'关键字':<40}  {'期望文件':<60}  {'命中':>4}  {'召回文件'}")
    print("-" * 150)

    start_time = time.time()
    hits = 0
    for i, (filepath, symbols) in enumerate(sample, 1):
        keyword = symbols[0]
        rel_path = str(filepath).replace("\\", "/")
        search_hits = client.search_semantic(keyword, top_k=10, mode=args.mode)

        matched = any(rel_path.replace("/", "\\") in h or rel_path in h.replace("\\", "/") for h in search_hits)
        if matched:
            hits += 1

        status = "✓" if matched else "✗"
        hit_file = search_hits[0] if search_hits else "(无结果)"
        short_keyword = keyword[:38] if len(keyword) > 38 else keyword
        short_path = rel_path[-58:] if len(rel_path) > 58 else rel_path
        print(f"{i:>3}  {short_keyword:<40}  {short_path:<60}  {status:>4}  {hit_file}")

    elapsed = time.time() - start_time
    client.stop()

    recall_rate = hits / len(sample) * 100
    print("-" * 150)
    print(f"\n召回率: {hits}/{len(sample)} = {recall_rate:.1f}%  耗时: {elapsed:.2f}s ({elapsed/len(sample)*1000:.0f}ms/条)")

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
