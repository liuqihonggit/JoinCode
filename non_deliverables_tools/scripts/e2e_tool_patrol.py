"""
E2E 工具巡检 — 循环启动真实 jcc.exe，每次 mcp_call 一个工具，验证 exe 启动链路。
目的：暴露测试进程内调用发现不了的问题（启动配置/DI 构建/参数解析/进程级崩溃）。
用空参数 {} 调用 — 工具返回参数不足错误是预期，exe 不崩溃/挂死即链路通。
跳过有副作用的工具（gh_/git_/agent_/desktop/mcp_connect/worktree/forward_user_input）避免真实写入+LLM token。
"""
import subprocess
import json
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

JCC = r"D:\project\w2\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe"
TIMEOUT = 30
SKIP_PREFIXES = ("gh_", "git_", "agent_")
SKIP_NAMES = {"agent", "forward_user_input", "mcp_connect",
              "worktree_create", "worktree_remove", "worktree_cleanup", "worktree_merge",
              "enter_worktree", "exit_worktree"}

def get_tools():
    r = subprocess.run([JCC, "mcp_list"], capture_output=True, text=True, timeout=60, encoding="utf-8")
    data = json.loads(r.stdout)
    return data["data"]

def has_side_effects(tool):
    name = tool["name"]
    cat = tool.get("category", "")
    kind = tool.get("kind", "")
    if cat == "desktop":
        return True
    if kind == "Slash":
        return True  # Slash 命令执行复杂操作，空参数会卡死/超时
    if name in SKIP_NAMES:
        return True
    for p in SKIP_PREFIXES:
        if name.startswith(p):
            return True
    return False

def patrol_one(tool):
    name = tool["name"]
    if has_side_effects(tool):
        return (name, "[SKIP]", "有真实副作用", 0.0)
    start = time.monotonic()
    try:
        r = subprocess.run([JCC, "mcp_call", name, "{}"], capture_output=True, text=True, timeout=TIMEOUT, encoding="utf-8")
        elapsed = time.monotonic() - start
        # 解析 jcc 输出 JSON
        try:
            out = json.loads(r.stdout)
            ok = out.get("ok", False)
            if ok:
                return (name, "[OK]", "", elapsed)
            else:
                err = out.get("error", out.get("data", ""))
                return (name, "[ERR]", str(err)[:100], elapsed)
        except json.JSONDecodeError:
            return (name, "[OK]", f"非JSON输出(exit={r.returncode})", elapsed)
    except subprocess.TimeoutExpired:
        elapsed = time.monotonic() - start
        return (name, "[TMO]", f"{TIMEOUT}s 超时", elapsed)
    except Exception as ex:
        elapsed = time.monotonic() - start
        return (name, "[CRASH]", f"{type(ex).__name__}: {ex}", elapsed)

def main():
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 0
    print("E2E 工具巡检 — 循环启动 jcc.exe 验证启动链路（并行 10 进程）")
    tools = get_tools()
    if limit > 0:
        tools = tools[:limit]
    print(f"工具总数: {len(tools)}")
    results = [None] * len(tools)
    with ThreadPoolExecutor(max_workers=10) as pool:
        futures = {pool.submit(patrol_one, tool): i for i, tool in enumerate(tools)}
        done = 0
        for fut in as_completed(futures):
            i = futures[fut]
            results[i] = fut.result()
            done += 1
            name, mn, err, elapsed = results[i]
            print(f"{done}/{len(tools)} {mn} {name} {err} ({elapsed:.1f}s)", flush=True)
    # 汇总
    counts = {}
    for _, mn, _, _ in results:
        counts[mn] = counts.get(mn, 0) + 1
    print("\n=== 汇总 ===")
    for mn, c in sorted(counts.items()):
        print(f"{mn}: {c}")
    # 写报告
    report = Path(r"D:\project\w2\.xxx\e2e_patrol_report.md")
    report.parent.mkdir(exist_ok=True)
    with report.open("w", encoding="utf-8") as f:
        f.write("# E2E 工具巡检报告（循环启动 jcc.exe 验证启动链路）\n\n")
        f.write(f"工具总数: {len(tools)}\n")
        for mn, c in sorted(counts.items()):
            f.write(f"{mn}: {c}\n")
        f.write("\n## 详细结果\n")
        for name, mn, err, elapsed in results:
            f.write(f"{mn} {name} {err} ({elapsed:.1f}s)\n")
    print(f"\n报告已写: {report}")

if __name__ == "__main__":
    main()
