#!/usr/bin/env python3
"""扫描 docs/ 下所有文档，按 DSH 四状态体系分类并生成移动计划。

状态映射规则：
- ADR: 从 README.md 索引表解析状态
  - accepted → implemented
  - superseded → archived
  - proposed → proposed
- 非ADR文档: 从头部 10 行解析状态关键词
  - "已完成/全部完成/completed/完成时间" → archived
  - "待决策/未实现/待实施/proposed/设计草案" → proposed
  - "已陈旧/superseded/内容已过时" → archived
  - 无明确标注 → implemented (默认当前有效)
- README.md 索引文件: 保留原位不移动
"""

import re
import os
import json
from pathlib import Path

DOCS_ROOT = Path(r"D:\project\w1\docs")
ADR_DIR = DOCS_ROOT / "adr"
ADR_README = ADR_DIR / "README.md"

# --- 1. 解析 ADR README 索引获取每个 ADR 的状态 ---

def parse_adr_statuses():
    """从 ADR README.md 索引表解析每个 ADR 的状态。"""
    content = ADR_README.read_text(encoding="utf-8")
    statuses = {}
    # 匹配索引表行: | [NNNN](NNNN-xxx.md) | 标题 | 状态 | 日期 |
    pattern = re.compile(r'\|\s*\[(\d{4})\]\((\d{4}-[^)]+\.md)\)\s*\|[^|]+\|\s*([^|]+)\s*\|')
    for m in pattern.finditer(content):
        num = m.group(1)
        filename = m.group(2)
        status_raw = m.group(3).strip()
        # 只记录第一次匹配（完整索引表在前）
        if num not in statuses:
            statuses[num] = (filename, status_raw)
    return statuses

def map_adr_status(status_raw):
    """将 ADR 原始状态映射到 DSH 四状态。"""
    s = status_raw.lower()
    if 'superseded' in s or 'deprecated' in s:
        return 'archived'
    if 'proposed' in s:
        return 'proposed'
    if 'rejected' in s:
        return 'rejected'
    if 'accepted' in s:
        return 'implemented'
    # 默认
    return 'implemented'

# --- 2. 解析非 ADR 文档状态 ---

def parse_non_adr_status(filepath):
    """从文档头部 15 行解析状态关键词。"""
    try:
        with open(filepath, encoding="utf-8") as f:
            head = "".join(f.readline() for _ in range(15))
    except Exception:
        return 'implemented'
    
    lower = head.lower()
    
    # proposed 关键词
    proposed_keywords = [
        '待决策', '未实现', '待实施', '设计草案', '待讨论',
        '状态: proposed', 'status: proposed', '待用户确认',
    ]
    for kw in proposed_keywords:
        if kw in lower:
            return 'proposed'
    
    # archived 关键词
    archived_keywords = [
        '已陈旧', 'superseded', '内容已过时', '内容已陈旧',
        '全部完成', '完成时间:', '状态: completed', 'status: completed',
        '状态: superseded', '归档记录', '死代码归档',
    ]
    for kw in archived_keywords:
        if kw in lower:
            return 'archived'
    
    # 默认 implemented
    return 'implemented'

# --- 3. 生成完整归类计划 ---

