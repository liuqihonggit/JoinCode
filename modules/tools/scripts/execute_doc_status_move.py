#!/usr/bin/env python3
"""执行文档状态分类移动 — 把 docs/ 下文档按 DSH 四状态体系移到对应状态目录。

目录结构：
  docs/implemented/adr/     — accepted ADR
  docs/implemented/AI交互/  — git 交互 ADR
  docs/implemented/design/  — 当前有效设计文档
  docs/implemented/refactor/— 当前有效重构文档
  docs/implemented/task/    — 当前有效任务文档
  docs/archived/adr/        — superseded ADR
  docs/archived/design/     — 陈旧/已完成设计文档
  docs/archived/fix/        — 已完成修复记录
  docs/archived/plan/       — 已完成计划
  docs/archived/refactor/   — 已完成重构记录
  docs/archived/task/       — 已完成任务记录
  docs/proposed/adr/        — proposed ADR
  docs/proposed/design/     — 待决策设计文档
  docs/proposed/task/       — 待实施任务
  docs/rejected/            — (空)
"""

import json
import shutil
from pathlib import Path

DOCS_ROOT = Path(r"D:\project\w1\docs")
PLAN_FILE = DOCS_ROOT / "doc_status_classification_plan.json"

def fix_dst_path(src, dst):
    """修正目标路径：确保 ADR 文件也放到 adr/ 子目录下。"""
    src = Path(src)
    dst = Path(dst)
    # 如果源文件在 adr/ 下，但目标路径没有 adr/ 子目录，则加上
    if 'adr' in src.parts and 'adr' not in dst.parts:
        # 在状态目录后插入 adr/
        parts = list(dst.parts)
        # 找到状态目录位置（implemented/archived/proposed/rejected）
        for i, p in enumerate(parts):
            if p in ('implemented', 'archived', 'proposed', 'rejected'):
                parts.insert(i + 1, 'adr')
                break
        dst = Path(*parts)
    return dst

def main():
    plan = json.loads(PLAN_FILE.read_text(encoding="utf-8"))
    
    moved = 0
    errors = []
    
    for status in ['implemented', 'archived', 'proposed', 'rejected']:
        for item in plan[status]:
            src = Path(item['src'])
            dst = fix_dst_path(item['src'], item['dst'])
            
            if not src.exists():
                errors.append(f"源文件不存在: {src}")
                continue
            
            # 创建目标目录
            dst.parent.mkdir(parents=True, exist_ok=True)
            
            # 移动文件
            try:
                shutil.move(str(src), str(dst))
                moved += 1
            except Exception as e:
                errors.append(f"移动失败 {src} → {dst}: {e}")
    
    print(f"成功移动 {moved} 个文件")
    if errors:
        print(f"\n错误 ({len(errors)}):")
        for e in errors:
            print(f"  {e}")
    
    # 清理空的源目录
    print("\n清理空目录...")
    for subdir in ['adr', 'AI交互', 'design', 'fix', 'plan', 'refactor', 'task']:
        d = DOCS_ROOT / subdir
        if d.exists():
            # 检查是否只有 README.md 或空
            remaining = list(d.rglob("*"))
            remaining_files = [f for f in remaining if f.is_file() and f.name != 'README.md']
            if not remaining_files:
                print(f"  {subdir}/ — 仅剩 README.md 或空，保留")
            else:
                print(f"  {subdir}/ — 还有 {len(remaining_files)} 个非 README 文件未移动!")
                for f in remaining_files:
                    print(f"    {f.relative_to(DOCS_ROOT)}")

if __name__ == '__main__':
    main()
