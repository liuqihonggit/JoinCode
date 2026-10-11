#!/usr/bin/env python3
"""检查 modules/ 下所有 .md 文件中对 docs/ 的断裂链接。"""

import re
from pathlib import Path

repo = Path(r"D:\project\w1")
modules = repo / "modules"

# 收集所有实际存在的文件
all_files = set()
for f in repo.rglob("*.md"):
    rel = str(f.relative_to(repo)).replace("\\", "/")
    all_files.add(rel)

# 扫描 modules/ 下所有 .md 文件
broken = []
for f in modules.rglob("*.md"):
    try:
        content = f.read_text(encoding="utf-8")
    except:
        continue
    for m in re.finditer(r'docs/([^\s)"\']+\.(?:md|json))', content):
        path = m.group(1)
        full = "docs/" + path
        if full not in all_files:
            if "NNNN" in full or "]" in full or "(已删除)" in full:
                continue
            broken.append((str(f.relative_to(repo)), full))

unique_broken = sorted(set(broken))
print(f"modules/ 下断裂链接: {len(unique_broken)} 个")
for f, link in unique_broken:
    print(f"  {f}: {link}")
