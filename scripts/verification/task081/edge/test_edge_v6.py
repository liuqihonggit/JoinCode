#!/usr/bin/env python3
"""jcc 边缘条件测试 v6 — 每个场景实时打印, 输出到 .xxx/logs/"""
import subprocess, json, time, urllib.request, sys, os, threading, uuid, socket
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_EXE = "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"
MOCK = "http://localhost:9901"
MODEL = "gpt-5.6-sol"
LOGDIR = Path(".xxx/logs")
LOGDIR.mkdir(parents=True, exist_ok=True)
ENV_BASE = {
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": MODEL,
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
    "JCC_DISABLE_RETRY": "1",
}
ENV = {**os.environ, **ENV_BASE}

results = []
lock = threading.Lock()
mock_proc = None

def log(msg):
    print(msg, flush=True)

def mock_health():
    try:
        req = urllib.request.Request(f"{MOCK}/__control__/state")
        resp = urllib.request.urlopen(req, timeout=3)
        return json.loads(resp.read()) is not None
    except:
        return False

def mock_start():
    global mock_proc
    if mock_proc and mock_proc.poll() is None:
        return True
    log("  [启动 MockServer...] ")
    mock_proc = subprocess.Popen(
        [MOCK_EXE, "--port", "9901", "--interactive", "--no-open-browser"],
        stdout=open(LOGDIR / "mockserver_edge.log", "w"),
        stderr=subprocess.STDOUT,
        env={**os.environ},
    )
    for _ in range(10):
        time.sleep(1)
        if mock_health():
            log("  OK")
            return True
    log("  FAILED")
    return False

def mock_ensure():
    if mock_health():
        return True
    log("  [MockServer 不健康, 重启...]")
    if mock_proc:
        mock_proc.kill()
        mock_proc.wait(timeout=5)
    return mock_start()

def mock_clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try: urllib.request.urlopen(req, timeout=3)
    except: pass

