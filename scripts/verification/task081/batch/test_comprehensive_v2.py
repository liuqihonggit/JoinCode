#!/usr/bin/env python3
"""jcc 全面功能测试 v2 — 修正注入格式"""
import subprocess, json, time, urllib.request, sys, os, threading, uuid
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK = "http://localhost:9901"
MODEL = "gpt-5.6-sol"
ENV = {
    **os.environ,
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": MODEL,
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
}

results = []
lock = threading.Lock()

def sse_line(obj):
    """构造 SSE 行: data: {json}\n\n"""
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop", no_done=False):
    """构造 SSE 流式 chunk 列表"""
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    
    # 首个 role chunk
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    
    # reasoning_content
    if reasoning:
        if isinstance(reasoning, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": reasoning}}]}))
        else:
            for r in reasoning:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    
    # content
    if content_text is not None:
        if isinstance(content_text, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
        else:
            for c in content_text:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": c}}]}))
    
    # tool_calls
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    
    # finish
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    
    if not no_done:
        chunks.append("data: [DONE]\n\n")
    
    return chunks

def inject_stream(chunks, status=200, headers=None, delay_ms=0):
    """注入流式响应"""
    body = {"status_code": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0:
        body["delay_ms"] = delay_ms
    if headers:
        body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        urllib.request.urlopen(req, timeout=5)
    except Exception as e:
        print(f"  INJECT ERROR: {e}")

def inject_non_stream(body_text, status=200, headers=None):
    """注入非流式响应"""
    body = {"status_code": status, "is_stream": False, "body": body_text, "content_type": "application/json"}
    if headers:
        body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        urllib.request.urlopen(req, timeout=5)
    except Exception as e:
        print(f"  INJECT ERROR: {e}")

def inject_simple(text="完成", **kwargs):
    """注入简单流式文本响应"""
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

def inject_tool_call(tool_name, tool_args, result_text="工具执行完成"):
    """注入工具调用响应：第一轮返回 tool_calls，第二轮返回结果"""
    inject_stream(make_stream_chunks(
        tool_calls=[{"id": "call_1", "name": tool_name, "args": tool_args}],
        finish_reason="tool_calls"
    ))
    inject_simple(result_text)

def clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try:
        urllib.request.urlopen(req, timeout=5)
    except: pass

def get_state():
    try:
        req = urllib.request.Request(f"{MOCK}/__control__/state")
        resp = urllib.request.urlopen(req, timeout=5)
        return json.loads(resp.read())
    except:
        return {}

def run_jcc(prompt, await_sec=30, extra_args=None):
    """运行 jcc 并返回 (exit_code, stdout, stderr, elapsed)"""
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args:
        args.extend(extra_args)
    t0 = time.time()
    try:
        proc = subprocess.run(args, env=ENV, capture_output=True, text=True, timeout=await_sec+10)
        elapsed = time.time() - t0
        return proc.returncode, proc.stdout, proc.stderr, elapsed
    except subprocess.TimeoutExpired as e:
        elapsed = time.time() - t0
        return -999, (e.stdout or b"").decode() if isinstance(e.stdout, bytes) else (e.stdout or ""), (e.stderr or b"").decode() if isinstance(e.stderr, bytes) else (e.stderr or ""), elapsed

def test(name, func):
    clear()
    t0 = time.time()
    try:
        func()
        elapsed = time.time() - t0
        with lock:
            results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        with lock:
            results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:150]}")
    except Exception as e:
        elapsed = time.time() - t0
        with lock:
            results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:150]}")
    clear()

def assert_eq(a, b, msg=""):
    if a != b:
        raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text:
        raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

def assert_not_contains(text, substr, msg=""):
    if substr in text:
        raise AssertionError(f"{msg}: '{substr}' found in output")

# ============================================================
# 1. 基础流式响应
# ============================================================
print("\n=== 1. 基础流式响应 ===")

def test_basic_stream():
    inject_simple("你好世界")
    code, out, err, elapsed = run_jcc("你好")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
    assert_contains(out, "你好", "输出应包含响应内容")
test("基础流式响应", test_basic_stream)

def test_multi_chunk():
    inject_stream(make_stream_chunks(content_text=["你好", "世界", "！"]))
    code, out, err, elapsed = run_jcc("测试多chunk")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("多 chunk 流式", test_multi_chunk)