def generate_plan():
    adr_statuses = parse_adr_statuses()
    
    plan = {
        'implemented': [],
        'archived': [],
        'proposed': [],
        'rejected': [],
        'skip': [],  # README.md 等索引文件，保留原位
    }
    
    # 3a. ADR 文件
    for num, (filename, status_raw) in sorted(adr_statuses.items()):
        filepath = ADR_DIR / filename
        if not filepath.exists():
            plan['skip'].append({
                'src': str(filepath),
                'reason': f'ADR {num} 文件不存在（可能已迁移到其他目录）'
            })
            continue
        dsh_status = map_adr_status(status_raw)
        plan[dsh_status].append({
            'src': str(filepath),
            'dst': str(DOCS_ROOT / dsh_status / filename),
            'adr_num': num,
            'original_status': status_raw,
        })
    
    # 3b. AI交互/ 目录下的 ADR（全部 accepted → implemented）
    ai_dir = DOCS_ROOT / "AI交互"
    if ai_dir.exists():
        for f in sorted(ai_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({
                    'src': str(f),
                    'reason': 'README.md 索引文件保留原位'
                })
                continue
            plan['implemented'].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / 'implemented' / 'AI交互' / f.name),
                'adr_num': f.stem[:4],
                'original_status': 'accepted (from AI交互/)',
            })
    
    # 3c. design/ 目录
    design_dir = DOCS_ROOT / "design"
    if design_dir.exists():
        for f in sorted(design_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            dsh_status = parse_non_adr_status(f)
            plan[dsh_status].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / dsh_status / 'design' / f.name),
                'original_status': f'non-ADR (auto-detected: {dsh_status})',
            })
    
    # 3d. fix/ 目录
    fix_dir = DOCS_ROOT / "fix"
    if fix_dir.exists():
        for f in sorted(fix_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            # fix 文档默认 archived（已完成的修复记录）
            dsh_status = parse_non_adr_status(f)
            if dsh_status == 'implemented':
                dsh_status = 'archived'  # fix 文档默认归档
            plan[dsh_status].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / dsh_status / 'fix' / f.name),
                'original_status': f'non-ADR (auto-detected: {dsh_status})',
            })
    
    # 3e. plan/ 目录
    plan_dir = DOCS_ROOT / "plan"
    if plan_dir.exists():
        for f in sorted(plan_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            dsh_status = parse_non_adr_status(f)
            plan[dsh_status].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / dsh_status / 'plan' / f.name),
                'original_status': f'non-ADR (auto-detected: {dsh_status})',
            })
    
    # 3f. refactor/ 目录
    refactor_dir = DOCS_ROOT / "refactor"
    if refactor_dir.exists():
        for f in sorted(refactor_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            dsh_status = parse_non_adr_status(f)
            plan[dsh_status].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / dsh_status / 'refactor' / f.name),
                'original_status': f'non-ADR (auto-detected: {dsh_status})',
            })
    
    # 3g. task/ 目录
    task_dir = DOCS_ROOT / "task"
    if task_dir.exists():
        for f in sorted(task_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            dsh_status = parse_non_adr_status(f)
            plan[dsh_status].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / dsh_status / 'task' / f.name),
                'original_status': f'non-ADR (auto-detected: {dsh_status})',
            })
        # task/single_source_audit/ 子目录
        audit_dir = task_dir / "single_source_audit"
        if audit_dir.exists():
            for f in sorted(audit_dir.glob("*.md")):
                dsh_status = parse_non_adr_status(f)
                plan[dsh_status].append({
                    'src': str(f),
                    'dst': str(DOCS_ROOT / dsh_status / 'task' / 'single_source_audit' / f.name),
                    'original_status': f'non-ADR (auto-detected: {dsh_status})',
                })
    
    # 3h. zh-CN/ 目录
    zh_dir = DOCS_ROOT / "zh-CN"
    if zh_dir.exists():
        for f in sorted(zh_dir.glob("*.md")):
            if f.name == 'README.md':
                plan['skip'].append({'src': str(f), 'reason': 'README.md 索引文件保留原位'})
                continue
            plan['implemented'].append({
                'src': str(f),
                'dst': str(DOCS_ROOT / 'implemented' / 'zh-CN' / f.name),
                'original_status': 'non-ADR (default: implemented)',
            })
    
    return plan

# --- 4. 输出计划 ---

if __name__ == '__main__':
    plan = generate_plan()
    
    print("=" * 60)
    print("DSH 四状态分类计划")
    print("=" * 60)
    
    for status in ['implemented', 'archived', 'proposed', 'rejected']:
        items = plan[status]
        print(f"\n## {status}/ ({len(items)} 个文件)")
        for item in items:
            src = Path(item['src']).relative_to(DOCS_ROOT)
            dst = Path(item['dst']).relative_to(DOCS_ROOT)
            print(f"  {src} → {dst}")
    
    print(f"\n## skip/ ({len(plan['skip'])} 个文件)")
    for item in plan['skip']:
        src = Path(item['src']).relative_to(DOCS_ROOT)
        print(f"  {src} — {item['reason']}")
    
    # 保存 JSON
    output = DOCS_ROOT / "doc_status_classification_plan.json"
    with open(output, 'w', encoding='utf-8') as f:
        json.dump(plan, f, ensure_ascii=False, indent=2)
    print(f"\n计划已保存到: {output}")
    
    # 统计
    total = sum(len(plan[s]) for s in ['implemented', 'archived', 'proposed', 'rejected'])
    print(f"\n总计: {total} 个文件待移动, {len(plan['skip'])} 个文件保留原位")
