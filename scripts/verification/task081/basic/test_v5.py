#!/usr/bin/env python3
"""jcc 全面测试框架 v5 — 带健康检查 + 快速错误机制"""
import subprocess, json, time, urllib.request, sys, os, threading, uuid, socket
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_EXE = "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"
MOCK = "http://localhost:9901"
MODEL = "gpt-5.6-sol"
ENV_BASE = {
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": MODEL,
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
    "JCC_DISABLE_RETRY": "1",  # 禁用重试，让错误快速失败
}
# 合并当前环境变量
ENV = {**os.environ, **ENV_BASE}

results = []
lock = threading.Lock()
mock_proc = None

# ============================================================
# MockServer 健康检查与管理
# ============================================================

def mock_health():
    """检查 MockServer 是否健康"""
    try:
        req = urllib.request.Request(f"{MOCK}/__control__/state")
        resp = urllib.request.urlopen(req, timeout=3)
        return json.loads(resp.read()) is not None
    except:
        return False

def mock_start():
    """启动 MockServer"""
    global mock_proc
    if mock_proc and mock_proc.poll() is None:
        return True
    print("  [启动 MockServer...]", end="", flush=True)
    mock_proc = subprocess.Popen(
        [MOCK_EXE, "--port", "9901", "--interactive", "--no-open-browser"],
        stdout=open(".xxx/logs/mockserver.log", "w"),
        stderr=subprocess.STDOUT,
        env={**os.environ},
    )
    # 等待健康
    for _ in range(10):
        time.sleep(1)
        if mock_health():
            print(" OK")
            return True
    print(" FAILED")
    return False

def mock_ensure():
    """确保 MockServer 健康，不健康就重启"""
    if mock_health():
        return True
    print("  [MockServer 不健康，重启...]", end="", flush=True)
    if mock_proc:
        mock_proc.kill()
        mock_proc.wait(timeout=5)
    return mock_start()

def mock_clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try: urllib.request.urlopen(req, timeout=3)
    except: pass

# ============================================================
# 注入工具
# ============================================================

def sse_line(obj):
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop", no_done=False):
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
    if not no_done: chunks.append("data: [DONE]\n\n")
    return chunks

def inject_stream(chunks, status=200, headers=None, delay_ms=0):
    body = {"status": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0: body["delay_ms"] = delay_ms
    if headers: body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_error_stream(status, message="error"):
    """注入流式错误响应 — 快速失败，不触发流式回退"""
    body = {"status": status, "is_stream": True, "stream_chunks": [], "content_type": "text/event-stream",
            "body": json.dumps({"error": {"message": message, "code": str(status)}})}
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_non_stream(body_text, status=200, headers=None):
    body = {"status": status, "is_stream": False, "body": body_text, "content_type": "application/json"}
    if headers: body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_simple(text="完成", **kwargs):
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

def inject_tool_call(tool_name, tool_args, result_text="工具执行完成"):
    inject_stream(make_stream_chunks(tool_calls=[{"id": "call_1", "name": tool_name, "args": tool_args}], finish_reason="tool_calls"))
    inject_simple(result_text)

# ============================================================
# jcc 运行
# ============================================================

def run_jcc(prompt, await_sec=15, extra_args=None, env_override=None):
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args: args.extend(extra_args)
    e = {**ENV, **(env_override or {})}
    t0 = time.time()
    try:
        proc = subprocess.run(args, env=e, capture_output=True, text=True, timeout=await_sec+5)
        return proc.returncode, proc.stdout, proc.stderr, time.time()-t0
    except subprocess.TimeoutExpired as ex:
        out = ex.stdout or b""
        err = ex.stderr or b""
        if isinstance(out, bytes): out = out.decode(errors="replace")
        if isinstance(err, bytes): err = err.decode(errors="replace")
        return -999, out, err, time.time()-t0

def run_jcc_cmd(args, timeout=15):
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

# ============================================================
# 测试框架
# ============================================================

def test(name, func, need_mock=True):
    if need_mock:
        if not mock_ensure():
            with lock: results.append((name, "SKIP", 0, "MockServer 不可用"))
            print(f"  ⊘ {name} (SKIP: MockServer 不可用)")
            return
    mock_clear()
    t0 = time.time()
    try:
        func()
        elapsed = time.time() - t0
        with lock: results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        with lock: results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:150]}")
    except Exception as e:
        elapsed = time.time() - t0
        with lock: results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:150]}")
    mock_clear()

def assert_eq(a, b, msg=""):
    if a != b: raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text: raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

