#!/usr/bin/env python3
"""修复 ADR README.md 中所有断裂的内部链接。

将 [NNNN](NNNN-xxx.md) 更新为 [NNNN](../{status}/{subdir}/NNNN-xxx.md)
"""

import re
from pathlib import Path

DOCS_ROOT = Path(r"D:\project\w1\docs")
ADR_README = DOCS_ROOT / "adr" / "README.md"

def build_adr_location_map():
    """构建 ADR 编号 → (状态, 子目录, 文件名) 映射。"""
    location_map = {}
    for status in ['implemented', 'archived', 'proposed', 'rejected']:
        status_dir = DOCS_ROOT / status
        if not status_dir.exists():
            continue
        for f in status_dir.rglob("*.md"):
            if f.name == 'README.md':
                continue
            # 提取 ADR 编号（文件名前4位数字）
            m = re.match(r'(\d{4})', f.name)
            if m:
                num = m.group(1)
                rel = f.relative_to(status_dir)
                subdir = str(rel.parent).replace('\\', '/')
                location_map[num] = (status, subdir, f.name)
    return location_map

def fix_adr_readme():
    location_map = build_adr_location_map()
    content = ADR_README.read_text(encoding="utf-8")
    
    # 替换 [NNNN](NNNN-xxx.md) → [NNNN](../{status}/{subdir}/NNNN-xxx.md)
    pattern = re.compile(r'\[(\d{4})\]\((\d{4}-[^)]+\.md)\)')
    
    def replacer(m):
        num = m.group(1)
        filename = m.group(2)
        if num in location_map:
            status, subdir, actual_filename = location_map[num]
            if subdir == '.':
                new_path = f'../{status}/{actual_filename}'
            else:
                new_path = f'../{status}/{subdir}/{actual_filename}'
            return f'[{num}]({new_path})'
        else:
            # 文件不存在，保留原链接但标注
            return m.group(0)
    
    new_content = pattern.sub(replacer, content)
    
    # 统计
    changes = content != new_content
    if changes:
        ADR_README.write_text(new_content, encoding="utf-8")
        
        # 验证修复结果
        remaining_broken = 0
        for m in pattern.finditer(new_content):
            num = m.group(1)
            if num not in location_map:
                remaining_broken += 1
        
        fixed = len(list(pattern.finditer(content))) - remaining_broken
        print(f"修复了 {fixed} 个链接")
        if remaining_broken > 0:
            print(f"仍有 {remaining_broken} 个链接指向不存在的 ADR（可能已删除或迁移到其他位置）")
            # 列出这些
            for m in pattern.finditer(new_content):
                num = m.group(1)
                if num not in location_map:
                    print(f"  [{num}]({m.group(2)})")
    else:
        print("无需修复")

def fix_other_readmes():
    """修复 docs/ 下其他 README.md 中的链接。"""
    location_map = build_adr_location_map()
    
    readmes = [
        DOCS_ROOT / "README.md",
        DOCS_ROOT / "design" / "README.md",
        DOCS_ROOT / "fix" / "README.md",
        DOCS_ROOT / "plan" / "README.md",
        DOCS_ROOT / "refactor" / "README.md",
        DOCS_ROOT / "task" / "README.md",
        DOCS_ROOT / "zh-CN" / "README.md",
    ]
    
    pattern = re.compile(r'\[(\d{4})\]\((\d{4}-[^)]+\.md)\)')
    
    for readme in readmes:
        if not readme.exists():
            continue
        content = readme.read_text(encoding="utf-8")
        
        def replacer(m):
            num = m.group(1)
            filename = m.group(2)
            if num in location_map:
                status, subdir, actual_filename = location_map[num]
                if subdir == '.':
                    new_path = f'{status}/{actual_filename}'
                else:
                    new_path = f'{status}/{subdir}/{actual_filename}'
                return f'[{num}]({new_path})'
            return m.group(0)
        
        new_content = pattern.sub(replacer, content)
        if new_content != content:
            readme.write_text(new_content, encoding="utf-8")
            print(f"修复了 {readme.relative_to(DOCS_ROOT)}")

if __name__ == '__main__':
    print("=== 修复 ADR README.md ===")
    fix_adr_readme()
    print("\n=== 修复其他 README.md ===")
    fix_other_readmes()