def sse_line(obj):
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop"):
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    if reasoning:
        if isinstance(reasoning, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": reasoning}}]}))
        else:
            for r in reasoning:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    if content_text is not None:
        if isinstance(content_text, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
        else:
            for c in content_text:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": c}}]}))
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    chunks.append("data: [DONE]\n\n")
    return chunks

def inject_stream(chunks, status=200, content_type="text/event-stream"):
    mock_clear()
    body = {"status_code": status, "content_type": content_type, "is_stream": True, "stream_chunks": chunks}
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=json.dumps(body).encode(), method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=3)
    except: pass

def inject_simple(text="完成", **kwargs):
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

def run_jcc(prompt, await_sec=15, extra_args=None, env_override=None):
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args: args.extend(extra_args)
    e = {**ENV}
    if env_override: e.update(env_override)
    try:
        proc = subprocess.run(args, env=e, capture_output=True, text=True, timeout=await_sec+10)
        return proc.returncode, proc.stdout, proc.stderr
    except subprocess.TimeoutExpired:
        return -999, "", "TIMEOUT"

def run_jcc_cmd(args, timeout=15):
    cmd = [JCC, "--trust", "--non-interactive"] + args
    try:
        proc = subprocess.run(cmd, env=ENV, capture_output=True, text=True, timeout=timeout+5)
        return proc.returncode, proc.stdout, proc.stderr
    except subprocess.TimeoutExpired:
        return -999, "", "TIMEOUT"

def assert_eq(actual, expected, msg):
    ok = actual == expected
    with lock:
        results.append(ok)
    if ok:
        log(f"  ✓ {msg}")
    else:
        log(f"  ✗ {msg}: expected {expected}, got {actual}")
    return ok

def assert_ne(actual, unexpected, msg):
    ok = actual != unexpected
    with lock:
        results.append(ok)
    if ok:
        log(f"  ✓ {msg}")
    else:
        log(f"  ✗ {msg}: should not be {unexpected}, got {actual}")
    return ok

def assert_contains(haystack, needle, msg):
    ok = needle in haystack
    with lock:
        results.append(ok)
    if ok:
        log(f"  ✓ {msg}")
    else:
        log(f"  ✗ {msg}: '{needle}' not found in output")
    return ok

def test(name, fn, need_mock=True):
    log(f"\n--- {name} ---")
    try:
        if need_mock and not mock_ensure():
            with lock: results.append(False)
            log(f"  ✗ MockServer 不可用")
            return
        fn()
    except Exception as e:
        with lock: results.append(False)
        log(f"  ✗ 异常: {e}")

# ============================================================
print("=" * 60)
print("jcc 边缘条件测试 v6")
print("=" * 60)

# === 1. 超长提示词 ===
print("\n=== 1. 超长提示词 ===")

def t_huge_prompt_10k():
    inject_simple("收到长提示词")
    prompt = "请处理这段文本: " + "A" * 10000
    code, out, err = run_jcc(prompt, await_sec=15)
    assert_eq(code, 0, "10KB 提示词")
test("10KB 提示词", t_huge_prompt_10k)

def t_huge_prompt_30k():
    inject_simple("收到超长提示词")
    prompt = "请处理: " + "B" * 30000
    code, out, err = run_jcc(prompt, await_sec=20)
    assert_eq(code, 0, "30KB 提示词")
test("30KB 提示词", t_huge_prompt_30k)

# === 2. 超大响应 ===
print("\n=== 2. 超大响应 ===")

def t_huge_response_10k():
    inject_simple("C" * 10000)
    code, out, err = run_jcc("生成10KB文本", await_sec=15)
    assert_eq(code, 0, "10KB 响应")
    assert_contains(out, "C" * 100, "10KB 响应内容包含")
test("10KB 响应", t_huge_response_10k)

def t_huge_response_100k():
    inject_simple("D" * 100000)
    code, out, err = run_jcc("生成100KB文本", await_sec=20)
    assert_eq(code, 0, "100KB 响应")
test("100KB 响应", t_huge_response_100k)

# === 3. 空响应 ===
print("\n=== 3. 空响应 ===")

def t_empty_content():
    inject_simple("")
    code, out, err = run_jcc("空响应测试", await_sec=10)
    # 空 content 是设计行为: jcc 视为错误 exit=1, 只要不卡死即可
    assert_ne(code, -999, "空 content 不卡死")
test("空 content", t_empty_content)

def t_empty_stream():
    # 只有 role 和 DONE, 没有 content
    chunks = [
        sse_line({"id": "c1", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
        sse_line({"id": "c1", "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n",
    ]
    inject_stream(chunks)
    code, out, err = run_jcc("空流测试", await_sec=10)
    assert_ne(code, -999, "空流(无content) 不卡死")
test("空流(无content)", t_empty_stream)

# === 4. SSE 格式错误 ===
print("\n=== 4. SSE 格式错误 ===")

def t_malformed_sse():
    mock_clear()
    body = {"status_code": 200, "content_type": "text/event-stream", "is_stream": True, "stream_chunks": ["not a valid sse line\n\n", "data: broken json\n\n"]}
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=json.dumps(body).encode(), method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=3)
    except: pass
    code, out, err = run_jcc("格式错误测试", await_sec=15)
    assert_ne(code, -999, "格式错误不卡死")
test("SSE 格式错误", t_malformed_sse)

def t_missing_done():
    # 没有 data: [DONE] 结尾
    chunks = [
        sse_line({"id": "c1", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
        sse_line({"id": "c1", "choices": [{"index": 0, "delta": {"content": "没有DONE"}}]}),
        sse_line({"id": "c1", "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
    ]
    inject_stream(chunks)
    code, out, err = run_jcc("缺少DONE测试", await_sec=15)
    assert_ne(code, -999, "缺少DONE不卡死")
test("缺少 [DONE]", t_missing_done)

# === 5. 工具调用边缘 ===
print("\n=== 5. 工具调用边缘 ===")

def t_tool_empty_args():
    inject_stream(make_stream_chunks(tool_calls=[{"id": "c1", "name": "read", "args": {}}], finish_reason="tool_calls"))
    code, out, err = run_jcc("空参数工具调用", await_sec=15)
    assert_ne(code, -999, "空参数工具不卡死")
test("空参数工具调用", t_tool_empty_args)

def t_tool_unknown_name():
    inject_stream(make_stream_chunks(tool_calls=[{"id": "c1", "name": "nonexistent_tool", "args": {"x": 1}}], finish_reason="tool_calls"))
    code, out, err = run_jcc("未知工具调用", await_sec=15)
    assert_ne(code, -999, "未知工具不卡死")
test("未知工具调用", t_tool_unknown_name)

def t_tool_huge_args():
    huge_args = {"filePath": ".xxx/test_huge_args.txt", "content": "X" * 50000}
    inject_stream(make_stream_chunks(tool_calls=[{"id": "c1", "name": "write", "args": huge_args}], finish_reason="tool_calls"))
    code, out, err = run_jcc("超大参数工具", await_sec=20)
    assert_ne(code, -999, "超大参数工具不卡死")
test("超大参数工具", t_tool_huge_args)

# === 6. 多轮工具链 ===
print("\n=== 6. 多轮工具链 ===")

def t_5_tool_chain():
    tools = [{"id": f"c{i}", "name": "read", "args": {"filePath": "README.md"}} for i in range(5)]
    inject_stream(make_stream_chunks(tool_calls=tools, finish_reason="tool_calls"))
    code, out, err = run_jcc("5个工具并行", await_sec=20)
    # 工具调用后需要第二轮 LLM 响应, MockServer 只注入一次, 只要不卡死即可
    assert_ne(code, -999, "5个工具并行不卡死")
test("5个工具并行", t_5_tool_chain)

# === 7. 供应商切换 ===
print("\n=== 7. 供应商切换 ===")

def t_vendor_anthropic():
    inject_simple("anthropic 响应")
    code, out, err = run_jcc("供应商测试", extra_args=["--vendor", "anthropic"], env_override={"JCC_VENDOR": "anthropic", "JCC_MODEL_ID": "claude-3-5-sonnet"})
    assert_ne(code, -999, "anthropic 供应商不卡死")
test("anthropic 供应商", t_vendor_anthropic)

def t_vendor_deepseek():
    inject_simple("deepseek 响应")
    code, out, err = run_jcc("供应商测试", extra_args=["--vendor", "deepseek"], env_override={"JCC_VENDOR": "deepseek", "JCC_MODEL_ID": "deepseek-chat"})
    assert_ne(code, -999, "deepseek 供应商不卡死")
test("deepseek 供应商", t_vendor_deepseek)

# === 8. 特殊字符 ===
print("\n=== 8. 特殊字符 ===")

def t_unicode_mixed():
    inject_simple("中文 English 日本語 한국어 Русский")
    code, out, err = run_jcc("多语言测试", await_sec=10)
    assert_eq(code, 0, "多语言混合")
test("多语言混合", t_unicode_mixed)

def t_emoji():
    inject_simple("测试 🎉🚀✨🔥💯")
    code, out, err = run_jcc("emoji 测试", await_sec=10)
    assert_eq(code, 0, "emoji")
test("emoji", t_emoji)

def t_newlines():
    inject_simple("第一行\n第二行\n\r第三行\r\n")
    code, out, err = run_jcc("换行符测试", await_sec=10)
    assert_eq(code, 0, "各种换行符")
test("各种换行符", t_newlines)

# === 9. 逐字符流式 ===
print("\n=== 9. 逐字符流式 ===")

def t_char_by_char():
    text = "逐字符输出测试"
    inject_simple(list(text))
    code, out, err = run_jcc("逐字符测试", await_sec=15)
    assert_eq(code, 0, "逐字符流式")
    assert_contains(out, "逐字符输出测试", "逐字符内容完整")
test("逐字符流式", t_char_by_char)

# === 10. 并发 ===
print("\n=== 10. 并发 ===")

def t_concurrent_5():
    inject_simple("并发响应")
    def run_one():
        return run_jcc("并发测试", await_sec=15)
    threads = []
    out_results = []
    def worker():
        out_results.append(run_one())
    for _ in range(5):
        t = threading.Thread(target=worker)
        threads.append(t)
        t.start()
    for t in threads:
        t.join(timeout=30)
    # 并发时 MockServer 只注入一次响应, 至少 1 个成功即可
    ok_count = sum(1 for r in out_results if r[0] == 0)
    ok = ok_count >= 1
    with lock: results.append(ok)
    if ok:
        log(f"  ✓ 5进程并发: {ok_count}/5 成功")
    else:
        log(f"  ✗ 5进程并发: 全部失败 {[r[0] for r in out_results]}")
test("5进程并发", t_concurrent_5)

# === 11. --force-interactive ===
print("\n=== 11. --force-interactive ===")

def t_force_interactive():
    # 不带 --non-interactive, 带 --force-interactive
    args = [JCC, "--trust", "--force-interactive", "--await", "5"]
    try:
        proc = subprocess.run(args, env=ENV, capture_output=True, text=True, timeout=15, input="/exit\n")
        # 不应报 "交互模式需要真实终端"
        ok = "交互模式需要真实终端" not in proc.stderr
        with lock: results.append(ok)
        if ok:
            log("  ✓ --force-interactive 不报 TTY 错误")
        else:
            log(f"  ✗ --force-interactive 报 TTY 错误: {proc.stderr[:200]}")
    except Exception as e:
        with lock: results.append(False)
        log(f"  ✗ 异常: {e}")
test("--force-interactive TTY", t_force_interactive, need_mock=False)

# === 12. rg 子命令 ===
print("\n=== 12. rg 子命令 ===")

def t_rg_basic():
    code, out, err = run_jcc_cmd(["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])
    assert_eq(code, 0, "rg 基本搜索")
test("rg 基本搜索", t_rg_basic, need_mock=False)

def t_rg_no_match():
    code, out, err = run_jcc_cmd(["rg", "ZZZNOTEXISTZZZ", "modules/"])
    assert_eq(code, 1, "rg 无匹配 exit=1")
test("rg 无匹配", t_rg_no_match, need_mock=False)

def t_rg_regex():
    code, out, err = run_jcc_cmd(["rg", "internal.*class", "modules/tools/rg/", "--type", "cs", "--max-count", "3"])
    assert_eq(code, 0, "rg 正则搜索")
test("rg 正则搜索", t_rg_regex, need_mock=False)

# === 13. 元命令 ===
print("\n=== 13. 元命令 ===")

def t_mcp_list():
    code, out, err = run_jcc_cmd(["mcp_list"])
    assert_eq(code, 0, "mcp_list")
test("mcp_list", t_mcp_list, need_mock=False)

def t_slash_list():
    code, out, err = run_jcc_cmd(["slash_list"])
    assert_eq(code, 0, "slash_list")
test("slash_list", t_slash_list, need_mock=False)

def t_schema():
    code, out, err = run_jcc_cmd(["schema"])
    assert_eq(code, 0, "schema")
test("schema", t_schema, need_mock=False)

# === 14. 环境变量 ===
print("\n=== 14. 环境变量 ===")

def t_json_output():
    inject_simple("JSON")
    code, out, err = run_jcc("json", extra_args=["--json"])
    assert_eq(code, 0, "--json 输出")
test("--json 输出", t_json_output)

def t_quiet():
    inject_simple("quiet")
    code, out, err = run_jcc("quiet", extra_args=["--quiet"])
    assert_eq(code, 0, "--quiet")
test("--quiet", t_quiet)

def t_model_switch():
    inject_simple("模型切换")
    code, out, err = run_jcc("模型测试", extra_args=["-m", "gpt-5.6-sol"])
    assert_eq(code, 0, "-m 模型切换")
test("-m 模型切换", t_model_switch)

# === 15. 上下文压缩 ===
print("\n=== 15. 上下文压缩 ===")

def t_small_context():
    inject_simple("小上下文")
    code, out, err = run_jcc("小上下文测试", env_override={"JCC_MAX_CONTEXT_CHARS": "100"})
    assert_eq(code, 0, "小上下文窗口")
test("小上下文窗口", t_small_context)

# ============================================================
print("\n" + "=" * 60)
print("测试汇总")
print("=" * 60)
total = len(results)
passed = sum(results)
failed = total - passed
print(f"总计: {total} | 通过: {passed} | 失败: {failed}")

if failed > 0:
    print("\n失败详情:")
    print("(见上方 ✗ 标记)")

# 写结果到 .xxx/logs/
with open(LOGDIR / "test_edge_v6_result.txt", "w", encoding="utf-8") as f:
    f.write(f"总计: {total} | 通过: {passed} | 失败: {failed}\n")

# 关闭 MockServer
if mock_proc:
    mock_proc.kill()

sys.exit(0 if failed == 0 else 1)