def test_long_content():
    long_text = "这是一段很长的文本。" * 200
    inject_simple(long_text)
    code, out, err, elapsed = run_jcc("生成长文本")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("超长内容(2000字)", test_long_content)

def test_unicode_emoji():
    inject_simple("🎉🚀✨💡🔥\n\n你好世界！\n\t缩进\t\"引号\" '单引号'")
    code, out, err, elapsed = run_jcc("测试Unicode")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("Unicode+Emoji", test_unicode_emoji)

# ============================================================
# 2. 工具调用
# ============================================================
print("\n=== 2. 工具调用 ===")

def test_single_tool():
    inject_tool_call("read_file", {"filePath": "test.cs"}, "文件读取完成")
    code, out, err, elapsed = run_jcc("读取文件")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("单工具调用", test_single_tool)

def test_two_tools_parallel():
    chunks = make_stream_chunks(
        tool_calls=[
            {"id": "call_1", "name": "read_file", "args": {"filePath": "a.cs"}},
            {"id": "call_2", "name": "grep", "args": {"pattern": "class"}}
        ],
        finish_reason="tool_calls"
    )
    inject_stream(chunks)
    inject_simple("两个工具完成")
    code, out, err, elapsed = run_jcc("同时读取和搜索")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("双工具并行", test_two_tools_parallel)

def test_three_tools():
    chunks = make_stream_chunks(
        tool_calls=[
            {"id": "call_1", "name": "read_file", "args": {"filePath": "a.cs"}},
            {"id": "call_2", "name": "glob", "args": {"pattern": "**/*.ts"}},
            {"id": "call_3", "name": "git_status", "args": {}}
        ],
        finish_reason="tool_calls"
    )
    inject_stream(chunks)
    inject_simple("三个工具完成")
    code, out, err, elapsed = run_jcc("执行三个工具")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("三工具并行", test_three_tools)

# ============================================================
# 3. 思考链
# ============================================================
print("\n=== 3. 思考链 ===")

def test_reasoning():
    inject_stream(make_stream_chunks(
        reasoning=["让我思考...", "答案是42"],
        content_text="答案是42"
    ))
    code, out, err, elapsed = run_jcc("思考并回答")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("reasoning_content", test_reasoning)

def test_large_reasoning():
    big = "思考" * 2500
    inject_stream(make_stream_chunks(reasoning=big, content_text="结论"))
    code, out, err, elapsed = run_jcc("大量思考")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("超大思考链(5KB)", test_large_reasoning)

def test_reasoning_plus_tool():
    chunks = make_stream_chunks(
        reasoning=["需要读取文件"],
        tool_calls=[{"id": "call_1", "name": "read_file", "args": {"filePath": "test.cs"}}],
        finish_reason="tool_calls"
    )
    inject_stream(chunks)
    inject_simple("读取完成")
    code, out, err, elapsed = run_jcc("思考后调用工具")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("思考链+工具调用", test_reasoning_plus_tool)

# ============================================================
# 4. 边缘条件
# ============================================================
print("\n=== 4. 边缘条件 ===")

def test_empty_content():
    inject_stream(make_stream_chunks(content_text="", finish_reason="stop"))
    code, out, err, elapsed = run_jcc("空内容")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("空内容响应", test_empty_content)

def test_null_content_chunk():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": None}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "有效"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("null content")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("null content chunk", test_null_content_chunk)

def test_no_done():
    inject_stream(make_stream_chunks(content_text="无DONE", no_done=True))
    code, out, err, elapsed = run_jcc("无DONE标记")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("无[DONE]标记", test_no_done)

def test_empty_tool_calls():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "开始"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": []}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "结束"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("空tool_calls")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("空tool_calls数组", test_empty_tool_calls)

def test_malformed_chunk():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "正常"}}]}),
        "data: NOT_VALID_JSON\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "恢复"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("畸形JSON")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("畸形JSON容错", test_malformed_chunk)

def test_sse_comment_lines():
    chunks = [
        ": keep-alive comment\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        ": another comment\n\n",
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("SSE注释行")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("SSE注释行", test_sse_comment_lines)

def test_done_with_trailing_space():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]  \n\n",
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("DONE带空格")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("[DONE]带尾随空格", test_done_with_trailing_space)

