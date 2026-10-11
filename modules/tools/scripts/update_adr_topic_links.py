#!/usr/bin/env python3
"""更新 ADR 按主题拆分后的链接引用。

路径变化: docs/implemented/adr/NNNN-xxx.md → docs/implemented/adr/{topic}/NNNN-xxx.md
"""

import re
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
ADR_DIR = REPO_ROOT / "docs" / "implemented" / "adr"

# 构建 ADR 编号 → 主题子目录映射
def build_topic_map():
    topic_map = {}
    for subdir in ADR_DIR.iterdir():
        if not subdir.is_dir():
            continue
        for f in subdir.glob("*.md"):
            m = re.match(r'(\d{4})', f.name)
            if m:
                topic_map[m.group(1)] = subdir.name
    return topic_map

def update_file(filepath, topic_map, changes_log):
    try:
        content = filepath.read_text(encoding="utf-8")
    except:
        return 0
    
    original = content
    changes = 0
    
    for num, topic in topic_map.items():
        # 找到该 ADR 的文件名
        for f in (ADR_DIR / topic).glob(f"{num}-*.md"):
            filename = f.name
            
            # docs/implemented/adr/NNNN-xxx.md → docs/implemented/adr/{topic}/NNNN-xxx.md
            old = f'docs/implemented/adr/{filename}'
            new = f'docs/implemented/adr/{topic}/{filename}'
            if old in content and old != new:
                count = content.count(old)
                content = content.replace(old, new)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
            
            # implemented/adr/NNNN-xxx.md → implemented/adr/{topic}/NNNN-xxx.md (相对路径)
            old2 = f'implemented/adr/{filename}'
            new2 = f'implemented/adr/{topic}/{filename}'
            if old2 in content and old2 != new2:
                # 避免双重替换（如果已有 docs/ 前缀，上面已处理）
                if f'docs/{old2}' not in content:
                    count = content.count(old2)
                    content = content.replace(old2, new2)
                    changes += count
                    changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old2}' → '{new2}' ({count}处)")
            
            # ../implemented/adr/NNNN-xxx.md → ../implemented/adr/{topic}/NNNN-xxx.md
            old3 = f'../implemented/adr/{filename}'
            new3 = f'../implemented/adr/{topic}/{filename}'
            if old3 in content and old3 != new3:
                count = content.count(old3)
                content = content.replace(old3, new3)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old3}' → '{new3}' ({count}处)")
            
            # ../../implemented/adr/NNNN-xxx.md → ../../implemented/adr/{topic}/NNNN-xxx.md
            old4 = f'../../implemented/adr/{filename}'
            new4 = f'../../implemented/adr/{topic}/{filename}'
            if old4 in content and old4 != new4:
                count = content.count(old4)
                content = content.replace(old4, new4)
                changes += count
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old4}' → '{new4}' ({count}处)")
            
            break  # 每个编号只匹配一个文件
    
    if content != original:
        filepath.write_text(content, encoding="utf-8")
    
    return changes

def main():
    topic_map = build_topic_map()
    print(f"主题映射: {len(topic_map)} 个 ADR")
    
    # 扫描所有 .md 文件
    files_to_scan = [REPO_ROOT / "AGENTS.md", REPO_ROOT / "README.md"]
    docs_root = REPO_ROOT / "docs"
    for f in docs_root.rglob("*.md"):
        files_to_scan.append(f)
    modules_dir = REPO_ROOT / "modules"
    if modules_dir.exists():
        for f in modules_dir.rglob("*.md"):
            files_to_scan.append(f)
    # .github/ 下的 .md
    gh_dir = REPO_ROOT / ".github"
    if gh_dir.exists():
        for f in gh_dir.rglob("*.md"):
            files_to_scan.append(f)
    
    print(f"扫描 {len(files_to_scan)} 个文件")
    
    all_changes = []
    total = 0
    for filepath in files_to_scan:
        log = []
        c = update_file(filepath, topic_map, log)
        if c > 0:
            total += c
            all_changes.extend(log)
    
    print(f"总替换: {total} 处")
    if all_changes:
        print("变更详情 (前20条):")
        for c in all_changes[:20]:
            print(c)
        if len(all_changes) > 20:
            print(f"... 还有 {len(all_changes) - 20} 条")

if __name__ == '__main__':
    main()
