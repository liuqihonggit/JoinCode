#!/usr/bin/env python3
"""复制 jcc-gh.exe 和 jcc-rg.exe 及其依赖到 jcc 输出目录
用法: python .xxx/copy_dev_artifacts.py
ADR 0070: gh/rg 是独立 exe, jcc 跨进程调用时需在同一目录找到 exe 及依赖
共享依赖只复制 jcc 没有的 dll, 避免覆盖 jcc 自己的版本
"""
import shutil, sys, os
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONFIG = "Debug"
TFM = "net10.0"
JCC_DIR = REPO / "artifacts" / "bin" / "JoinCode" / CONFIG / TFM
TOOL_DIRS = {
    "gh": REPO / "artifacts" / "bin" / "gh" / CONFIG / TFM,
    "rg": REPO / "artifacts" / "bin" / "rg" / CONFIG / TFM,
}

# jcc-gh/jcc-rg 专属文件(不覆盖 jcc 自己的)
TOOL_EXE_FILES = ["jcc-gh.exe", "jcc-gh.dll", "jcc-gh.pdb", "jcc-gh.deps.json", "jcc-gh.runtimeconfig.json",
                  "jcc-rg.exe", "jcc-rg.dll", "jcc-rg.pdb", "jcc-rg.deps.json", "jcc-rg.runtimeconfig.json"]

def main():
    if not JCC_DIR.exists():
        print(f"错误: jcc 输出目录不存在: {JCC_DIR}")
        return 1

    copied = 0
    for name, tool_dir in TOOL_DIRS.items():
        if not tool_dir.exists():
            print(f"  [跳过] {name} 输出目录不存在: {tool_dir}")
            continue
        # 复制 jcc-gh/jcc-rg 专属文件
        for fname in TOOL_EXE_FILES:
            if name not in fname:
                continue
            src = tool_dir / fname
            if src.exists():
                dst = JCC_DIR / fname
                shutil.copy2(src, dst)
                copied += 1
        # 复制 jcc 目录缺少的依赖 dll(不覆盖已有的)
        for dll in tool_dir.glob("*.dll"):
            dst = JCC_DIR / dll.name
            if not dst.exists():
                shutil.copy2(dll, dst)
                copied += 1
        for pdb in tool_dir.glob("*.pdb"):
            dst = JCC_DIR / pdb.name
            if not dst.exists():
                shutil.copy2(pdb, dst)
                copied += 1
        print(f"  [OK] {name} 产物已复制")

    print(f"总计复制 {copied} 个文件到 {JCC_DIR}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
