"""执行文档迁移：git mv docs/ 下域级文档到 modules/<domain>/docs/

读取 doc_migration_plan.json，对每个域级文档执行 git mv。
保持子目录结构，docs/tasks/ 合并到 docs/task/。
"""

import json
import os
import subprocess
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
DOCS_DIR = REPO_ROOT / "docs"
MODULES_DIR = REPO_ROOT / "modules"


def get_target_path(rel_path: str, domain: str) -> Path:
    """计算目标路径

    规则:
    - docs/tasks/ → modules/<domain>/docs/task/ (合并 tasks 到 task)
    - docs/<category>/<subpath> → modules/<domain>/docs/<category>/<subpath>
    - 根级文档(condition-order-fix-tasks.md 等)不迁移(已在 global)
    """
    parts = rel_path.replace("\\", "/").split("/")

    # tasks/ → task/
    if parts[0] == "tasks":
        parts[0] = "task"

    # 构建目标路径: modules/<domain>/docs/<category>/<subpath>
    target = MODULES_DIR / domain / "docs" / Path(*parts)
    return target


def run_git_mv(src: Path, dst: Path) -> bool:
    """执行 git mv，返回是否成功"""
    src_rel = src.relative_to(REPO_ROOT)
    dst_rel = dst.relative_to(REPO_ROOT)

    # 创建目标目录
    dst.parent.mkdir(parents=True, exist_ok=True)

    # 执行 git mv
    result = subprocess.run(
        ["git", "mv", str(src_rel).replace("\\", "/"), str(dst_rel).replace("\\", "/")],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
    )

    if result.returncode != 0:
        print(f"  ❌ FAIL: {src_rel} → {dst_rel}")
        print(f"     {result.stderr.strip()}")
        return False
    else:
        print(f"  ✅ {src_rel} → {dst_rel}")
        return True


def main():
    plan_path = REPO_ROOT / "modules" / "tools" / "scripts" / "doc_migration_plan.json"
    with open(plan_path, "r", encoding="utf-8") as fp:
        plan = json.load(fp)

    domains = [
        "generators", "collections", "persistence", "async_primitives",
        "abstractions", "guard", "storage", "plugins", "mcp", "llm",
        "cognition", "perception", "services", "composition", "apps", "tools",
    ]

    total = 0
    success = 0
    failed = 0

    for domain in domains:
        files = plan.get(domain, [])
        if not files:
            continue

        print(f"\n{'='*60}")
        print(f"迁移域: {domain} ({len(files)} 个文档)")
        print(f"{'='*60}")

        for item in files:
            rel_path = item["rel_path"]
            src = DOCS_DIR / rel_path
            dst = get_target_path(rel_path, domain)

            if not src.exists():
                print(f"  ⚠️  SKIP (不存在): {rel_path}")
                continue

            total += 1
            if run_git_mv(src, dst):
                success += 1
            else:
                failed += 1

    print(f"\n{'='*60}")
    print(f"迁移完成: {success}/{total} 成功, {failed} 失败")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
