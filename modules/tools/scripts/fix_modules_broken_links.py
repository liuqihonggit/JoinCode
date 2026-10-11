#!/usr/bin/env python3
"""修复 modules/ 下的断裂链接。"""

import re
from pathlib import Path

repo = Path(r"D:\project\w1")
modules = repo / "modules"

# 需要处理的断裂路径 → 替换为
replacements = {
    "docs/adr/01xx-desktop-scene-orchestration.md": "docs/implemented/adr/data_core_selection/0105-desktop-scene-orchestration.md",
    "docs/adr/00XX-path-existence-precheck.md": "docs/implemented/adr/script_path_validation/0062-path-existence-precheck-and-garbled-detection.md",
    "docs/plan/TUI架构与消息管道重构设计.md": "(已删除)",
    "docs/refactor/asynclock-file-io-actor-migration.md": "(已删除)",
    "docs/plan/状态机重构计划.md": "(已删除)",
}

# 自引用路径（文件在 modules/ 下，引用 docs/ 旧路径）
self_refs = [
    "docs/task/gui/设计文档.md",
    "docs/fix/脚本超时与路径校验修复.md",
    "docs/task/TASK008-GitHubMCP工具复刻任务清单.md",
]

total = 0
for f in modules.rglob("*.md"):
    try:
        content = f.read_text(encoding="utf-8")
    except:
        continue
    original = content
    
    for old, new in replacements.items():
        if old in content:
            content = content.replace(old, new)
    
    for old in self_refs:
        if old in content:
            content = content.replace(old, "(已删除)")
    
    if content != original:
        f.write_text(content, encoding="utf-8")
        total += 1
        print(f"  {f.relative_to(repo)}: 已修复")

print(f"\n修复了 {total} 个文件")