# ============================================================
# 开头：健康检查 + 启动 MockServer
# ============================================================
print("=" * 60)
print("jcc 全面测试 v5 — 带健康检查 + 快速错误机制")
print("=" * 60)

if not mock_health():
    if not mock_start():
        print("FATAL: MockServer 启动失败，无法继续测试")
        sys.exit(1)
else:
    print("MockServer 已在运行")

print(f"JCC_DISABLE_RETRY=1 (快速错误失败)")
print()

# ============================================================
# 1. 基础流式响应
# ============================================================
print("=== 1. 基础流式响应 ===")

def t_basic():
    inject_simple("你好世界")
    code, out, err, el = run_jcc("你好")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
    assert_contains(out, "你好", "输出应包含响应")
test("基础流式", t_basic)

def t_multi_chunk():
    inject_stream(make_stream_chunks(content_text=["你好", "世界", "！"]))
    code, out, err, el = run_jcc("多chunk")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("多chunk", t_multi_chunk)

def t_long():
    inject_simple("文本" * 200)
    code, out, err, el = run_jcc("长文本")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("超长内容", t_long)

def t_unicode():
    inject_simple("🎉🚀✨\n你好\t\"引号\"")
    code, out, err, el = run_jcc("Unicode")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("Unicode+Emoji", t_unicode)

# ============================================================
# 2. 工具调用
# ============================================================
print("\n=== 2. 工具调用 ===")

def t_tool():
    inject_tool_call("read", {"filePath": "test.cs"}, "完成")
    code, out, err, el = run_jcc("读取文件")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("单工具(read)", t_tool)

def t_2tools():
    inject_stream(make_stream_chunks(
        tool_calls=[{"id": "c1", "name": "read", "args": {"filePath": "README.md"}},
                     {"id": "c2", "name": "glob", "args": {"pattern": "**/*.cs"}}],
        finish_reason="tool_calls"))
    inject_simple("完成")
    code, out, err, el = run_jcc("双工具")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("双工具并行", t_2tools)

def t_3tools():
    inject_stream(make_stream_chunks(
        tool_calls=[{"id": "c1", "name": "read", "args": {"filePath": "README.md"}},
                     {"id": "c2", "name": "glob", "args": {"pattern": "**/*.ts"}},
                     {"id": "c3", "name": "edit", "args": {"filePath": "README.md"}}],
        finish_reason="tool_calls"))
    inject_simple("完成")
    code, out, err, el = run_jcc("三工具")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("三工具并行", t_3tools)

def t_10tools():
    tools = [{"id": f"c{i}", "name": "read", "args": {"filePath": "README.md"}} for i in range(10)]
    inject_stream(make_stream_chunks(tool_calls=tools, finish_reason="tool_calls"))
    inject_simple("完成")
    code, out, err, el = run_jcc("10工具", await_sec=30)
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("10工具并行", t_10tools)

# ============================================================
# 3. 思考链
# ============================================================
print("\n=== 3. 思考链 ===")

def t_reasoning():
    inject_stream(make_stream_chunks(reasoning=["思考...", "答案42"], content_text="42"))
    code, out, err, el = run_jcc("思考")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("reasoning", t_reasoning)

def t_big_reasoning():
    inject_stream(make_stream_chunks(reasoning="思" * 2500, content_text="结论"))
    code, out, err, el = run_jcc("大思考")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("超大思考链", t_big_reasoning)

def t_reasoning_tool():
    inject_stream(make_stream_chunks(
        reasoning=["需要读文件"],
        tool_calls=[{"id": "c1", "name": "read", "args": {"filePath": "t.cs"}}],
        finish_reason="tool_calls"))
    inject_simple("完成")
    code, out, err, el = run_jcc("思考+工具")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("思考+工具", t_reasoning_tool)

# ============================================================
# 4. 边缘条件
# ============================================================
print("\n=== 4. 边缘条件 ===")

def t_null_content():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": None}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "有效"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
              "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("null")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("null content", t_null_content)

def t_no_done():
    inject_stream(make_stream_chunks(content_text="无DONE", no_done=True))
    code, out, err, el = run_jcc("无DONE")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("无[DONE]", t_no_done)

def t_empty_tool_calls():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "开始"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"tool_calls": []}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "结束"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
              "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("空tool")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("空tool_calls", t_empty_tool_calls)

