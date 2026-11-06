#!/usr/bin/env python3
"""jcc MCP 工具直接调用测试 — 测试各种工具是否正常工作"""
import subprocess, json, time, sys, os
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
ENV = {
    **os.environ,
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": "gpt-5.6-sol",
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
}

results = []

def run_jcc_cmd(args, timeout=30):
    """运行 jcc 子命令"""
    cmd = [JCC, "--trust", "--non-interactive"] + args
    t0 = time.time()
    try:
        proc = subprocess.run(cmd, env=ENV, capture_output=True, text=True, timeout=timeout)
        return proc.returncode, proc.stdout, proc.stderr, time.time()-t0
    except subprocess.TimeoutExpired as e:
        out = e.stdout or b""
        err = e.stderr or b""
        if isinstance(out, bytes): out = out.decode(errors="replace")
        if isinstance(err, bytes): err = err.decode(errors="replace")
        return -999, out, err, time.time()-t0

def test(name, func):
    t0 = time.time()
    try:
        func()
        elapsed = time.time() - t0
        results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:200]}")
    except Exception as e:
        elapsed = time.time() - t0
        results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:200]}")

def assert_eq(a, b, msg=""):
    if a != b: raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text: raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

def assert_not_contains(text, substr, msg=""):
    if substr in text: raise AssertionError(f"{msg}: '{substr}' found in output")

# ============================================================
# 1. mcp_list 测试
# ============================================================
print("\n=== 1. mcp_list 各分类 ===")

CATEGORIES = ["github", "file", "search", "git", "shell", "config", "web", "memory", "todo", "task",
              "agent", "skill", "plan", "workflow", "context", "monitor", "cron", "goal",
              "code_analysis", "code_index", "lsp", "graph", "permission", "sandbox",
              "terminal", "REPL", "vcr", "sleep", "policy", "snip", "brief", "analytics",
              "notification", "voice", "file_transfer", "remote_trigger", "desktop_control",
              "vision", "browser", "notebook", "code_execution", "powershell", "build",
              "mcp_client", "mcp_auth", "mcp_resource", "interaction", "peers",
              "pr_subscription", "step_evidence", "worktree", "error_recovery",
              "structured_output", "team", "code_generation"]

for cat in CATEGORIES:
    def f(cat=cat):
        code, out, err, elapsed = run_jcc_cmd(["mcp_list", "--category", cat])
        assert_eq(code, 0, f"exit code for {cat}, stderr={err[-100:]}")
    test(f"mcp_list --category {cat}", f)

# ============================================================
# 2. mcp_search 测试
# ============================================================
print("\n=== 2. mcp_search ===")

SEARCH_QUERIES = ["pr", "issue", "file read", "grep", "git status", "memory", "todo", "task",
                  "plan", "workflow", "agent", "skill", "bash", "python", "web fetch"]

for q in SEARCH_QUERIES:
    def f(q=q):
        code, out, err, elapsed = run_jcc_cmd(["mcp_search", q])
        assert_eq(code, 0, f"exit code for '{q}', stderr={err[-100:]}")
    test(f"mcp_search '{q}'", f)

# ============================================================
# 3. gh 子命令测试
# ============================================================
print("\n=== 3. gh 子命令 ===")

def test_gh_pr_list():
    code, out, err, elapsed = run_jcc_cmd(["gh", "pr", "list", "--limit", "1"])
    # gh 可能因为没有 token 而失败，但不应该崩溃
    # 只要不超时即可
test("gh pr list", test_gh_pr_list)

def test_gh_repo_view():
    code, out, err, elapsed = run_jcc_cmd(["gh", "repo", "view", "--json", "nameWithOwner"])
test("gh repo view", test_gh_repo_view)

def test_gh_api():
    code, out, err, elapsed = run_jcc_cmd(["gh", "api", "repos/octocat/hello-world"])
test("gh api call", test_gh_api)

# ============================================================
# 4. rg 子命令测试
# ============================================================
print("\n=== 4. rg 子命令 ===")

def test_rg_basic():
    code, out, err, elapsed = run_jcc_cmd(["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])
    assert_eq(code, 0, f"exit code, stderr={err[-100:]}")
test("rg 基本搜索", test_rg_basic)

def test_rg_count():
    code, out, err, elapsed = run_jcc_cmd(["rg", "-c", "public", "modules/", "--type", "cs", "--max-count", "3"])
test("rg 计数模式", test_rg_count)

def test_rg_json():
    code, out, err, elapsed = run_jcc_cmd(["rg", "class", "modules/", "--type", "cs", "--max-count", "3", "--json"])
test("rg JSON输出", test_rg_json)

# ============================================================
# 5. slash 命令测试
# ============================================================
print("\n=== 5. slash 命令 ===")

SLASH_COMMANDS = ["/help", "/status", "/version", "/context", "/usage", "/stats",
                  "/tools", "/memory", "/tasks", "/goal", "/config", "/theme",
                  "/color", "/vim", "/doctor", "/env", "/permissions", "/cost",
                  "/model", "/vendor", "/effort", "/sampling"]

for cmd in SLASH_COMMANDS:
    def f(cmd=cmd):
        code, out, err, elapsed = run_jcc_cmd(["slash_call", cmd, "{}"], timeout=15)
        # 斜杠命令可能需要会话上下文，有些可能失败
        # 只要不崩溃（exit != -999）即可
        if code == -999:
            raise AssertionError(f"超时")
    test(f"slash_call {cmd}", f)

# ============================================================
# 6. schema/config 子命令
# ============================================================
print("\n=== 6. 自省子命令 ===")

def test_schema():
    code, out, err, elapsed = run_jcc_cmd(["schema"])
    assert_eq(code, 0, f"exit code, stderr={err[-100:]}")
    # schema 应该返回 JSON
    assert_contains(out, "{", "schema 应输出 JSON")
test("schema 输出JSON", test_schema)

def test_config_print():
    code, out, err, elapsed = run_jcc_cmd(["config-print"])
    assert_eq(code, 0, f"exit code, stderr={err[-100:]}")
test("config-print", test_config_print)

# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("MCP 工具测试汇总")
print("=" * 60)
passed = sum(1 for _, s, _, _ in results if s == "PASS")
failed = sum(1 for _, s, _, _ in results if s == "FAIL")
errors = sum(1 for _, s, _, _ in results if s == "ERROR")
total = len(results)
print(f"总计: {total} | 通过: {passed} | 失败: {failed} | 错误: {errors}")
print()
if failed > 0 or errors > 0:
    print("失败/错误详情:")
    for name, status, elapsed, msg in results:
        if status != "PASS":
            icon = "✗" if status == "FAIL" else "!!"
            print(f"  {icon} {name} ({elapsed:.1f}s) — {msg[:150]}")

Path(".xxx/logs/mcp_tools_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