def test_extra_data_after_done():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "额外"}}]}),
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("DONE后有数据")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("[DONE]后有额外数据", test_extra_data_after_done)

def test_multi_choices():
    chunks = [
        sse_line({"choices": [
            {"index": 0, "delta": {"content": "选择1"}},
            {"index": 1, "delta": {"content": "选择2"}}
        ]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("多choices")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("多choices(n>1)", test_multi_choices)

def test_finish_reason_length():
    inject_stream(make_stream_chunks(content_text="部分内容", finish_reason="length"))
    code, out, err, elapsed = run_jcc("finish_reason=length")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("finish_reason=length", test_finish_reason_length)

def test_finish_reason_content_filter():
    inject_stream(make_stream_chunks(content_text="", finish_reason="content_filter"))
    code, out, err, elapsed = run_jcc("content_filter")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("finish_reason=content_filter", test_finish_reason_content_filter)

# ============================================================
# 5. 错误处理
# ============================================================
print("\n=== 5. 错误处理 ===")

def test_404():
    inject_non_stream('{"error":{"message":"not found","code":"404"}}', status=404)
    code, out, err, elapsed = run_jcc("404错误")
    assert code != 0, f"404应非零退出, got {code}"
test("404错误", test_404)

def test_401():
    inject_non_stream('{"error":{"message":"unauthorized","code":"401"}}', status=401)
    code, out, err, elapsed = run_jcc("401错误")
    assert code != 0, f"401应非零退出, got {code}"
test("401错误", test_401)

def test_500():
    inject_non_stream('{"error":{"message":"server error","code":"500"}}', status=500)
    code, out, err, elapsed = run_jcc("500错误")
    assert code != 0, f"500应非零退出, got {code}"
test("500错误", test_500)

def test_429():
    inject_non_stream('{"error":{"message":"rate limited","code":"429"}}', status=429,
                      headers={"Retry-After": "1"})
    code, out, err, elapsed = run_jcc("429限流")
    assert code != 0, f"429应非零退出, got {code}"
test("429限流+Retry-After", test_429)

def test_429_no_header():
    inject_non_stream('{"error":{"message":"rate limited","code":"429"}}', status=429)
    code, out, err, elapsed = run_jcc("429无RetryAfter")
    assert code != 0, f"429应非零退出, got {code}"
test("429限流无Retry-After", test_429_no_header)

def test_503():
    inject_non_stream('{"error":{"message":"service unavailable","code":"503"}}', status=503)
    code, out, err, elapsed = run_jcc("503错误")
    assert code != 0, f"503应非零退出, got {code}"
test("503服务不可用", test_503)

# ============================================================
# 6. 非流式响应
# ============================================================
print("\n=== 6. 非流式响应 ===")

def test_non_stream_response():
    body = json.dumps({
        "id": "chatcmpl-1",
        "object": "chat.completion",
        "choices": [{"index": 0, "message": {"role": "assistant", "content": "非流式响应"}, "finish_reason": "stop"}],
        "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}
    })
    inject_non_stream(body)
    code, out, err, elapsed = run_jcc("非流式测试")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("非流式响应", test_non_stream_response)

def test_non_stream_with_tool():
    body = json.dumps({
        "id": "chatcmpl-2",
        "object": "chat.completion",
        "choices": [{"index": 0, "message": {
            "role": "assistant",
            "content": None,
            "tool_calls": [{"id": "call_1", "type": "function", "function": {"name": "read_file", "arguments": json.dumps({"filePath": "test.cs"})}}]
        }, "finish_reason": "tool_calls"}],
        "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}
    })
    inject_non_stream(body)
    inject_simple("工具完成")
    code, out, err, elapsed = run_jcc("非流式工具调用")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("非流式+工具调用", test_non_stream_with_tool)

# ============================================================
# 7. 斜杠命令
# ============================================================
print("\n=== 7. 斜杠命令 ===")

def test_slash_help():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_call", "/help", "{}"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("/help", test_slash_help)

def test_slash_list():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_list"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("slash_list", test_slash_list)

def test_slash_schema_compact():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_schema", "/compact"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("slash_schema /compact", test_slash_schema_compact)

def test_slash_schema_status():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_schema", "/status"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("slash_schema /status", test_slash_schema_status)

# ============================================================
# 8. MCP 工具
# ============================================================
print("\n=== 8. MCP 工具 ===")

def test_mcp_list():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_list"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("mcp_list", test_mcp_list)

def test_mcp_list_github():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_list", "--category", "github"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("mcp_list github", test_mcp_list_github)

def test_mcp_search():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_search", "pr"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("mcp_search", test_mcp_search)

def test_mcp_schema():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_schema", "gh_pr_view"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("mcp_schema gh_pr_view", test_mcp_schema)

# ============================================================
# 9. 延迟响应
# ============================================================
print("\n=== 9. 延迟响应 ===")

def test_2s_delay():
    inject_stream(make_stream_chunks(content_text="延迟响应"), delay_ms=2000)
    code, out, err, elapsed = run_jcc("2秒延迟")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("2秒延迟", test_2s_delay)

def test_5s_delay():
    inject_stream(make_stream_chunks(content_text="5秒延迟"), delay_ms=5000)
    code, out, err, elapsed = run_jcc("5秒延迟", await_sec=30)
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("5秒延迟", test_5s_delay)

# ============================================================
# 10. 并发
# ============================================================
print("\n=== 10. 并发 ===")

def test_concurrent_3():
    errors = []
    def run_one(idx):
        try:
            inject_simple(f"并发{idx}")
            c, o, e, el = run_jcc(f"并发测试{idx}")
            if c != 0:
                errors.append(f"进程{idx} exit={c}")
        except Exception as ex:
            errors.append(f"进程{idx}异常: {ex}")
    
    threads = []
    for i in range(3):
        t = threading.Thread(target=run_one, args=(i,))
        threads.append(t)
        t.start()
    for t in threads:
        t.join(timeout=60)
    if errors:
        raise AssertionError("; ".join(errors))
test("3进程并发", test_concurrent_3)

# ============================================================
# 11. 逐字符流式
# ============================================================
print("\n=== 11. 逐字符流式 ===")

def test_char_by_char():
    text = "Hello World 你好世界"
    chunks = [sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]})]
    for ch in text:
        chunks.append(sse_line({"choices": [{"index": 0, "delta": {"content": ch}}]}))
    chunks.append(sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}))
    chunks.append("data: [DONE]\n\n")
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("逐字符")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("逐字符流式(20+chunk)", test_char_by_char)