def t_malformed():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "正常"}}]}),
              "data: BAD_JSON\n\n",
              sse_line({"choices": [{"index": 0, "delta": {"content": "恢复"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
              "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("畸形")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("畸形JSON容错", t_malformed)

def t_sse_comment():
    chunks = [": comment\n\n", sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("SSE注释")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("SSE注释行", t_sse_comment)

def t_done_space():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]  \n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("DONE空格")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("[DONE]带空格", t_done_space)

def t_extra_after_done():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
              "data: [DONE]\n\n", sse_line({"choices": [{"index": 0, "delta": {"content": "额外"}}]})]
    inject_stream(chunks)
    code, out, err, el = run_jcc("DONE后数据")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("[DONE]后数据", t_extra_after_done)

def t_multi_choices():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "A"}}, {"index": 1, "delta": {"content": "B"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("多choices")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("多choices(n>1)", t_multi_choices)

def t_finish_length():
    inject_stream(make_stream_chunks(content_text="部分", finish_reason="length"))
    code, out, err, el = run_jcc("length")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("finish_reason=length", t_finish_length)

def t_delta_null():
    chunks = [sse_line({"choices": [{"index": 0, "delta": None}]}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("null delta")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("delta=null", t_delta_null)

def t_empty_choices():
    chunks = [sse_line({"choices": []}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("空choices")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("空choices数组", t_empty_choices)

def t_model_change():
    chunks = [sse_line({"model": "a", "choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"model": "b", "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("model变化")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("model名变化", t_model_change)

def t_tool_index_jump():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "c1", "function": {"name": "read_file", "arguments": "{}"}}]}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 2, "id": "c3", "function": {"name": "grep", "arguments": "{}"}}]}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    inject_simple("完成")
    code, out, err, el = run_jcc("index跳跃")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("tool_call index跳跃", t_tool_index_jump)

# ============================================================
# 5. 错误处理（快速机制：流式错误 + 禁用重试 + 短超时）
# ============================================================
print("\n=== 5. 错误处理（快速） ===")

def t_400():
    inject_error_stream(400, "bad request")
    code, out, err, el = run_jcc("400", await_sec=10)
    assert code != 0, f"400应非零退出, got {code}"
test("400 Bad Request", t_400)

def t_401():
    inject_error_stream(401, "unauthorized")
    code, out, err, el = run_jcc("401", await_sec=10)
    assert code != 0, f"401应非零退出, got {code}"
test("401 Unauthorized", t_401)

def t_403():
    inject_error_stream(403, "forbidden")
    code, out, err, el = run_jcc("403", await_sec=10)
    assert code != 0, f"403应非零退出, got {code}"
test("403 Forbidden", t_403)

def t_404():
    inject_error_stream(404, "not found")
    code, out, err, el = run_jcc("404", await_sec=10)
    assert code != 0, f"404应非零退出, got {code}"
test("404 Not Found", t_404)

def t_429():
    inject_error_stream(429, "rate limited")
    code, out, err, el = run_jcc("429", await_sec=10)
    assert code != 0, f"429应非零退出, got {code}"
test("429 Rate Limited", t_429)

def t_500():
    inject_error_stream(500, "server error")
    code, out, err, el = run_jcc("500", await_sec=10)
    assert code != 0, f"500应非零退出, got {code}"
test("500 Server Error", t_500)

def t_502():
    inject_error_stream(502, "bad gateway")
    code, out, err, el = run_jcc("502", await_sec=10)
    assert code != 0, f"502应非零退出, got {code}"
test("502 Bad Gateway", t_502)

def t_503():
    inject_error_stream(503, "unavailable")
    code, out, err, el = run_jcc("503", await_sec=10)
    assert code != 0, f"503应非零退出, got {code}"
test("503 Unavailable", t_503)

def t_504():
    inject_error_stream(504, "timeout")
    code, out, err, el = run_jcc("504", await_sec=10)
    assert code != 0, f"504应非零退出, got {code}"
test("504 Timeout", t_504)

# ============================================================
# 6. 混合响应
# ============================================================
print("\n=== 6. 混合响应 ===")

def t_content_then_tool():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "我来读取"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "c1", "function": {"name": "read", "arguments": json.dumps({"filePath": "t.cs"})}}]}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    inject_simple("完成")
    code, out, err, el = run_jcc("文本后工具")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("content+tool_calls", t_content_then_tool)

def t_tool_then_content():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "c1", "function": {"name": "read", "arguments": json.dumps({"filePath": "t.cs"})}}]}}]}),
              sse_line({"choices": [{"index": 0, "delta": {"content": "正在读取"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    inject_simple("完成")
    code, out, err, el = run_jcc("工具后文本")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("tool_calls+content", t_tool_then_content)

# ============================================================
# 7. 多轮工具链
# ============================================================
print("\n=== 7. 多轮工具链 ===")

def t_chain3():
    inject_tool_call("read", {"filePath": "README.md"})
    inject_tool_call("grep", {"pattern": "class"})
    inject_simple("完成")
    code, out, err, el = run_jcc("3轮链", await_sec=30)
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("3轮工具链", t_chain3)

def t_chain5():
    for i in range(4):
        inject_tool_call("read", {"filePath": "README.md"})
    inject_simple("完成")
    code, out, err, el = run_jcc("5轮链", await_sec=45)
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("5轮工具链", t_chain5)

# ============================================================
# 8. 超大参数/响应
# ============================================================
print("\n=== 8. 超大参数/响应 ===")

def t_huge_args():
    inject_tool_call("write", {"content": "A" * 10000, "filePath": "t.txt"}, "完成")
    code, out, err, el = run_jcc("大参数")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("超大工具参数(10KB)", t_huge_args)

def t_huge_response():
    inject_simple("X" * 50000)
    code, out, err, el = run_jcc("大响应")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("超大响应(50KB)", t_huge_response)

# ============================================================
# 9. 特殊字符
# ============================================================
print("\n=== 9. 特殊字符 ===")

def t_unicode_args():
    inject_tool_call("write", {"filePath": "测试/文件.txt", "content": "中文🎉"}, "完成")
    code, out, err, el = run_jcc("Unicode参数")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("Unicode工具参数", t_unicode_args)

def t_json_args():
    inject_tool_call("write", {"filePath": "t.json", "content": json.dumps({"k": "v"})}, "完成")
    code, out, err, el = run_jcc("JSON参数")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("嵌套JSON参数", t_json_args)

def t_special_args():
    inject_tool_call("write", {"filePath": "t.txt", "content": '"\'\n\t\\'}, "完成")
    code, out, err, el = run_jcc("特殊字符")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("特殊字符参数", t_special_args)

# ============================================================
# 10. 逐字符流式
# ============================================================
print("\n=== 10. 逐字符流式 ===")

def t_char_by_char():
    text = "Hello World 你好世界"
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]})]
    for ch in text:
        chunks.append(sse_line({"choices": [{"index": 0, "delta": {"content": ch}}]}))
    chunks.append(sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}))
    chunks.append("data: [DONE]\n\n")
    inject_stream(chunks)
    code, out, err, el = run_jcc("逐字符")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("逐字符(20+chunk)", t_char_by_char)

