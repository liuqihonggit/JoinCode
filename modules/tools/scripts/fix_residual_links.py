#!/usr/bin/env python3
"""修复链接更新后的残留问题：
1. 双重 implemented/ 前缀: docs/implemented/implemented/adr/ → docs/implemented/adr/
2. AI交互/ 下 ADR 的旧格式链接: docs/adr/NNNN-xxx.md → docs/implemented/AI交互/NNNN-xxx.md
3. 其他残留的 docs/adr/NNNN-xxx.md 链接（NNNN 在新目录结构中存在）
"""

import re
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
DOCS_ROOT = REPO_ROOT / "docs"

# AI交互/ 下的 ADR 编号
AI_ADR_NUMS = {
    '0003': '0003-rebase-over-merge.md',
    '0026': '0026-pr-two-stage-pipeline.md',
    '0073': '0073-gh-rest-api-direct-call.md',
    '0075': '0075-gh-cli-troubleshooting-guide.md',
    '0078': '0078-merge-e2e-synonym-rules.md',
    '0089': '0089-jcc-builtin-tools-only-no-system-gh-rg.md',
    '0090': '0090-jcc-gh-cli-subcommand.md',
    '0092': '0092-worktree-path-inconsistency-fix.md',
    '0094': '0094-github-verbose-output.md',
    '0132': '0132-jcc-build-deploy-and-gh-troubleshooting.md',
}

# task/ 下的 ADR 编号
TASK_ADR_NUMS = {
    '0009': '0009-mcp-streamable-http.md',
    '0013': '0013-hypergraph-vs-dag-separation.md',
}

# 构建所有 ADR 编号 → 新路径的映射
def build_adr_map():
    adr_map = {}
    for status in ['implemented', 'archived', 'proposed', 'rejected']:
        status_dir = DOCS_ROOT / status
        if not status_dir.exists():
            continue
        for f in status_dir.rglob("*.md"):
            if f.name == 'README.md':
                continue
            m = re.match(r'(\d{4})', f.name)
            if m:
                num = m.group(1)
                rel = str(f.relative_to(DOCS_ROOT)).replace('\\', '/')
                adr_map[num] = rel
    return adr_map

def fix_file(filepath, adr_map, changes_log):
    try:
        content = filepath.read_text(encoding="utf-8")
    except:
        return 0
    
    original = content
    changes = 0
    
    # 1. 修复双重 implemented/ 前缀
    # docs/implemented/implemented/adr/ → docs/implemented/adr/
    # implemented/implemented/adr/ → implemented/adr/
    for old, new in [
        ('docs/implemented/implemented/adr/', 'docs/implemented/adr/'),
        ('implemented/implemented/adr/', 'implemented/adr/'),
        ('docs/implemented/implemented/AI交互/', 'docs/implemented/AI交互/'),
        ('implemented/implemented/AI交互/', 'implemented/AI交互/'),
        ('docs/implemented/implemented/design/', 'docs/implemented/design/'),
        ('implemented/implemented/design/', 'implemented/design/'),
        ('docs/implemented/implemented/task/', 'docs/implemented/task/'),
        ('implemented/implemented/task/', 'implemented/task/'),
        ('docs/implemented/implemented/refactor/', 'docs/implemented/refactor/'),
        ('implemented/implemented/refactor/', 'implemented/refactor/'),
        ('docs/implemented/implemented/plan/', 'docs/implemented/plan/'),
        ('implemented/implemented/plan/', 'implemented/plan/'),
        ('docs/implemented/implemented/fix/', 'docs/implemented/fix/'),
        ('implemented/implemented/fix/', 'implemented/fix/'),
        # archived 双重
        ('docs/archived/archived/adr/', 'docs/archived/adr/'),
        ('archived/archived/adr/', 'archived/adr/'),
        # proposed 双重
        ('docs/proposed/proposed/adr/', 'docs/proposed/adr/'),
        ('proposed/proposed/adr/', 'proposed/adr/'),
        ('docs/proposed/proposed/design/', 'docs/proposed/design/'),
        ('proposed/proposed/design/', 'proposed/design/'),
        ('docs/proposed/proposed/task/', 'docs/proposed/task/'),
        ('proposed/proposed/task/', 'proposed/task/'),
    ]:
        if old in content:
            count = content.count(old)
            content = content.replace(old, new)
            changes += count
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
    
    # 2. 修复 AI交互/ 下 ADR 的旧格式链接
    for num, filename in AI_ADR_NUMS.items():
        # docs/adr/NNNN-xxx.md → docs/implemented/AI交互/NNNN-xxx.md
        old = f'docs/adr/{filename}'
        new = f'docs/implemented/AI交互/{filename}'
        if old in content:
            count = content.count(old)
            content = content.replace(old, new)
            changes += count
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
        
        # adr/NNNN-xxx.md → implemented/AI交互/NNNN-xxx.md (相对路径)
        old2 = f'adr/{filename}'
        new2 = f'implemented/AI交互/{filename}'
        if old2 in content and f'docs/{old2}' not in content:
            count = content.count(old2)
            content = content.replace(old2, new2)
            changes += count
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old2}' → '{new2}' ({count}处)")
    
    # 3. 修复 task/ 下 ADR 的旧格式链接
    for num, filename in TASK_ADR_NUMS.items():
        old = f'docs/adr/{filename}'
        new = f'docs/implemented/task/{filename}'
        if old in content:
            count = content.count(old)
            content = content.replace(old, new)
            changes += count
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' ({count}处)")
        
        old2 = f'adr/{filename}'
        new2 = f'implemented/task/{filename}'
        if old2 in content:
            count = content.count(old2)
            content = content.replace(old2, new2)
            changes += count
            changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old2}' → '{new2}' ({count}处)")
    
    # 4. 修复其他残留的 docs/adr/NNNN-xxx.md 链接
    # 扫描所有 docs/adr/NNNN-xxx.md 模式
    pattern = re.compile(r'docs/adr/(\d{4}-[^\s)\"\']+\.md)')
    for m in pattern.finditer(content):
        filename = m.group(1)
        num = filename[:4]
        if num in adr_map:
            old = f'docs/adr/{filename}'
            new = f'docs/{adr_map[num]}'
            if old in content:
                content = content.replace(old, new)
                changes += 1
                changes_log.append(f"  {filepath.relative_to(REPO_ROOT)}: '{old}' → '{new}' (1处)")
    
    if content != original:
        filepath.write_text(content, encoding="utf-8")
    
    return changes

def main():
    adr_map = build_adr_map()
    
    files_to_scan = [REPO_ROOT / "AGENTS.md"]
    for f in DOCS_ROOT.rglob("*.md"):
        files_to_scan.append(f)
    modules_dir = REPO_ROOT / "modules"
    if modules_dir.exists():
        for f in modules_dir.rglob("*.md"):
            files_to_scan.append(f)
    
    print(f"扫描 {len(files_to_scan)} 个文件")
    
    all_changes = []
    total = 0
    for filepath in files_to_scan:
        log = []
        c = fix_file(filepath, adr_map, log)
        if c > 0:
            total += c
            all_changes.extend(log)
    
    print(f"总修复: {total} 处")
    if all_changes:
        print("变更详情 (前30条):")
        for c in all_changes[:30]:
            print(c)
        if len(all_changes) > 30:
            print(f"... 还有 {len(all_changes) - 30} 条")

if __name__ == '__main__':
    main()
