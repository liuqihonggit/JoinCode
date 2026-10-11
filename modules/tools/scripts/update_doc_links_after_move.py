#!/usr/bin/env python3
"""批量更新文档移动后的断裂链接引用。

构建 ADR 编号 → 新路径映射，扫描所有 .md 文件中的引用并替换。

引用模式：
1. docs/adr/NNNN-xxx.md → docs/{status}/adr/NNNN-xxx.md
2. adr/NNNN-xxx.md → {status}/adr/NNNN-xxx.md (相对路径)
3. ../adr/NNNN-xxx.md → ../{status}/adr/NNNN-xxx.md (从子目录)
4. [NNNN](NNNN-xxx.md) → [NNNN](../{status}/adr/NNNN-xxx.md) (ADR README 内部)
5. AI交互/NNNN-xxx.md → implemented/AI交互/NNNN-xxx.md
6. design/DSGxxx.md → {status}/design/DSGxxx.md
7. fix/FIXxxx.md → {status}/fix/FIXxxx.md
8. plan/PLAxxx.md → {status}/plan/PLAxxx.md
9. refactor/REFxxx.md → {status}/refactor/REFxxx.md
10. task/TASKxxx.md → {status}/task/TASKxxx.md
"""

import re
import os
from pathlib import Path
from collections import defaultdict

REPO_ROOT = Path(r"D:\project\w1")
DOCS_ROOT = REPO_ROOT / "docs"

# --- 1. 构建文件名 → 新路径映射 ---

def build_file_map():
    """构建 文件名 → (状态, 相对新路径) 映射。"""
    file_map = {}  # filename → (status, relative_path_from_docs)
    
    for status in ['implemented', 'archived', 'proposed', 'rejected']:
        status_dir = DOCS_ROOT / status
        if not status_dir.exists():
            continue
        for f in status_dir.rglob("*.md"):
            if f.name == 'README.md':
                continue
            rel = f.relative_to(DOCS_ROOT)
            file_map[f.name] = (status, str(rel).replace('\\', '/'))
    
    return file_map

# --- 2. 扫描并更新所有 .md 文件 ---

def update_file(filepath, file_map, changes_log):
    """更新单个文件中的引用。"""
    try:
        content = filepath.read_text(encoding="utf-8")
    except Exception:
        return 0
    
    original = content
    changes = 0
    
    # 模式1: docs/adr/NNNN-xxx.md → docs/{status}/adr/NNNN-xxx.md
    # 模式2: adr/NNNN-xxx.md → {status}/adr/NNNN-xxx.md
    # 模式3: ../adr/NNNN-xxx.md → ../{status}/adr/NNNN-xxx.md
    # 通用：找到所有指向 docs/ 下文件的路径引用
    
    for filename, (status, new_rel) in file_map.items():
        # 跳过 AI交互/ 下的文件（用不同模式处理）
        if 'AI交互' in new_rel:
            continue
        
        # 各种可能的引用模式
        patterns = [
            # docs/adr/filename → docs/{status}/adr/filename
            (f'docs/adr/{filename}', f'docs/{new_rel}'),
            # adr/filename → {status}/adr/filename (相对路径，不含 docs/ 前缀)
            # 但要避免匹配 docs/adr/ 的情况（已上面处理）
        ]
        
        # 从 new_rel 提取子目录和文件名
        # new_rel 格式: implemented/adr/0002-xxx.md
        parts = new_rel.split('/')
        if len(parts) >= 2:
            subdir = parts[1]  # adr, design, fix, etc.
            status_part = parts[0]  # implemented, archived, proposed
            
            # adr/filename → {status}/adr/filename
            old_rel = f'{subdir}/{filename}'
            new_path = f'{status_part}/{subdir}/{filename}'
            if old_rel != new_path:
                patterns.append((old_rel, new_path))
            
            # ../adr/filename → ../{status}/adr/filename
            old_rel2 = f'../{subdir}/{filename}'
            new_path2 = f'../{status_part}/{subdir}/{filename}'
            if old_rel2 != new_path2:
                patterns.append((old_rel2, new_path2))
            
            # ../../adr/filename → ../../{status}/adr/filename
            old_rel3 = f'../../{subdir}/{filename}'
            new_path3 = f'../../{status_part}/{subdir}/{filename}'
            if old_rel3 != new_path3:
                patterns.append((old_rel3, new_path3))
        
        for old, new in patterns:
            if old in content and old != new:
                count = content.count(old)
                content = content.replace(old, new)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
    
    # 处理 AI交互/ 下的文件
    ai_dir = DOCS_ROOT / "implemented" / "AI交互"
    if ai_dir.exists():
        for f in ai_dir.glob("*.md"):
            if f.name == 'README.md':
                continue
            # AI交互/filename → implemented/AI交互/filename
            old = f'AI交互/{f.name}'
            new = f'implemented/AI交互/{f.name}'
            if old in content and old != new:
                count = content.count(old)
                content = content.replace(old, new)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
            
            # docs/AI交互/filename → docs/implemented/AI交互/filename
            old2 = f'docs/AI交互/{f.name}'
            new2 = f'docs/implemented/AI交互/{f.name}'
            if old2 in content and old2 != new2:
                count = content.count(old2)
                content = content.replace(old2, new2)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old2}' → '{new2}' ({count}处)")
    
    if content != original:
        filepath.write_text(content, encoding="utf-8")
    
    return changes

def main():
    file_map = build_file_map()
    print(f"文件映射: {len(file_map)} 个文件")
    
    # 扫描所有需要更新的 .md 文件
    # 1. AGENTS.md
    # 2. docs/ 下所有 .md 文件（包括新目录结构中的）
    # 3. modules/ 下所有 .md 文件（可能有引用）
    
    files_to_scan = []
    
    # AGENTS.md
    agents_md = REPO_ROOT / "AGENTS.md"
    if agents_md.exists():
        files_to_scan.append(agents_md)
    
    # docs/ 下所有 .md 文件
    for f in DOCS_ROOT.rglob("*.md"):
        files_to_scan.append(f)
    
    # modules/ 下所有 .md 文件（限制深度避免太多）
    modules_dir = REPO_ROOT / "modules"
    if modules_dir.exists():
        for f in modules_dir.rglob("*.md"):
            files_to_scan.append(f)
    
    print(f"扫描 {len(files_to_scan)} 个文件")
    
    all_changes = []
    total_changes = 0
    
    for filepath in files_to_scan:
        changes_log = []
        changes = update_file(filepath, file_map, changes_log)
        if changes > 0:
            total_changes += changes
            all_changes.extend(changes_log)
    
    print(f"\n总替换: {total_changes} 处")
    print(f"涉及文件: {len(set(c.strip().split(':')[0] for c in all_changes))} 个")
    
    # 输出前 50 条变更
    if all_changes:
        print("\n变更详情 (前50条):")
        for c in all_changes[:50]:
            print(c)
        if len(all_changes) > 50:
            print(f"\n... 还有 {len(all_changes) - 50} 条变更")

if __name__ == '__main__':
    main()