# ============================================================
# 11. usage 统计
# ============================================================
print("\n=== 11. usage 统计 ===")

def t_usage_first():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}], "usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150}}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}), "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("usage首chunk")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("usage在首个chunk", t_usage_first)

def t_usage_last():
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
              sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150}}),
              "data: [DONE]\n\n"]
    inject_stream(chunks)
    code, out, err, el = run_jcc("usage末chunk")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("usage在末尾chunk", t_usage_last)

def t_no_usage():
    inject_simple("无usage")
    code, out, err, el = run_jcc("无usage")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("无usage", t_no_usage)

# ============================================================
# 12. 延迟响应
# ============================================================
print("\n=== 12. 延迟响应 ===")

def t_2s():
    inject_stream(make_stream_chunks(content_text="延迟"), delay_ms=2000)
    code, out, err, el = run_jcc("2秒")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("2秒延迟", t_2s)

def t_5s():
    inject_stream(make_stream_chunks(content_text="延迟"), delay_ms=5000)
    code, out, err, el = run_jcc("5秒", await_sec=20)
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("5秒延迟", t_5s)

# ============================================================
# 13. 并发
# ============================================================
print("\n=== 13. 并发 ===")

def t_concurrent5():
    errors = []
    def run_one(idx):
        if not mock_ensure(): errors.append(f"进程{idx}: MockServer不可用"); return
        try:
            inject_simple(f"并发{idx}")
            c, o, e, el = run_jcc(f"并发{idx}")
            if c != 0: errors.append(f"进程{idx} exit={c}")
        except Exception as ex: errors.append(f"进程{idx}: {ex}")
    threads = [threading.Thread(target=run_one, args=(i,)) for i in range(5)]
    for t in threads: t.start()
    for t in threads: t.join(timeout=60)
    if errors: raise AssertionError("; ".join(errors[:3]))
test("5进程并发", t_concurrent5)

# ============================================================
# 14. 子命令（不需要 MockServer）
# ============================================================
print("\n=== 14. 子命令 ===")

