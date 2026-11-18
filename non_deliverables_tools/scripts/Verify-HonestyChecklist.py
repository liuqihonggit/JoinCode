#!/usr/bin/env python3
"""诚实清单一致性门禁 —— resources/honesty/checklist.yaml 与它的可视化文档必须同源。

存在的理由：这两份东西是给模型和给人看的两面，分开维护一定漂移。
清单里加了项、文档忘了打勾，用户切模型时看到「N 项全部完成」，
而文档里还留着没勾的缺口 —— 那正是「诚实模块」自己变得不诚实的方式。

检查四件事：
  ① done 项数 == 文档里 `- [x] **` 的行数（多一个少一个都失败）
  ② 每一项都有 evidence 与 next（缺失时诚实提示会打印「无」「待定」，等于没证据）
  ③ status 只允许 done / pending（其他写法会被 HonestyNotice 当成未完成，静默错位）
  ④ id 唯一（重复 id 会让「这一项做完没有」的答案取决于读取顺序）

用法: python non_deliverables_tools/scripts/Verify-HonestyChecklist.py
退出码: 0 一致 / 1 不一致
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CHECKLIST = REPO_ROOT / "resources/honesty/checklist.yaml"
DOC = REPO_ROOT / "docs/implemented/task/TASK083-诚实模块与目标驱动.md"

ITEM_RE = re.compile(r"^  - id: (?P<id>\S+)\s*$", re.M)
FIELD_RE = re.compile(r"^    (?P<key>status|evidence|next): (?P<value>.*)$", re.M)


def parse_items(text: str) -> list[dict[str, str]]:
    """按 `- id:` 切分条目；只认固定缩进（4 空格）的字段行。"""
    items: list[dict[str, str]] = []
    matches = list(ITEM_RE.finditer(text))
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[match.end():end]
        fields = {m.group("key"): m.group("value").strip().strip('"') for m in FIELD_RE.finditer(block)}
        items.append({"id": match.group("id"), **fields})
    return items


def main() -> int:
    failures: list[str] = []

    if not CHECKLIST.exists():
        print(f"FAIL 诚实清单不存在: {CHECKLIST}")
        return 1
    if not DOC.exists():
        print(f"FAIL 可视化文档不存在: {DOC}")
        return 1

    checklist_text = CHECKLIST.read_text(encoding="utf-8")
    items = parse_items(checklist_text)

    if not items:
        print("FAIL 清单里没有任何条目（解析规则或文件格式变了）")
        return 1

    ids = [item["id"] for item in items]
    duplicated = sorted({item_id for item_id in ids if ids.count(item_id) > 1})
    if duplicated:
        failures.append(f"id 重复: {', '.join(duplicated)}")

    for item in items:
        status = item.get("status", "")
        if status not in {"done", "pending"}:
            failures.append(f"{item['id']}: status='{status}' 不在 done/pending 内")
        if not item.get("evidence"):
            failures.append(f"{item['id']}: 缺 evidence（诚实提示会打印「无」= 没有证据）")
        if not item.get("next"):
            failures.append(f"{item['id']}: 缺 next（诚实提示会打印「待定」= 用户不知道该做什么）")

    done_count = sum(1 for item in items if item.get("status") == "done")
    doc_text = DOC.read_text(encoding="utf-8")
    tick_count = sum(1 for line in doc_text.splitlines() if line.startswith("- [x] **"))

    if done_count != tick_count:
        failures.append(
            f"done 项数 {done_count} != 文档打勾 {tick_count}"
            "（清单加项后要同步 docs/implemented/task/TASK083-诚实模块与目标驱动.md）"
        )

    if failures:
        for failure in failures:
            print(f"FAIL {failure}")
        return 1

    print(f"OK 诚实清单与文档一致：{len(items)} 项，{done_count} 项 done，{len(items) - done_count} 项 pending")
    return 0


if __name__ == "__main__":
    sys.exit(main())
