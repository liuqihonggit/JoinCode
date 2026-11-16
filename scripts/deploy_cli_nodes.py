#!/usr/bin/env python3
"""把各分组 exe 的构建产物部署到 jcc 的工具目录（artifacts/pack/tool-nodes/tools/<组>/）。

存在理由：`jcc tool call <组> <工具>` 判断走 exe 还是宿主，读的是**部署目录里的 node.yaml**，
不是源码目录里的。只编不部署，就会出现"源码里明明 local、jcc 却按旧的 host 转发"的假故障
（实测踩过：media 组 node.yaml 停留在 quadtree_* 还是 host 的旧版本）。

用法：
  python scripts/deploy_cli_nodes.py                # 部署全部已构建的分组
  python scripts/deploy_cli_nodes.py --group code   # 只部署某组
  python scripts/deploy_cli_nodes.py --target DIR   # 指定部署根（默认 artifacts/pack/tool-nodes/tools）
"""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GROUPS = REPO / "modules" / "tools" / "cli_nodes" / "groups"
BIN = REPO / "artifacts" / "bin"
DEFAULT_TARGET = REPO / "artifacts" / "pack" / "tool-nodes" / "tools"

# 分组 exe 运行所需的最小文件集（少了 deps.json / runtimeconfig.json 起不来；node.yaml 决定路由）
RUNTIME_SUFFIXES = (".dll", ".exe", ".deps.json", ".runtimeconfig.json", ".pdb")
EXTRA_FILES = ("node.yaml", "help.txt")


def deploy_group(group_dir: Path, target_root: Path) -> tuple[bool, str]:
    group = group_dir.name
    build_dirs = sorted(BIN.glob(f"jcc_tool_{group}/Debug/*"), key=lambda p: p.stat().st_mtime, reverse=True)
    build_dirs = [p for p in build_dirs if p.is_dir()]
    if not build_dirs:
        return False, "未构建（先 dotnet build 该组）"
    build = build_dirs[0]

    target = target_root / group
    target.mkdir(parents=True, exist_ok=True)

    copied = 0
    for source in build.iterdir():
        if not source.is_file():
            continue
        if source.name.startswith("jcc-tool-") and source.name.endswith(RUNTIME_SUFFIXES) or source.name in {
            "Abstractions.Primitives.Tools.dll", "YamlDotNet.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll", "Microsoft.Extensions.Logging.Abstractions.dll",
        }:
            shutil.copy2(source, target / source.name)
            copied += 1

    for name in EXTRA_FILES:
        source = group_dir / name
        if source.exists():
            shutil.copy2(source, target / name)
            copied += 1
        elif name == "node.yaml":
            return False, "缺少 node.yaml（先重跑生成器）"

    return True, f"{copied} 个文件 ← {build.name}"


def main() -> int:
    parser = argparse.ArgumentParser(description="部署分组 exe 到 jcc 工具目录")
    parser.add_argument("--group", action="append", help="只部署指定分组，可重复")
    parser.add_argument("--target", default=str(DEFAULT_TARGET), help="部署根目录")
    args = parser.parse_args()

    group_dirs = sorted(p for p in GROUPS.iterdir() if p.is_dir() and (p / "node.yaml").exists())
    if args.group:
        wanted = set(args.group)
        group_dirs = [p for p in group_dirs if p.name in wanted]

    target_root = Path(args.target)
    target_root.mkdir(parents=True, exist_ok=True)

    failed = False
    for group_dir in group_dirs:
        ok, detail = deploy_group(group_dir, target_root)
        if not ok:
            failed = True
        print(f"{group_dir.name:<8} {'OK  ' if ok else 'SKIP'} {detail}")

    print()
    print(f"部署根: {target_root}")
    print("完成" if not failed else "有分组未部署（见 SKIP 行）")
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
