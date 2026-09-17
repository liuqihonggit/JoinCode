#!/usr/bin/env python3
"""搜索全仓库 DisposeAsync 中 await 后台任务但未吞 OperationCanceledException 的隐患。"""
import re
from pathlib import Path

REPO_ROOT = Path(r"D:/project/w1")

# 搜索所有生产代码（排除 tests）
SEARCH_DIRS = [
    REPO_ROOT / "lib",
    REPO_ROOT / "kit",
    REPO_ROOT / "llm",
    REPO_ROOT / "server",
    REPO_ROOT / "app",
]

# 模式：await _xxxTask 或 await xxxTask（后台任务等待）
await_task = re.compile(r'\bawait\s+(\w*Task)\b')
cancel_call = re.compile(r'\b_cts\.Cancel\(\)|\bcts\.Cancel\(\)|\.Cancel\(\)')
disposeasync_decl = re.compile(r'\b(?:public\s+)?(?:async\s+)?(?:ValueTask|Task)\s+Dispose(?:Async)?\s*\(')

results = []
for search_dir in SEARCH_DIRS:
    if not search_dir.exists():
        continue
    for cs_file in sorted(search_dir.rglob("*.cs")):
        if ".tests" in str(cs_file) or "GlobalUsings" in cs_file.name:
            continue
        try:
            text = cs_file.read_text(encoding="utf-8-sig")
        except Exception:
            continue
        lines = text.splitlines()
        # 找到所有 DisposeAsync 方法
        in_dispose = False
        brace_depth = 0
        dispose_start = -1
        has_cancel = False
        has_await_task = False
        await_task_lines = []
        has_try_catch_cancel = False
        
        for i, line in enumerate(lines, 1):
            stripped = line.strip()
            
            # 检测 DisposeAsync 方法开始
            if not in_dispose and disposeasync_decl.search(line):
                in_dispose = True
                dispose_start = i
                brace_depth = 0
                has_cancel = False
                has_await_task = False
                await_task_lines = []
                has_try_catch_cancel = False
            
            if in_dispose:
                brace_depth += line.count('{') - line.count('}')
                
                if cancel_call.search(line):
                    has_cancel = True
                
                if await_task.search(line) and 'ConfigureAwait' not in line:
                    pass  # 有 ConfigureAwait 的可能是正常的
                
                m = await_task.search(line)
                if m:
                    has_await_task = True
                    await_task_lines.append((i, line.rstrip()))
                
                # 检查 try-catch OperationCanceledException
                if 'catch' in line and 'OperationCanceledException' in line:
                    has_try_catch_cancel = True
                
                if brace_depth <= 0 and '{' in line:
                    # 方法结束
                    if has_await_task and has_cancel and not has_try_catch_cancel:
                        rel = str(cs_file.relative_to(REPO_ROOT))
                        results.append((rel, dispose_start, await_task_lines))
                    in_dispose = False

# 也搜索 AwaitBackgroundTaskSafe 已经修复的
print(f"发现 {len(results)} 处 DisposeAsync 中 await 后台任务+有Cancel+无try-catch OperationCanceledException:\n")
for fpath, start, task_lines in sorted(results):
    print(f"  {fpath}:{start}")
    for lineno, line in task_lines:
        print(f"    L{lineno}: {line.strip()}")
    print()

print(f"汇总: {len(results)} 处潜在隐患")
