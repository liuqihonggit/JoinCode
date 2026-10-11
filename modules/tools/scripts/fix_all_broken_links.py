#!/usr/bin/env python3
"""修复所有断裂的文档链接。

A类: 路径需要更新（文件存在但路径变了）→ 更新路径
B类: 文件确实不存在 → 把 Markdown 链接改为纯文本
"""

import re
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
DOCS_ROOT = REPO_ROOT / "docs"

# 构建所有实际存在的 .md 文件映射（文件名 → 完整相对路径）
def build_file_index():
    index = {}
    for f in DOCS_ROOT.rglob("*.md"):
        if f.name == 'README.md':
            continue
        rel = str(f.relative_to(REPO_ROOT)).replace('\\', '/')
        index[f.name] = rel
    return index

# A类: 旧路径 → 新路径的映射规则
def find_new_path(old_path, file_index):
    """尝试为旧路径找到新路径。返回新路径或 None。"""
    # 提取文件名
    filename = Path(old_path).name
    
    # 如果文件名在索引中，返回新路径
    if filename in file_index:
        return file_index[filename]
    
    return None

# B类: 确认不存在的文件（不在 file_index 中且不在 modules/ 中）
def is_truly_nonexistent(filename):
    """检查文件是否真的不存在于整个项目中。"""
    # 检查 docs/ 下
    for f in DOCS_ROOT.rglob(filename):
        return False
    # 检查 modules/ 下
    modules = REPO_ROOT / "modules"
    if modules.exists():
        for f in modules.rglob(filename):
            return False
    return True

def fix_file(filepath, file_index, changes_log):
    try:
        content = filepath.read_text(encoding="utf-8")
    except:
        return 0
    
    original = content
    changes = 0
    
    # 匹配 Markdown 链接: [text](docs/xxx.md) 或 [text](docs/xxx.md)
    # 也匹配裸路径: docs/xxx.md
    link_pattern = re.compile(r'\[([^\]]*)\]\((docs/[^)]+)\)')
    bare_pattern = re.compile(r'(docs/[^\s)\"\']+\.(?:md|json))')
    
    # 先处理 Markdown 链接
    def link_replacer(m):
        nonlocal changes
        text = m.group(1)
        old_path = m.group(2)
        
        new_path = find_new_path(old_path, file_index)
        if new_path and new_path != old_path:
            changes += 1
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: [{text}]({old_path}) → [{text}]({new_path})")
            return f'[{text}]({new_path})'
        
        # 检查是否真的不存在
        filename = Path(old_path).name
        if is_truly_nonexistent(filename):
            changes += 1
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: [{text}]({old_path}) → 纯文本 '{text}'")
            return text  # 改为纯文本
        
        return m.group(0)
    
    content = link_pattern.sub(link_replacer, content)
    
    # 再处理裸路径（不在 Markdown 链接中的）
    def bare_replacer(m):
        nonlocal changes
        old_path = m.group(1)
        
        new_path = find_new_path(old_path, file_index)
        if new_path and new_path != old_path:
            changes += 1
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: {old_path} → {new_path}")
            return new_path
        
        return m.group(0)
    
    content = bare_pattern.sub(bare_replacer, content)
    
    if content != original:
        filepath.write_text(content, encoding="utf-8")
    
    return changes

def main():
    file_index = build_file_index()
    print(f"文件索引: {len(file_index)} 个文件")
    
    # 扫描所有 .md 文件
    files_to_scan = [REPO_ROOT / "AGENTS.md", REPO_ROOT / "README.md"]
    for f in DOCS_ROOT.rglob("*.md"):
        files_to_scan.append(f)
    modules_dir = REPO_ROOT / "modules"
    if modules_dir.exists():
        for f in modules_dir.rglob("*.md"):
            files_to_scan.append(f)
    gh_dir = REPO_ROOT / ".github"
    if gh_dir.exists():
        for f in gh_dir.rglob("*.md"):
            files_to_scan.append(f)
    
    print(f"扫描 {len(files_to_scan)} 个文件")
    
    all_changes = []
    total = 0
    for filepath in files_to_scan:
        log = []
        c = fix_file(filepath, file_index, log)
        if c > 0:
            total += c
            all_changes.extend(log)
    
    print(f"总修复: {total} 处")
    if all_changes:
        print("变更详情:")
        for c in all_changes[:50]:
            print(c)
        if len(all_changes) > 50:
            print(f"... 还有 {len(all_changes) - 50} 条")

if __name__ == '__main__':
    main()
