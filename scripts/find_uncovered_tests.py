#!/usr/bin/env python3
"""找出所有测试项目(.csproj)中没有被 PR-CI 运行的项目。

CI 配置来源:
  .github/workflows/ci-unit-tests.yml   — matrix.csprojs 列表
  .github/workflows/ci-integration.yml  — dotnet test 直接引用
  .github/workflows/ci-e2e.yml          — dotnet test 直接引用

输出: 未覆盖的测试项目列表 + 建议加入定期任务
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WORKFLOWS = ROOT / ".github" / "workflows"

# ── 1. 找出所有测试 .csproj ──────────────────────────────────────────
# 测试项目命名约定: *.tests/*.csproj, *.tests/**/*.csproj, test/**/*.csproj
test_csprojs = set()
for csproj in ROOT.rglob("*.csproj"):
    rel = csproj.relative_to(ROOT).as_posix()
    # 跳过非测试项目(生产代码)
    if ".tests/" in rel or ".tests." in rel or rel.startswith("test/"):
        # 跳过 .bak
        if ".bak" in rel:
            continue
        test_csprojs.add(rel)

# ── 2. 从 CI 配置提取被引用的 .csproj ────────────────────────────────
ci_csprojs = set()

for yml in WORKFLOWS.glob("ci-*.yml"):
    text = yml.read_text(encoding="utf-8")
    # 提取所有 .csproj 路径引用
    for m in re.finditer(r'[\w/.-]+\.csproj', text):
        ci_csprojs.add(m.group())

# JoinCode.slnx 覆盖所有项目(integration.yml 用 slnx 跑全量)
# 但 slnx 包含生产+测试,我们只关心测试项目是否被显式引用
# integration.yml 用 --filter "Category=Integration" 限制,不是全量跑
# 所以 slnx 不算覆盖所有测试

# ── 3. 对比 ──────────────────────────────────────────────────────────
uncovered = sorted(test_csprojs - ci_csprojs)
covered = sorted(test_csprojs & ci_csprojs)

print(f"=== 测试项目覆盖分析 ===")
print(f"总测试项目: {len(test_csprojs)}")
print(f"CI 已覆盖:  {len(covered)}")
print(f"未覆盖:     {len(uncovered)}")
print()

if uncovered:
    print("=== 未被 PR-CI 覆盖的测试项目 ===")
    for proj in uncovered:
        print(f"  ❌ {proj}")
    print()
    print("=== 建议加入定期任务(ci-daily.yml) ===")
    print("以下项目应加入每日定期 CI 运行:")
    for proj in uncovered:
        print(f"  - {proj}")
else:
    print("✅ 所有测试项目都被 PR-CI 覆盖")

# ── 4. 检查已有的定期任务 ────────────────────────────────────────────
daily_workflows = list(WORKFLOWS.glob("ci-*daily*.yml")) + list(WORKFLOWS.glob("ci-*weekly*.yml"))
if daily_workflows:
    print()
    print("=== 已有定期任务 ===")
    for wf in daily_workflows:
        print(f"  📅 {wf.name}")
        text = wf.read_text(encoding="utf-8")
        for m in re.finditer(r'[\w/.-]+\.csproj', text):
            print(f"      {m.group()}")