def t_schema():
    code, out, err, el = run_jcc_cmd(["schema"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("schema", t_schema, need_mock=False)

def t_config_print():
    code, out, err, el = run_jcc_cmd(["config-print"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("config-print", t_config_print, need_mock=False)

def t_mcp_list():
    code, out, err, el = run_jcc_cmd(["mcp_list"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("mcp_list", t_mcp_list, need_mock=False)

def t_slash_list():
    code, out, err, el = run_jcc_cmd(["slash_list"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("slash_list", t_slash_list, need_mock=False)

def t_slash_help():
    code, out, err, el = run_jcc_cmd(["slash_call", "/help", "{}"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("/help", t_slash_help, need_mock=False)

def t_rg():
    code, out, err, el = run_jcc_cmd(["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("rg 搜索", t_rg, need_mock=False)

# ============================================================
# 15. 环境变量/选项
# ============================================================
print("\n=== 15. 环境变量/选项 ===")

def t_json():
    inject_simple("JSON")
    code, out, err, el = run_jcc("json", extra_args=["--json"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("--json", t_json)

def t_quiet():
    inject_simple("quiet")
    code, out, err, el = run_jcc("quiet", extra_args=["--quiet"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("--quiet", t_quiet)

def t_allowed():
    inject_simple("allowed")
    code, out, err, el = run_jcc("allowed", extra_args=["--allowed-tools", "read"])
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("--allowed-tools", t_allowed)

def t_model_switch():
    inject_simple("model")
    code, out, err, el = run_jcc("model", env_override={"JCC_MODEL_ID": "gpt-4o"})
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("切换model", t_model_switch)

def t_max_output():
    inject_simple("A" * 10000)
    code, out, err, el = run_jcc("截断", env_override={"JCC_MAX_OUTPUT_CHARS": "1000"})
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("JCC_MAX_OUTPUT_CHARS", t_max_output)

# ============================================================
# 16. 供应商切换
# ============================================================
print("\n=== 16. 供应商切换 ===")

def t_vendor_anthropic():
    inject_simple("anthropic")
    code, out, err, el = run_jcc("anthropic", env_override={"JCC_VENDOR": "anthropic", "JCC_MODEL_ID": "claude-opus-5"})
test("anthropic供应商", t_vendor_anthropic)

def t_vendor_deepseek():
    inject_simple("deepseek")
    code, out, err, el = run_jcc("deepseek", env_override={"JCC_VENDOR": "deepseek", "JCC_MODEL_ID": "deepseek-v4-flash"})
test("deepseek供应商", t_vendor_deepseek)

# ============================================================
# 17. 非流式回退
# ============================================================
print("\n=== 17. 非流式回退 ===")

def t_fallback():
    body = json.dumps({"id": "x", "object": "chat.completion", "choices": [{"index": 0, "message": {"role": "assistant", "content": "回退成功"}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}})
    inject_non_stream(body)
    inject_non_stream(body)  # 回退请求
    code, out, err, el = run_jcc("回退")
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
    assert_contains(out, "回退成功", "应包含回退内容")
test("流式回退到非流式", t_fallback)

# ============================================================
# 18. 上下文压缩
# ============================================================
print("\n=== 18. 上下文压缩 ===")

def t_small_window():
    inject_simple("A" * 5000)
    code, out, err, el = run_jcc("压缩", env_override={"JCC_MAX_CONTEXT_TOKENS": "500"})
    assert_eq(code, 0, f"exit, stderr={err[-100:]}")
test("小上下文窗口", t_small_window)

# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("测试汇总")
print("=" * 60)
passed = sum(1 for _, s, _, _ in results if s == "PASS")
failed = sum(1 for _, s, _, _ in results if s == "FAIL")
errors = sum(1 for _, s, _, _ in results if s == "ERROR")
skipped = sum(1 for _, s, _, _ in results if s == "SKIP")
total = len(results)
print(f"总计: {total} | 通过: {passed} | 失败: {failed} | 错误: {errors} | 跳过: {skipped}")
print()
if failed > 0 or errors > 0:
    print("失败/错误详情:")
    for name, status, elapsed, msg in results:
        if status not in ("PASS", "SKIP"):
            icon = "✗" if status == "FAIL" else "!!"
            print(f"  {icon} {name} ({elapsed:.1f}s) — {msg[:150]}")

Path(".xxx/logs/v5_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

# 关闭 MockServer
if mock_proc:
    mock_proc.terminate()
    try: mock_proc.wait(timeout=5)
    except: mock_proc.kill()

sys.exit(0 if failed == 0 and errors == 0 else 1)