# ============================================================
# 12. 混合 content + tool_calls
# ============================================================
print("\n=== 12. 混合响应 ===")

def test_content_then_tool():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "我来读取文件"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "call_1", "function": {"name": "read_file", "arguments": json.dumps({"filePath": "test.cs"})}}]}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    inject_simple("读取完成")
    code, out, err, elapsed = run_jcc("先文本后工具")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("content+tool_calls混合", test_content_then_tool)

def test_tool_then_content():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "call_1", "function": {"name": "read_file", "arguments": json.dumps({"filePath": "test.cs"})}}]}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "正在读取"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    inject_simple("完成")
    code, out, err, elapsed = run_jcc("先工具后文本")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("tool_calls+content混合", test_tool_then_content)

# ============================================================
# 13. 额外字段
# ============================================================
print("\n=== 13. 额外字段 ===")

def test_chunk_with_extra_fields():
    chunks = [
        sse_line({"id": "x", "object": "chat.completion.chunk", "created": 1234567890, "model": "test", "system_fingerprint": "fp_123", "choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("额外字段")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("chunk含额外字段", test_chunk_with_extra_fields)

def test_usage_in_last_chunk():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150}}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("usage在最后chunk")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("usage在末尾chunk", test_usage_in_last_chunk)

# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("测试汇总")
print("=" * 60)
passed = sum(1 for _, s, _, _ in results if s == "PASS")
failed = sum(1 for _, s, _, _ in results if s == "FAIL")
errors = sum(1 for _, s, _, _ in results if s == "ERROR")
total = len(results)
print(f"总计: {total} | 通过: {passed} | 失败: {failed} | 错误: {errors}")
print()
for name, status, elapsed, msg in results:
    icon = "✓" if status == "PASS" else "✗" if status == "FAIL" else "!!"
    print(f"  {icon} {name} ({elapsed:.1f}s)" + (f" — {msg[:120]}" if msg else ""))

Path(".xxx/logs/comprehensive_v2_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
