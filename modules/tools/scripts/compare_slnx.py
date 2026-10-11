#!/usr/bin/env python3
"""对比旧七层 slnx 和新 module.slnx 中的项目，找出遗漏。"""

import re
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
OLD_SLN_DIR = REPO_ROOT / "build" / "sln"
MODULES_DIR = REPO_ROOT / "modules"

def extract_projects(slnx_path):
    """从 slnx 文件中提取项目路径列表。"""
    try:
        tree = ET.parse(slnx_path)
        root = tree.getroot()
        projects = []
        for proj in root.iter("Project"):
            path = proj.get("Path", "")
            if path:
                projects.append(path)
        return projects
    except Exception as e:
        print(f"  解析失败 {slnx_path}: {e}")
        return []

def main():
    # 1. 提取所有旧 slnx 中的项目
    old_slns = {}
    for f in sorted(OLD_SLN_DIR.glob("*.slnx")):
        projects = extract_projects(f)
        old_slns[f.name] = projects
        print(f"旧 {f.name}: {len(projects)} 个项目")

    # 2. 提取所有 module.slnx 中的项目
    module_projects = set()
    for f in sorted(MODULES_DIR.glob("*/module.slnx")):
        projects = extract_projects(f)
        domain = f.parent.name
        print(f"新 {domain}/module.slnx: {len(projects)} 个项目")
        for p in projects:
            module_projects.add(p)

    # 3. 提取 JoinCode.slnx 中的项目（主解决方案）
    joincode_projects = set(old_slns.get("JoinCode.slnx", []))

    # 4. 找出只在旧 slnx 中而不在任何 module.slnx 中的项目
    print("\n=== 对比结果 ===")
    all_old_projects = set()
    for name, projects in old_slns.items():
        if name == "JoinCode.slnx":
            continue
        for p in projects:
            all_old_projects.add(p)

    only_in_old = all_old_projects - module_projects
    only_in_modules = module_projects - all_old_projects - joincode_projects

    print(f"\n旧七层 slnx 总项目数: {len(all_old_projects)}")
    print(f"新 module.slnx 总项目数: {len(module_projects)}")
    print(f"JoinCode.slnx 项目数: {len(joincode_projects)}")

    print(f"\n只在旧 slnx 中（不在任何 module.slnx）: {len(only_in_old)} 个")
    for p in sorted(only_in_old):
        print(f"  {p}")

    print(f"\n只在 module.slnx 中（不在旧七层 slnx）: {len(only_in_modules)} 个")
    for p in sorted(only_in_modules)[:20]:
        print(f"  {p}")
    if len(only_in_modules) > 20:
        print(f"  ... 还有 {len(only_in_modules) - 20} 个")

    # 5. 检查 JoinCode.slnx 是否包含所有 module.slnx 项目
    not_in_joincode = module_projects - joincode_projects
    print(f"\n在 module.slnx 但不在 JoinCode.slnx: {len(not_in_joincode)} 个")
    for p in sorted(not_in_joincode):
        print(f"  {p}")

if __name__ == "__main__":
    main()
